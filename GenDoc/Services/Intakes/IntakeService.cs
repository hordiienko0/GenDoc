using GenDoc.Data;
using GenDoc.Models;
using GenDoc.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.Services.Intakes
{
    // Фіксовані назви папок усередині кожного набору - на них орієнтується і
    // створення набору (IntakeService.CreateAsync), і розкладання людей за
    // придатністю при імпорті (ImportService).
    public static class IntakeFolderNames
    {
        public const string All = "Всі";
        public const string Fit = "Придатні";
        public const string LimitedFit = "Обмежено придатні";
        public const string Unfit = "Непридатні";

        /// <summary>Категорію ще не проставили. Окрема папка, а не «Всі»: так
        /// одразу видно, кому бракує висновку, і людина не вдає обмежено
        /// придатну (рішення користувача 2026-08-31).</summary>
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

            // Автоперехід статусів за датами: Planned → Active → Completed.
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

                // Фіксована структура: корінь набору → «Всі» → «Придатні», «Обмежено придатні».
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

                // По підпапці на кожну категорію придатності - перелік і порядок
                // задає IntakeFitnessFolders, щоб майстер, імпорт і переїзд за
                // зміною статусу спиралися на одне правило.
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
                    $"{categoryNodes.Count + 1} папки (Всі; {folderList}), "
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

            // Активний набір ОДИН на всю базу й визначається датами: якщо новий
            // набір уже почався, Status вище виставлено Active, і GetActiveAsync
            // віддасть саме його (номер найбільший). Персонального вибору більше
            // немає - «Зробити моїм» прибрано (рішення користувача 2026-08-31).
            // Поза транзакцією: оновлення екранного стану - не частина цілісності
            // дерева, і його невдача не має відкочувати створений набір.
            try
            {
                await _serviceProvider.GetRequiredService<ActiveIntakeState>().RefreshAsync();
            }
            catch (Exception ex)
            {
                // Набір уже створено й закомічено. Мовчати не можна - інакше
                // статус-рядок «іноді не оновлюється» без жодного сліду.
                ErrorLog.Write(ex, _currentUserContext.CurrentUserFullName);
            }

            return intake;
        }

        private static async Task<int> GetNextNumberInternalAsync(AppDbContext db)
            => (await db.Intakes.IgnoreQueryFilters().MaxAsync(i => (int?)i.Number) ?? 0) + 1;

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

            // Один груповий запит на всі набори - інакше N наборів = N запитів на екрані.
            var peopleCounts = await db.Recipients
                .Where(r => r.IntakeId != null)
                .GroupBy(r => r.IntakeId!.Value)
                .Select(g => new { IntakeId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.IntakeId, x => x.Count);

            return intakes.Select(i => new IntakeOverview(
                i.Id, i.Number, i.DisplayNumber, i.Status, i.DateStart, i.DateEnd, i.DateClosed,
                i.RootOrgNodeId, peopleCounts.GetValueOrDefault(i.Id),
                CompletenessPercent: -1, IncompletePeopleCount: -1,
                HasPackage: i.DefaultPackageId is not null)).ToList();
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
            if (intake.DefaultPackageId is int packageId)
            {
                var completenessService = _serviceProvider.GetRequiredService<Completeness.ICompletenessService>();
                var summary = await completenessService.GetIntakeSummaryAsync(intakeId, packageId);
                incompletePeopleCount = summary.IncompletePeople;
            }

            var graduateNodes = await db.OrgNodes.AsNoTracking()
                .Where(n => n.IntakeId == null && n.Depth <= 1)
                .OrderBy(n => n.Path)
                .Select(n => new { n.Id, n.Name })
                .ToListAsync();

            var targets = new List<(int NodeId, string Name)> { (-1, "Створити папку «Випускники»") };
            targets.AddRange(graduateNodes.Select(n => (n.Id, n.Name)));

            return new IntakeCloseInfo(intakeId, intake.DisplayNumber, peopleCount, incompletePeopleCount, occupiedRoomCount, targets);
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

                    var movedCount = await db.Recipients.CountAsync(r => r.IntakeId == request.IntakeId);
                    await db.Recipients.Where(r => r.IntakeId == request.IntakeId)
                        .ExecuteUpdateAsync(s => s.SetProperty(r => r.OrgNodeId, node.Id));
                    detailsParts.Add($"переміщено {movedCount} осіб");
                }

                if (request.ReleaseRooms)
                {
                    var roomCount = await db.Recipients
                        .Where(r => r.IntakeId == request.IntakeId && r.RoomId != null)
                        .Select(r => r.RoomId!.Value).Distinct().CountAsync();
                    await db.Recipients.Where(r => r.IntakeId == request.IntakeId && r.RoomId != null)
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

        public async Task ReopenAsync(int intakeId)
        {
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
