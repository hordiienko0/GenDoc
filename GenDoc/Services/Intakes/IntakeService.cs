using GenDoc.Data;
using GenDoc.Models;
using GenDoc.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.Services.Intakes
{
    public static class IntakeFolderNames
    {
        public const string All = "Всі";
        public const string Fit = "Придатні";
        public const string LimitedFit = "Обмежено придатні";

        public const string NoCategory = "Без категорії";
    }

    public class IntakeService : IIntakeService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly IAuditLogService _auditLogService;
        private readonly ICurrentUserContext _currentUserContext;
        private readonly IServiceProvider _serviceProvider;

        public IntakeService(
            IDbContextFactory<AppDbContext> dbFactory,
            IAuditLogService auditLogService,
            ICurrentUserContext currentUserContext,
            IServiceProvider serviceProvider)
        {
            _dbFactory = dbFactory;
            _auditLogService = auditLogService;
            _currentUserContext = currentUserContext;
            _serviceProvider = serviceProvider;
        }

        public async Task<Intake?> GetActiveAsync()
        {
            using var db = _dbFactory.CreateDbContext();

            var today = DateOnly.FromDateTime(DateTime.Today);
            await db.Intakes
                .Where(i => i.Status == IntakeStatus.Planned && i.DateStart <= today && i.DateEnd >= today && !i.StatusIsPinned)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, IntakeStatus.Active));
            await db.Intakes
                .Where(i => i.Status == IntakeStatus.Active && i.DateEnd < today && !i.StatusIsPinned)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, IntakeStatus.Completed));

            return await db.Intakes.AsNoTracking()
                .Where(i => i.Status == IntakeStatus.Active)
                .OrderByDescending(i => i.Number)
                .FirstOrDefaultAsync();
        }

        public async Task<Intake?> GetByIdAsync(int id)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.Intakes.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
        }

        public async Task<int> GetNextNumberAsync()
        {
            using var db = _dbFactory.CreateDbContext();
            var max = await db.Intakes.IgnoreQueryFilters().MaxAsync(i => (int?)i.Number) ?? 0;
            return max + 1;
        }

        public async Task<string> GetNumberTemplateAsync()
        {
            using var db = _dbFactory.CreateDbContext();
            var template = await db.AppSettings.Select(s => s.IntakeNumberTemplate).FirstOrDefaultAsync();
            return string.IsNullOrWhiteSpace(template) ? "Набір №{n}" : template;
        }

        public async Task<Intake> CreateAsync(IntakeCreateRequest request)
        {
            using var db = _dbFactory.CreateDbContext();
            var baseNode = await db.OrgNodes.FirstAsync(n => n.Id == request.BaseNodeId);

            var today = DateOnly.FromDateTime(DateTime.Today);
            var intake = new Intake
            {
                Number = await GetNextNumberInternalAsync(db),
                DisplayNumber = request.DisplayNumber.Trim(),
                DateStart = request.DateStart,
                DateEnd = request.DateEnd,
                Status = request.DateStart <= today && today <= request.DateEnd
                    ? IntakeStatus.Active
                    : IntakeStatus.Planned,
                DefaultPackageId = await _serviceProvider.GetRequiredService<Completeness.ICompletenessService>()
                    .GetDefaultPackageIdAsync()
            };

            async Task ApplyAsync()
            {
                db.Intakes.Add(intake);
                await db.SaveChangesAsync();

                var rootNode = new OrgNode
                {
                    Name = intake.DisplayNumber,
                    ParentId = baseNode.Id,
                    Depth = baseNode.Depth + 1,
                    SortOrder = await db.OrgNodes.Where(n => n.ParentId == baseNode.Id)
                        .Select(n => (int?)n.SortOrder).MaxAsync() ?? -1,
                    IntakeId = intake.Id
                };
                rootNode.SortOrder += 1;
                db.OrgNodes.Add(rootNode);
                await db.SaveChangesAsync();
                rootNode.Path = $"{baseNode.Path}{rootNode.Id}/";

                var allNode = new OrgNode
                {
                    Name = IntakeFolderNames.All,
                    ParentId = rootNode.Id,
                    Depth = rootNode.Depth + 1,
                    SortOrder = 0,
                    IntakeId = intake.Id
                };
                db.OrgNodes.Add(allNode);
                await db.SaveChangesAsync();
                allNode.Path = $"{rootNode.Path}{allNode.Id}/";

                var categoryNodes = new List<OrgNode>();
                for (var i = 0; i < IntakeFitnessFolders.AllFolderNames.Count; i++)
                {
                    var node = new OrgNode
                    {
                        Name = IntakeFitnessFolders.AllFolderNames[i],
                        ParentId = allNode.Id,
                        Depth = allNode.Depth + 1,
                        SortOrder = i,
                        IntakeId = intake.Id
                    };
                    categoryNodes.Add(node);
                    db.OrgNodes.Add(node);
                }

                await db.SaveChangesAsync();
                foreach (var node in categoryNodes)
                    node.Path = $"{allNode.Path}{node.Id}/";

                intake.RootOrgNodeId = rootNode.Id;
                var folderList = string.Join(", ", IntakeFitnessFolders.AllFolderNames);
                _auditLogService.Log(db, "Створено набір", "Intake", intake.Id, null, intake.DisplayNumber,
                    $"{IntakeLabel.Of(intake.Number, intake.DisplayNumber)} (внутрішній №{intake.Number}): "
                    + $"{categoryNodes.Count + 1} папки (Всі; {folderList}), "
                    + $"{request.DateStart:dd.MM.yyyy} - {request.DateEnd:dd.MM.yyyy}");
                await db.SaveChangesAsync();
            }

            if (db.Database.CurrentTransaction is null)
            {
                using var tx = await db.Database.BeginTransactionAsync();
                await ApplyAsync();
                await tx.CommitAsync();
            }
            else
            {
                await ApplyAsync();
            }

            try
            {
                await _serviceProvider.GetRequiredService<ActiveIntakeState>().RefreshAsync();
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex, _currentUserContext.CurrentUserFullName);
            }

            return intake;
        }

        private static async Task<int> GetNextNumberInternalAsync(AppDbContext db)
            => (await db.Intakes.IgnoreQueryFilters().MaxAsync(i => (int?)i.Number) ?? 0) + 1;

        public async Task<int?> GetBaseNodeIdAsync(int intakeId)
        {
            using var db = _dbFactory.CreateDbContext();
            var rootId = await db.Intakes.Where(i => i.Id == intakeId)
                .Select(i => (int?)i.RootOrgNodeId).FirstOrDefaultAsync();
            if (rootId is not int id) return null;
            return await db.OrgNodes.Where(n => n.Id == id).Select(n => n.ParentId).FirstOrDefaultAsync();
        }

        /// <summary>Статус за періодом: ще не почався - запланований, триває - активний, минув - завершений.</summary>
        internal static IntakeStatus StatusForPeriod(DateOnly start, DateOnly end, DateOnly today)
            => today < start ? IntakeStatus.Planned
             : today > end ? IntakeStatus.Completed
             : IntakeStatus.Active;

        private static string StatusName(IntakeStatus status) => status switch
        {
            IntakeStatus.Active => "активний",
            IntakeStatus.Completed => "завершений",
            _ => "запланований"
        };

        /// <summary>
        /// Змінює назву, період і папку-основу набору. Назва набору - це й назва його кореневої папки,
        /// тож папка перейменовується разом із ним; решта розділів (журнал, архів, графіки, генерація)
        /// читають назву й дати з набору, тому бачать нові значення одразу.
        /// </summary>
        public async Task<IntakeUpdateResult> UpdateAsync(IntakeUpdateRequest request)
        {
            var displayNumber = request.DisplayNumber?.Trim() ?? string.Empty;
            if (displayNumber.Length == 0)
                throw new InvalidOperationException("Вкажіть номер набору.");
            if (request.DateEnd < request.DateStart)
                throw new InvalidOperationException("Дата завершення не може бути раніше за дату початку.");

            using var db = _dbFactory.CreateDbContext();
            var intake = await db.Intakes.FirstOrDefaultAsync(i => i.Id == request.IntakeId)
                ?? throw new InvalidOperationException("Набір не знайдено.");
            var root = await db.OrgNodes.FirstOrDefaultAsync(n => n.Id == intake.RootOrgNodeId)
                ?? throw new InvalidOperationException("Не знайдено папку набору.");
            var newBase = await db.OrgNodes.FirstOrDefaultAsync(n => n.Id == request.BaseNodeId)
                ?? throw new InvalidOperationException("Папку-основу не знайдено.");
            if (newBase.IntakeId is not null || newBase.Path.StartsWith(root.Path, StringComparison.Ordinal))
                throw new InvalidOperationException("Папкою-основою не може бути папка набору.");

            var oldLabel = IntakeLabel.Of(intake.Number, intake.DisplayNumber);
            var oldDisplayNumber = intake.DisplayNumber;
            var oldRootPath = await OrgTree.OrgPathBuilder.BuildAsync(db, root.Id);
            var newBasePath = await OrgTree.OrgPathBuilder.BuildAsync(db, newBase.Id);
            var newRootPath = newBasePath is null ? displayNumber : $"{newBasePath} / {displayNumber}";
            var changes = new List<string>();

            async Task ApplyAsync()
            {
                if (oldRootPath is not null && oldRootPath != newRootPath)
                    await RewriteOrgPathSnapshotsAsync(db, intake.Id, oldRootPath, newRootPath);

                if (intake.DisplayNumber != displayNumber)
                {
                    changes.Add($"назва: «{intake.DisplayNumber}» → «{displayNumber}»");
                    intake.DisplayNumber = displayNumber;
                }
                root.Name = displayNumber;

                if (intake.DateStart != request.DateStart || intake.DateEnd != request.DateEnd)
                {
                    changes.Add($"період: {intake.DateStart:dd.MM.yyyy} - {intake.DateEnd:dd.MM.yyyy} → "
                        + $"{request.DateStart:dd.MM.yyyy} - {request.DateEnd:dd.MM.yyyy}");
                    intake.DateStart = request.DateStart;
                    intake.DateEnd = request.DateEnd;

                    // Закритий вручну набір лишається закритим; інакше статус іде за новим періодом.
                    if (intake.DateClosed is null)
                    {
                        var status = StatusForPeriod(request.DateStart, request.DateEnd, DateOnly.FromDateTime(DateTime.Today));
                        if (status != intake.Status)
                            changes.Add($"статус: {StatusName(intake.Status)} → {StatusName(status)}");
                        intake.Status = status;
                        intake.StatusIsPinned = false;
                    }
                }

                if (root.ParentId != newBase.Id)
                {
                    var oldBaseName = root.ParentId is int oldBaseId
                        ? await db.OrgNodes.IgnoreQueryFilters().Where(n => n.Id == oldBaseId)
                            .Select(n => n.Name).FirstOrDefaultAsync()
                        : null;
                    await MoveBranchAsync(db, root, newBase);
                    changes.Add($"папка-основа: «{oldBaseName}» → «{newBase.Name}»");
                }

                if (changes.Count > 0)
                    _auditLogService.Log(db, "Змінено набір", "Intake", intake.Id, oldLabel,
                        IntakeLabel.Of(intake.Number, intake.DisplayNumber), string.Join("; ", changes));
                await db.SaveChangesAsync();
            }

            if (db.Database.CurrentTransaction is null)
            {
                using var tx = await db.Database.BeginTransactionAsync();
                await ApplyAsync();
                await tx.CommitAsync();
            }
            else
            {
                await ApplyAsync();
            }

            var warning = oldDisplayNumber == displayNumber
                ? null
                : await RenameDocumentFolderAsync(intake, oldDisplayNumber);

            try
            {
                await _serviceProvider.GetRequiredService<ActiveIntakeState>().RefreshAsync();
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex, _currentUserContext.CurrentUserFullName);
            }

            return new IntakeUpdateResult(intake, warning);
        }

        /// <summary>
        /// «Підрозділ» документів набору - знімок шляху в дереві на момент генерації. Частина шляху до кореневої
        /// папки набору (папка-основа й назва) береться нова, підпапки всередині набору лишаються як були.
        /// </summary>
        private static async Task RewriteOrgPathSnapshotsAsync(AppDbContext db, int intakeId, string oldRootPath, string newRootPath)
        {
            var docs = await db.GeneratedDocuments.IgnoreQueryFilters()
                .Where(d => d.IntakeId == intakeId && d.OrgPathSnapshot != null)
                .ToListAsync();
            foreach (var doc in docs)
            {
                if (doc.OrgPathSnapshot == oldRootPath)
                    doc.OrgPathSnapshot = newRootPath;
                else if (doc.OrgPathSnapshot!.StartsWith(oldRootPath + " / ", StringComparison.Ordinal))
                    doc.OrgPathSnapshot = newRootPath + doc.OrgPathSnapshot[oldRootPath.Length..];
            }
        }

        /// <summary>
        /// Після перейменування набору переносить теку з його документами у вихідній теці й переписує шляхи в базі.
        /// Помилка тут не скасовує вже збережених змін набору - лише повертає попередження.
        /// </summary>
        private async Task<string?> RenameDocumentFolderAsync(Intake intake, string oldDisplayNumber)
        {
            if (_serviceProvider.GetService<Generation.IOutputFolderService>() is not { } outputFolders) return null;

            Documents.IntakeFolderRename? rename = null;
            try
            {
                var root = await outputFolders.GetDefaultAsync();
                using var db = _dbFactory.CreateDbContext();
                rename = await Documents.IntakeDocumentFolder.RenameAsync(
                    db, root, intake.Id, oldDisplayNumber, intake.DisplayNumber);
                if (rename is null || !rename.HasChanges) return rename?.Warning;

                var details = rename.FolderMoved
                    ? "теку перейменовано"
                    : $"перенесено файлів: {rename.MovedFiles}";
                if (rename.KeptFiles.Count > 0) details += $", лишилось у старій теці: {rename.KeptFiles.Count}";
                _auditLogService.Log(db, "Перейменовано теку документів набору", "Intake", intake.Id,
                    rename.OldDirectory, rename.NewDirectory, details);
                await db.SaveChangesAsync();
                return rename.Warning;
            }
            catch (Exception ex)
            {
                rename?.Undo();
                ErrorLog.Write(ex, _currentUserContext.CurrentUserFullName);
                return $"Набір збережено, але теку з його документами перейменувати не вдалося: {ex.Message}";
            }
        }

        /// <summary>Переносить кореневу папку набору з усією гілкою (і видаленими в кошик теж) під іншу папку; набір гілки не змінюється.</summary>
        private static async Task MoveBranchAsync(AppDbContext db, OrgNode root, OrgNode newBase)
        {
            var oldPrefix = root.Path;
            var newPrefix = $"{newBase.Path}{root.Id}/";
            var depthDelta = newBase.Depth + 1 - root.Depth;

            var subtree = await db.OrgNodes.IgnoreQueryFilters()
                .Where(n => n.Path.StartsWith(oldPrefix) && n.Id != root.Id)
                .ToListAsync();

            root.SortOrder = (await db.OrgNodes.Where(n => n.ParentId == newBase.Id)
                .Select(n => (int?)n.SortOrder).MaxAsync() ?? -1) + 1;
            root.ParentId = newBase.Id;
            root.Path = newPrefix;
            root.Depth = newBase.Depth + 1;

            foreach (var node in subtree)
            {
                node.Path = string.Concat(newPrefix, node.Path.AsSpan(oldPrefix.Length));
                node.Depth += depthDelta;
            }
        }

        public async Task<IReadOnlyList<IntakeOverview>> GetOverviewsAsync()
        {
            using var db = _dbFactory.CreateDbContext();

            var today = DateOnly.FromDateTime(DateTime.Today);
            await db.Intakes
                .Where(i => i.Status == IntakeStatus.Planned && i.DateStart <= today && i.DateEnd >= today && !i.StatusIsPinned)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, IntakeStatus.Active));
            await db.Intakes
                .Where(i => i.Status == IntakeStatus.Active && i.DateEnd < today && !i.StatusIsPinned)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, IntakeStatus.Completed));

            var intakes = await db.Intakes.AsNoTracking().OrderByDescending(i => i.Number).ToListAsync();

            var peopleCounts = await db.Recipients
                .Where(r => r.IntakeId != null)
                .GroupBy(r => r.IntakeId!.Value)
                .Select(g => new { IntakeId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.IntakeId, x => x.Count);

            var completenessService = _serviceProvider.GetRequiredService<Completeness.ICompletenessService>();
            var overviews = new List<IntakeOverview>(intakes.Count);
            foreach (var i in intakes)
            {
                overviews.Add(new IntakeOverview(
                    i.Id, i.Number, i.DisplayNumber, i.Status, i.DateStart, i.DateEnd, i.DateClosed,
                    i.RootOrgNodeId, peopleCounts.GetValueOrDefault(i.Id),
                    CompletenessPercent: -1, IncompletePeopleCount: -1,
                    PackageId: await completenessService.GetDefaultPackageIdAsync(i.Id)));
            }
            return overviews;
        }

        public async Task<IReadOnlyList<int>> GetYearsAsync()
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.Intakes.Select(i => i.DateStart.Year).Distinct().OrderByDescending(y => y).ToListAsync();
        }

        public async Task<IntakeCloseInfo> GetCloseInfoAsync(int intakeId)
        {
            using var db = _dbFactory.CreateDbContext();
            var intake = await db.Intakes.AsNoTracking().FirstAsync(i => i.Id == intakeId);

            var peopleCount = await db.Recipients.CountAsync(r => r.IntakeId == intakeId);
            var occupiedRoomCount = await db.Recipients
                .Where(r => r.IntakeId == intakeId && r.RoomId != null)
                .Select(r => r.RoomId!.Value).Distinct().CountAsync();

            var incompletePeopleCount = 0;
            var completenessService = _serviceProvider.GetRequiredService<Completeness.ICompletenessService>();
            var packageId = await completenessService.GetDefaultPackageIdAsync(intakeId);
            if (packageId is int pid)
            {
                var summary = await completenessService.GetIntakeSummaryAsync(intakeId, pid);
                incompletePeopleCount = summary.IncompletePeople;
            }

            var graduateNodes = await db.OrgNodes.AsNoTracking()
                .Where(n => n.IntakeId == null && n.Depth <= 1)
                .OrderBy(n => n.Path)
                .Select(n => new { n.Id, n.Name })
                .ToListAsync();

            var targets = new List<(int NodeId, string Name)> { (-1, "Створити папку «Випускники»") };
            targets.AddRange(graduateNodes.Select(n => (n.Id, n.Name)));

            return new IntakeCloseInfo(intakeId, intake.DisplayNumber, peopleCount, incompletePeopleCount, occupiedRoomCount, targets, packageId);
        }

        public async Task CloseAsync(IntakeCloseRequest request)
        {
            using var db = _dbFactory.CreateDbContext();

            async Task ApplyAsync()
            {
                var intake = await db.Intakes.FirstAsync(i => i.Id == request.IntakeId);
                if (intake.Status == IntakeStatus.Completed)
                    throw new InvalidOperationException("Набір уже закрито.");

                var detailsParts = new List<string>();
                var peopleIds = await db.Recipients
                    .Where(r => r.IntakeId == request.IntakeId)
                    .Select(r => r.Id)
                    .ToListAsync();

                if (request.MovePersonnel)
                {
                    OrgNode targetParent;
                    if (request.TargetNodeId == -1)
                    {
                        targetParent = await db.OrgNodes.FirstOrDefaultAsync(
                                n => n.IntakeId == null && n.ParentId == null && n.Name == "Випускники")
                            ?? await CreateGraduatesRootAsync(db);
                    }
                    else
                    {
                        targetParent = await db.OrgNodes.FirstAsync(n => n.Id == request.TargetNodeId);
                    }

                    var node = new OrgNode
                    {
                        Name = intake.DisplayNumber,
                        ParentId = targetParent.Id,
                        Depth = targetParent.Depth + 1,
                        SortOrder = await db.OrgNodes.Where(n => n.ParentId == targetParent.Id)
                            .Select(n => (int?)n.SortOrder).MaxAsync() ?? -1,
                        IntakeId = null
                    };
                    node.SortOrder += 1;
                    db.OrgNodes.Add(node);
                    await db.SaveChangesAsync();
                    node.Path = $"{targetParent.Path}{node.Id}/";

                    await db.Recipients.Where(r => peopleIds.Contains(r.Id))
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(r => r.OrgNodeId, node.Id)
                            .SetProperty(r => r.IntakeId, (int?)null));
                    detailsParts.Add($"переміщено {peopleIds.Count} осіб");
                }

                if (request.ReleaseRooms)
                {
                    var roomCount = await db.Recipients
                        .Where(r => peopleIds.Contains(r.Id) && r.RoomId != null)
                        .Select(r => r.RoomId!.Value).Distinct().CountAsync();
                    await db.Recipients.Where(r => peopleIds.Contains(r.Id) && r.RoomId != null)
                        .ExecuteUpdateAsync(s => s.SetProperty(r => r.RoomId, (int?)null));
                    detailsParts.Add($"звільнено {roomCount} кімнат");
                }

                intake.Status = IntakeStatus.Completed;
                intake.DateClosed = DateOnly.FromDateTime(DateTime.Today);
                intake.ClosedBy = _currentUserContext.CurrentUserFullName;
                intake.StatusIsPinned = true;

                _auditLogService.Log(db, "Закрито набір", "Intake", intake.Id, null, intake.DisplayNumber,
                    detailsParts.Count > 0 ? string.Join(", ", detailsParts) : null);
                await db.SaveChangesAsync();
            }

            if (db.Database.CurrentTransaction is null)
            {
                using var tx = await db.Database.BeginTransactionAsync();
                await ApplyAsync();
                await tx.CommitAsync();
            }
            else
            {
                await ApplyAsync();
            }
        }

        private static async Task<OrgNode> CreateGraduatesRootAsync(AppDbContext db)
        {
            var node = new OrgNode
            {
                Name = "Випускники",
                ParentId = null,
                Depth = 0,
                SortOrder = await db.OrgNodes.Where(n => n.ParentId == null)
                    .Select(n => (int?)n.SortOrder).MaxAsync() ?? -1,
                IntakeId = null
            };
            node.SortOrder += 1;
            db.OrgNodes.Add(node);
            await db.SaveChangesAsync();
            node.Path = $"{node.Id}/";
            return node;
        }

        public async Task<string?> GetReopenConflictAsync(int intakeId)
        {
            using var db = _dbFactory.CreateDbContext();
            var intake = await db.Intakes.AsNoTracking().FirstOrDefaultAsync(i => i.Id == intakeId);
            if (intake is null) return null;

            var today = DateOnly.FromDateTime(DateTime.Today);
            if (intake.DateStart > today) return null;

            var active = await db.Intakes.AsNoTracking()
                .Where(i => i.Status == IntakeStatus.Active && i.Id != intakeId)
                .OrderByDescending(i => i.Number)
                .FirstOrDefaultAsync();
            return active is null
                ? null
                : $"Набір «{IntakeLabel.Of(active.Number, active.DisplayNumber)}» уже активний. " +
                  "Активним може бути лише один набір - спершу закрийте його.";
        }

        public async Task ReopenAsync(int intakeId)
        {
            if (await GetReopenConflictAsync(intakeId) is { } conflict)
                throw new InvalidOperationException(conflict);

            using var db = _dbFactory.CreateDbContext();

            async Task ApplyAsync()
            {
                var intake = await db.Intakes.FirstAsync(i => i.Id == intakeId);
                var today = DateOnly.FromDateTime(DateTime.Today);
                intake.Status = intake.DateStart > today ? IntakeStatus.Planned : IntakeStatus.Active;
                intake.DateClosed = null;
                intake.ClosedBy = null;
                intake.StatusIsPinned = true;

                _auditLogService.Log(db, "Відкрито повторно набір", "Intake", intake.Id, null, intake.DisplayNumber);
                await db.SaveChangesAsync();
            }

            if (db.Database.CurrentTransaction is null)
            {
                using var tx = await db.Database.BeginTransactionAsync();
                await ApplyAsync();
                await tx.CommitAsync();
            }
            else
            {
                await ApplyAsync();
            }
        }
    }
}
