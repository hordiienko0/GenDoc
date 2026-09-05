using System.IO;
using System.Security.Cryptography;
using GenDoc.Data;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Generation;
using GenDoc.Services.Intakes;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services.Documents
{
    public class DocumentArchiveService : IDocumentArchiveService
    {
        private const int DefaultMaxDocumentSizeKb = 5120;

        private const string NoContentMessage =
            "Файл цього документа не збережено в архіві - доступні лише його дані. " +
            "Сформуйте документ наново або завантажте файл вручну.";

        private const string RecordGoneMessage =
            "Цей документ уже відсутній в архіві - можливо, його видалили в іншому сеансі. " +
            "Оновіть список і спробуйте ще раз.";

        private const string DeletedRecipientMessage =
            "Особу переміщено в кошик - відновіть її, щоб перегенерувати";

        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly IAuditLogService _auditLogService;
        private readonly ICurrentUserContext _currentUserContext;
        private readonly ISecureTempFileService _tempFileService;
        private readonly IWatermarkService _watermarkService;
        private readonly IDocumentGenerationService _documentGenerationService;
        private readonly IDocumentHashService _documentHashService;

        public DocumentArchiveService(
            IDbContextFactory<AppDbContext> dbFactory,
            IAuditLogService auditLogService,
            ICurrentUserContext currentUserContext,
            ISecureTempFileService tempFileService,
            IWatermarkService watermarkService,
            IDocumentGenerationService documentGenerationService,
            IDocumentHashService documentHashService)
        {
            _dbFactory = dbFactory;
            _auditLogService = auditLogService;
            _currentUserContext = currentUserContext;
            _tempFileService = tempFileService;
            _watermarkService = watermarkService;
            _documentGenerationService = documentGenerationService;
            _documentHashService = documentHashService;
        }

        private static IQueryable<GeneratedDocument> ApplyFilter(AppDbContext db, ArchiveFilter filter)
        {
            var query = db.GeneratedDocuments
                .IgnoreQueryFilters()
                .Where(g => g.DeletedAt == null && g.IsCurrent);

            if (filter.IntakeId is int intakeId)
                query = query.Where(g => g.IntakeId == intakeId);
            if (filter.TemplateId is int templateId)
                query = query.Where(g => g.TemplateId == templateId);
            if (filter.PackageId is int packageId)
                query = query.Where(g => g.RunId != null
                    && db.GenerationPackageRuns.Any(r => r.Id == g.RunId && r.GenerationPackageId == packageId));
            if (filter.UserId is int userId)
                query = query.Where(g => g.GeneratedByUserId == userId);
            if (filter.Year is int year)
                query = query.Where(g => g.GeneratedAt.Year == year);
            return query;
        }

        private sealed record SearchCandidate(int Id, string LastName, string? FirstName, string? MiddleName, string FileName, long SizeBytes);

        private static async Task<List<SearchCandidate>> SearchCandidatesAsync(AppDbContext db, ArchiveFilter filter, string query)
        {
            var candidates = await ApplyFilter(db, filter)
                .OrderByDescending(g => g.GeneratedAt)
                .Select(g => new SearchCandidate(
                    g.Id,
                    db.Recipients.IgnoreQueryFilters()
                        .Where(r => r.Id == g.RecipientId).Select(r => r.LastName).FirstOrDefault() ?? "-",
                    db.Recipients.IgnoreQueryFilters()
                        .Where(r => r.Id == g.RecipientId).Select(r => r.FirstName).FirstOrDefault(),
                    db.Recipients.IgnoreQueryFilters()
                        .Where(r => r.Id == g.RecipientId).Select(r => r.MiddleName).FirstOrDefault(),
                    g.FileName,
                    g.SizeBytes))
                .ToListAsync();

            return candidates
                .Where(c => SearchNormalization.Contains(
                    string.Join(' ', new[] { c.LastName, c.FirstName, c.MiddleName, c.FileName }), query))
                .ToList();
        }

        public async Task<List<ArchiveRowDto>> QueryAsync(ArchiveFilter filter)
        {
            using var db = _dbFactory.CreateDbContext();

            IQueryable<GeneratedDocument> page;
            if (SearchNormalization.PrepareQuery(filter.Search) is { } query)
            {
                var ids = (await SearchCandidatesAsync(db, filter, query))
                    .Skip(filter.Skip).Take(filter.Take).Select(c => c.Id).ToList();
                page = db.GeneratedDocuments.IgnoreQueryFilters()
                    .Where(g => ids.Contains(g.Id))
                    .OrderByDescending(g => g.GeneratedAt);
            }
            else
            {
                page = ApplyFilter(db, filter)
                    .OrderByDescending(g => g.GeneratedAt)
                    .Skip(filter.Skip)
                    .Take(filter.Take);
            }

            var rows = await ProjectRows(db, page).ToListAsync();
            return await MarkStaleAsync(db, rows);
        }

        private static IQueryable<ArchiveRowDto> ProjectRows(AppDbContext db, IQueryable<GeneratedDocument> query)
            => query.Select(g => new ArchiveRowDto(
                g.Id,
                g.RecipientId,
                g.TemplateId,
                db.Recipients.IgnoreQueryFilters()
                    .Where(r => r.Id == g.RecipientId).Select(r => r.LastName).FirstOrDefault() ?? "-",
                db.Recipients.IgnoreQueryFilters()
                    .Where(r => r.Id == g.RecipientId).Select(r => r.FirstName).FirstOrDefault(),
                db.Recipients.IgnoreQueryFilters()
                    .Where(r => r.Id == g.RecipientId).Select(r => r.MiddleName).FirstOrDefault(),
                db.Templates.IgnoreQueryFilters()
                    .Where(t => t.Id == g.TemplateId).Select(t => t.Name).FirstOrDefault() ?? "-",
                db.Templates.IgnoreQueryFilters()
                    .Any(t => t.Id == g.TemplateId && t.DeletedAt == null),
                g.Version,
                g.IntakeId,
                g.IntakeId != null
                    ? db.Intakes.IgnoreQueryFilters()
                        .Where(i => i.Id == g.IntakeId).Select(i => (int?)i.Number).FirstOrDefault()
                    : null,
                g.OrgPathSnapshot,
                g.GeneratedAt,
                db.Users.IgnoreQueryFilters()
                    .Where(u => u.Id == g.GeneratedByUserId).Select(u => u.FullName).FirstOrDefault() ?? "-",
                db.DocumentAttachments.Count(a => a.DeletedAt == null
                    && a.GeneratedDocument!.DeletedAt == null
                    && a.GeneratedDocument.RecipientId == g.RecipientId
                    && a.GeneratedDocument.TemplateId == g.TemplateId),
                g.HasContent,
                g.SourceType,
                g.FileName,
                g.SizeBytes,
                db.Recipients.IgnoreQueryFilters()
                    .Any(r => r.Id == g.RecipientId && r.DeletedAt == null),
                false));

        private async Task<List<ArchiveRowDto>> MarkStaleAsync(AppDbContext db, List<ArchiveRowDto> rows)
        {
            if (rows.Count == 0) return rows;
            var detectStale = await db.AppSettings.Select(s => s.DetectStaleDocuments).FirstOrDefaultAsync() ?? true;
            if (!detectStale) return rows;

            var ids = rows.Select(r => r.Id).ToList();
            var hashes = await db.GeneratedDocuments.IgnoreQueryFilters()
                .Where(g => ids.Contains(g.Id) && g.SourceHash != null && g.SourceType == DocumentSourceType.Generated)
                .Select(g => new { g.Id, g.SourceHash })
                .ToDictionaryAsync(g => g.Id, g => g.SourceHash!);
            if (hashes.Count == 0) return rows;

            var candidates = rows.Where(r => hashes.ContainsKey(r.Id) && r.RecipientAlive).ToList();
            if (candidates.Count == 0) return rows;

            var recipientIds = candidates.Select(r => r.RecipientId).Distinct().ToList();
            var templateIds = candidates.Select(r => r.TemplateId).Distinct().ToList();
            var people = (await db.Recipients.Where(r => recipientIds.Contains(r.Id))
                    .WithHashSources().AsNoTracking().ToListAsync())
                .ToDictionary(r => r.Id);
            var mappings = (await db.TemplateFieldMappings
                    .Where(m => templateIds.Contains(m.TemplateId)).AsNoTracking().ToListAsync())
                .GroupBy(m => m.TemplateId).ToDictionary(g => g.Key, g => g.ToList());
            var orgSettings = await db.OrganizationSettings.AsNoTracking().FirstOrDefaultAsync();

            var stale = new HashSet<int>();
            foreach (var row in candidates)
            {
                if (!people.TryGetValue(row.RecipientId, out var person)) continue;
                var templateMappings = mappings.GetValueOrDefault(row.TemplateId) ?? new List<TemplateFieldMapping>();
                if (_documentHashService.ComputeSourceHash(templateMappings, person, orgSettings)
                    != DocumentHashService.AutoPart(hashes[row.Id]))
                    stale.Add(row.Id);
            }

            return rows.Select(r => stale.Contains(r.Id) ? r with { IsStale = true } : r).ToList();
        }

        private static async Task<string> DocumentLabelAsync(AppDbContext db, GeneratedDocument doc)
        {
            var person = await db.Recipients.IgnoreQueryFilters()
                .Where(r => r.Id == doc.RecipientId)
                .Select(r => new { r.LastName, r.FirstName, r.MiddleName })
                .FirstOrDefaultAsync();
            var template = await db.Templates.IgnoreQueryFilters()
                .Where(t => t.Id == doc.TemplateId).Select(t => t.Name).FirstOrDefaultAsync();
            var name = person is null ? "-" : NameFormatter.FullName(person.LastName, person.FirstName, person.MiddleName);
            return $"{name} · {template ?? "-"} · в.{doc.Version}";
        }

        private static string? ContentWarning(GeneratedDocument doc, byte[] content)
            => ContentWarning(doc.ContentHash, content);

        private static string? ContentWarning(string? expectedHash, byte[] content)
        {
            if (string.IsNullOrWhiteSpace(expectedHash)) return null;
            var actual = Convert.ToHexString(SHA256.HashData(content));
            return string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase)
                ? null
                : "Вміст файлу не збігається з контрольною сумою, збереженою під час генерації - "
                  + "файл в архіві міг бути пошкоджений або підмінений. Перевірте документ перед використанням.";
        }

        public async Task<ArchiveStats> GetStatsAsync(ArchiveFilter filter)
        {
            using var db = _dbFactory.CreateDbContext();
            if (SearchNormalization.PrepareQuery(filter.Search) is { } search)
            {
                var matches = await SearchCandidatesAsync(db, filter, search);
                return new ArchiveStats(matches.Count, matches.Sum(m => m.SizeBytes));
            }

            var query = ApplyFilter(db, filter);
            var count = await query.CountAsync();
            var totalBytes = await query.SumAsync(g => (long?)g.SizeBytes) ?? 0;
            return new ArchiveStats(count, totalBytes);
        }

        public async Task<ArchiveFilterOptions> GetFilterOptionsAsync()
        {
            using var db = _dbFactory.CreateDbContext();

            var documents = db.GeneratedDocuments.IgnoreQueryFilters().Where(g => g.DeletedAt == null);

            var intakeIds = await documents.Where(g => g.IntakeId != null)
                .Select(g => g.IntakeId!.Value).Distinct().ToListAsync();
            var intakes = (await db.Intakes.IgnoreQueryFilters().AsNoTracking()
                    .Where(i => i.DeletedAt == null || intakeIds.Contains(i.Id))
                    .OrderByDescending(i => i.Number)
                    .Select(i => new { i.Id, i.Number, i.DisplayNumber, i.Status, i.DeletedAt })
                    .ToListAsync())
                .Select(i => (i.Id, $"{IntakeLabel.Of(i.Number, i.DisplayNumber)} · "
                                    + (i.DeletedAt == null ? StatusLabel(i.Status) : "у кошику")))
                .ToList();

            var templateIds = await documents.Select(g => g.TemplateId).Distinct().ToListAsync();
            var templates = (await db.Templates.IgnoreQueryFilters()
                    .Where(t => templateIds.Contains(t.Id))
                    .OrderBy(t => t.Name).Select(t => new { t.Id, t.Name, t.DeletedAt }).ToListAsync())
                .Select(t => (t.Id, t.DeletedAt == null ? t.Name : $"{t.Name} (у кошику)")).ToList();

            var packages = (await db.GenerationPackages
                    .OrderBy(p => p.Name).Select(p => new { p.Id, p.Name }).ToListAsync())
                .Select(p => (p.Id, p.Name)).ToList();

            var authorIds = await db.GeneratedDocuments.Select(g => g.GeneratedByUserId).Distinct().ToListAsync();
            var authors = (await db.Users.Where(u => authorIds.Contains(u.Id))
                    .OrderBy(u => u.FullName).Select(u => new { u.Id, u.FullName }).ToListAsync())
                .Select(u => (u.Id, u.FullName)).ToList();

            var years = (await db.GeneratedDocuments.Select(g => g.GeneratedAt.Year).Distinct().ToListAsync())
                .Concat(await db.GeneratedGroupDocuments.Select(g => g.GeneratedAt.Year).Distinct().ToListAsync())
                .Concat(await db.GenerationPackageRuns.Select(r => r.RunAt.Year).Distinct().ToListAsync())
                .Distinct().OrderByDescending(y => y).ToList();

            return new ArchiveFilterOptions(intakes, templates, packages, authors, years);
        }

        private static string StatusLabel(IntakeStatus status) => status switch
        {
            IntakeStatus.Active => "активний",
            IntakeStatus.Completed => "завершений",
            _ => "запланований"
        };

        public async Task<ArchiveOpResult> OpenAsync(int documentId)
        {
            using var db = _dbFactory.CreateDbContext();
            var doc = await db.GeneratedDocuments.FirstOrDefaultAsync(g => g.Id == documentId);
            if (doc is null) return new ArchiveOpResult(false, RecordGoneMessage);
            var content = await db.GeneratedDocumentContents
                .FirstOrDefaultAsync(c => c.GeneratedDocumentId == documentId);
            if (content is null) return new ArchiveOpResult(false, NoContentMessage);

            await _tempFileService.OpenAsync(doc.FileName, content.Content);

            _auditLogService.Log(db, "Відкрито документ", "GeneratedDocument", documentId, null, doc.FileName,
                await DocumentLabelAsync(db, doc));
            await db.SaveChangesAsync();
            return new ArchiveOpResult(true, null, ContentWarning(doc, content.Content));
        }

        public async Task<ArchiveOpResult> SaveAsAsync(int documentId, string targetPath)
        {
            using var db = _dbFactory.CreateDbContext();
            var doc = await db.GeneratedDocuments.FirstOrDefaultAsync(g => g.Id == documentId);
            if (doc is null) return new ArchiveOpResult(false, RecordGoneMessage);
            var content = await db.GeneratedDocumentContents
                .FirstOrDefaultAsync(c => c.GeneratedDocumentId == documentId);
            if (content is null) return new ArchiveOpResult(false, NoContentMessage);

            var bytes = _watermarkService.Apply(content.Content, doc.FileName);
            await File.WriteAllBytesAsync(targetPath, bytes);

            _auditLogService.LogExport(db, "GeneratedDocument", 1,
                $"{await DocumentLabelAsync(db, doc)} → {Path.GetFileName(targetPath)}");
            await db.SaveChangesAsync();
            return new ArchiveOpResult(true, null, ContentWarning(doc, content.Content));
        }

        public async Task<(int Saved, List<string> Errors)> SaveManyAsync(IReadOnlyList<int> documentIds, string targetFolder)
        {
            using var db = _dbFactory.CreateDbContext();
            var template = await GetExportNameTemplateAsync(db);
            var errors = new List<string>();
            var saved = 0;
            var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var id in documentIds)
            {
                var doc = await db.GeneratedDocuments
                    .IgnoreQueryFilters()
                    .Include(g => g.Recipient)
                    .Include(g => g.Template)
                    .FirstAsync(g => g.Id == id);
                var content = await db.GeneratedDocumentContents
                    .FirstOrDefaultAsync(c => c.GeneratedDocumentId == id);
                if (content is null)
                {
                    errors.Add($"{doc.FileName}: {NoContentMessage}");
                    continue;
                }

                var subfolder = targetFolder;
                foreach (var segment in (doc.OrgPathSnapshot ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    subfolder = Path.Combine(subfolder, SecureTempFileService.SanitizeFileName(segment));
                Directory.CreateDirectory(subfolder);

                var baseName = BuildExportFileName(template, doc);
                var extension = Path.GetExtension(doc.FileName);
                var path = Path.Combine(subfolder, baseName + extension);
                var suffix = 2;
                while (usedPaths.Contains(path) || File.Exists(path))
                {
                    path = Path.Combine(subfolder, $"{baseName}_{suffix++}{extension}");
                }
                usedPaths.Add(path);

                try
                {
                    var bytes = _watermarkService.Apply(content.Content, doc.FileName);
                    await File.WriteAllBytesAsync(path, bytes);
                    saved++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{doc.FileName}: {ex.Message}");
                }
            }

            if (saved > 0)
            {
                _auditLogService.LogExport(db, "GeneratedDocument", saved, $"{saved} документів → {targetFolder}");
                await db.SaveChangesAsync();
            }

            return (saved, errors);
        }

        private static async Task<string> GetExportNameTemplateAsync(AppDbContext db)
        {
            var template = await db.AppSettings.Select(s => s.ExportFileNameTemplate).FirstOrDefaultAsync();
            return string.IsNullOrWhiteSpace(template) ? "{ПІБ} - {Шаблон}" : template;
        }

        private static string BuildExportFileName(string template, GeneratedDocument doc)
        {
            var person = string.Join(' ', new[]
            {
                doc.Recipient?.LastName, doc.Recipient?.FirstName, doc.Recipient?.MiddleName
            }.Where(p => !string.IsNullOrWhiteSpace(p)));

            var name = template
                .Replace("{ПІБ}", person)
                .Replace("{Шаблон}", doc.Template?.Name ?? "документ")
                .Replace("{Дата}", doc.GeneratedAt.ToString("dd.MM.yyyy"));

            return SecureTempFileService.SanitizeFileName(name);
        }

        public async Task<List<string>> GetManualTagsAsync(IReadOnlyList<int> templateIds)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.TemplateFieldMappings
                .Where(m => templateIds.Contains(m.TemplateId) && m.SourceType == MappingSourceType.Manual)
                .Select(m => m.PlaceholderTag)
                .Distinct()
                .OrderBy(t => t)
                .ToListAsync();
        }

        public async Task<ArchiveOpResult> RegenerateAsync(
            int documentId, Dictionary<string, string> manualValues, int? courseOfficerId = null)
        {
            using var db = _dbFactory.CreateDbContext();
            var doc = await db.GeneratedDocuments.FirstOrDefaultAsync(g => g.Id == documentId);
            if (doc is null) return new ArchiveOpResult(false, RecordGoneMessage);

            var recipientState = await db.Recipients.IgnoreQueryFilters()
                .Where(r => r.Id == doc.RecipientId)
                .Select(r => new { r.DeletedAt })
                .FirstOrDefaultAsync();
            if (recipientState is null || recipientState.DeletedAt is not null)
                return new ArchiveOpResult(false, DeletedRecipientMessage);

            doc.Recipient = await db.Recipients
                .Include(r => r.Unit)
                .Include(r => r.Room)
                .Include(r => r.OrgNode)
                .Include(r => r.Weapons)
                .FirstAsync(r => r.Id == doc.RecipientId);

            var template = await db.Templates.FirstOrDefaultAsync(t => t.Id == doc.TemplateId);
            if (template is null)
                return new ArchiveOpResult(false, "Шаблон видалено - перегенерація неможлива");

            if (template.Kind == TemplateKind.Group)
                return new ArchiveOpResult(false,
                    $"Шаблон «{template.Name}» - груповий: він формує один документ для всього складу, "
                    + "а не для однієї людини. Перегенерувати з нього документ для окремого одержувача не можна.");

            var mappings = await db.TemplateFieldMappings.Where(m => m.TemplateId == template.Id).ToListAsync();
            var orgSettings = await db.OrganizationSettings.FirstOrDefaultAsync();
            var courseOfficerSignature = GenerationService.CourseOfficerSignatureFor(db, mappings, courseOfficerId);
            var values = GenerationService.BuildValues(mappings, doc.Recipient, orgSettings, manualValues, courseOfficerSignature);

            var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.docx");
            try
            {
                var result = _documentGenerationService.GenerateOne(template, template.Content, values, tempPath);
                if (!result.Success)
                    return new ArchiveOpResult(false, result.ErrorMessage);

                var bytes = await File.ReadAllBytesAsync(tempPath);

                var fileName = string.IsNullOrWhiteSpace(doc.FileName)
                    ? $"{template.Name}.docx"
                    : Path.ChangeExtension(doc.FileName, ".docx");
                var root = await OutputFolderService.ConfiguredRootAsync(db);
                if (root is not null)
                {
                    fileName = await OutputFolderService.PlaceRegeneratedAsync(db, root, doc, doc.Recipient, template.Name);
                    try
                    {
                        await OutputFolderService.WriteAsync(root, fileName, bytes);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        return new ArchiveOpResult(false, $"Не вдалося записати файл у теку документів: {ex.Message}");
                    }
                }

                await AddVersionAsync(db, doc, bytes, fileName,
                    DocumentSourceType.Generated,
                    _documentHashService.ComputeSourceHash(
                        mappings, doc.Recipient, orgSettings, manualValues, courseOfficerSignature));

                var created = await db.GeneratedDocuments
                    .Where(g => g.RecipientId == doc.RecipientId && g.TemplateId == doc.TemplateId && g.IsCurrent)
                    .OrderByDescending(g => g.Version).FirstAsync();
                _auditLogService.Log(db, "Перегенеровано", "GeneratedDocument", created.Id, $"в.{doc.Version}",
                    $"в.{created.Version}", await DocumentLabelAsync(db, created));
                await db.SaveChangesAsync();
                return new ArchiveOpResult(true, null);
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }

        private async Task AddVersionAsync(
            AppDbContext db, GeneratedDocument previous, byte[] bytes, string fileName,
            DocumentSourceType sourceType, string? sourceHash = null)
        {
            var maxVersion = await db.GeneratedDocuments.IgnoreQueryFilters()
                .Where(g => g.RecipientId == previous.RecipientId && g.TemplateId == previous.TemplateId)
                .MaxAsync(g => g.Version);

            foreach (var current in db.GeneratedDocuments
                .Where(g => g.RecipientId == previous.RecipientId && g.TemplateId == previous.TemplateId && g.IsCurrent)
                .ToList())
            {
                current.IsCurrent = false;
            }

            var recipient = await db.Recipients.IgnoreQueryFilters()
                .Where(r => r.Id == previous.RecipientId)
                .Select(r => new { r.IntakeId, r.OrgNodeId, r.DeletedAt })
                .FirstOrDefaultAsync();
            var alive = recipient is not null && recipient.DeletedAt is null;
            var orgPath = alive
                ? await OrgTree.OrgPathBuilder.BuildAsync(db, recipient!.OrgNodeId)
                : previous.OrgPathSnapshot;

            db.GeneratedDocuments.Add(new GeneratedDocument
            {
                RecipientId = previous.RecipientId,
                TemplateId = previous.TemplateId,
                GeneratedAt = DateTime.Now,
                GeneratedByUserId = _currentUserContext.CurrentUserId ?? 0,
                FileName = fileName,
                SizeBytes = bytes.LongLength,
                ContentHash = Convert.ToHexString(SHA256.HashData(bytes)),
                SourceHash = sourceHash,
                Version = maxVersion + 1,
                IsCurrent = true,
                SourceType = sourceType,
                IntakeId = alive ? recipient!.IntakeId : previous.IntakeId,
                OrgNodeIdSnapshot = alive ? recipient!.OrgNodeId : previous.OrgNodeIdSnapshot,
                OrgPathSnapshot = orgPath,
                HasContent = true,
                Content = new GeneratedDocumentContent { Content = bytes }
            });

            await db.SaveChangesAsync();
        }

        public async Task<ArchiveOpResult> UploadManualAsync(int documentId, string filePath, string? note)
        {
            using var db = _dbFactory.CreateDbContext();
            var doc = await db.GeneratedDocuments.FirstOrDefaultAsync(g => g.Id == documentId);
            if (doc is null) return new ArchiveOpResult(false, RecordGoneMessage);

            if (await FileLimitErrorAsync(db, filePath) is { } limitError)
                return new ArchiveOpResult(false, limitError);

            var bytes = await File.ReadAllBytesAsync(filePath);
            await AddVersionAsync(db, doc, bytes, Path.GetFileName(filePath), DocumentSourceType.ManualUpload);

            var created = await db.GeneratedDocuments
                .Where(g => g.RecipientId == doc.RecipientId && g.TemplateId == doc.TemplateId && g.IsCurrent)
                .OrderByDescending(g => g.Version).FirstAsync();
            _auditLogService.Log(db, "Завантажено версію", "GeneratedDocument", created.Id, null,
                Path.GetFileName(filePath), JoinDetails(await DocumentLabelAsync(db, created), note));
            await db.SaveChangesAsync();
            return new ArchiveOpResult(true, null);
        }

        private static string JoinDetails(string label, string? note)
            => string.IsNullOrWhiteSpace(note) ? label : $"{label} · {note.Trim()}";

        private static async Task<string?> FileLimitErrorAsync(AppDbContext db, string filePath)
        {
            if (!File.Exists(filePath))
                return $"Файл не знайдено: {filePath}. Нічого не збережено.";

            var maxKb = await db.AppSettings.Select(s => s.MaxDocumentSizeKb).FirstOrDefaultAsync()
                ?? DefaultMaxDocumentSizeKb;
            var info = new FileInfo(filePath);
            if (info.Length > maxKb * 1024L)
                return $"Файл {info.Length / 1024 / 1024.0:0.#} МБ перевищує ліміт {maxKb / 1024.0:0.#} МБ. Нічого не збережено.";

            return null;
        }

        public async Task<ArchiveOpResult> AttachAsync(int documentId, string filePath, string? note)
        {
            using var db = _dbFactory.CreateDbContext();
            var doc = await db.GeneratedDocuments.FirstOrDefaultAsync(g => g.Id == documentId);
            if (doc is null) return new ArchiveOpResult(false, RecordGoneMessage);

            if (await FileLimitErrorAsync(db, filePath) is { } limitError)
                return new ArchiveOpResult(false, limitError);

            var bytes = await File.ReadAllBytesAsync(filePath);

            db.DocumentAttachments.Add(new DocumentAttachment
            {
                GeneratedDocumentId = doc.Id,
                FileName = Path.GetFileName(filePath),
                Content = bytes,
                SizeBytes = bytes.LongLength,
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                UploadedBy = _currentUserContext.CurrentUserFullName ?? string.Empty,
                UploadedAt = DateTime.Now
            });

            _auditLogService.Log(db, "Додано вкладення", "GeneratedDocument", documentId, null,
                Path.GetFileName(filePath), JoinDetails(await DocumentLabelAsync(db, doc), note));
            await db.SaveChangesAsync();
            return new ArchiveOpResult(true, null);
        }

        public async Task<ArchiveRowDto?> GetCurrentRowAsync(int recipientId, int templateId)
        {
            using var db = _dbFactory.CreateDbContext();

            var rows = await ProjectRows(db, db.GeneratedDocuments
                    .IgnoreQueryFilters()
                    .Where(g => g.DeletedAt == null
                        && g.RecipientId == recipientId && g.TemplateId == templateId && g.IsCurrent))
                .Take(1)
                .ToListAsync();
            return (await MarkStaleAsync(db, rows)).FirstOrDefault();
        }

        public async Task<ArchiveOpResult> OpenAttachmentAsync(int attachmentId)
        {
            using var db = _dbFactory.CreateDbContext();
            var attachment = await db.DocumentAttachments.FirstOrDefaultAsync(a => a.Id == attachmentId);
            if (attachment is null) return new ArchiveOpResult(false, RecordGoneMessage);

            await _tempFileService.OpenAsync(attachment.FileName, attachment.Content);

            var owner = await db.GeneratedDocuments.IgnoreQueryFilters()
                .FirstOrDefaultAsync(g => g.Id == attachment.GeneratedDocumentId);
            _auditLogService.Log(db, "Відкрито вкладення", "GeneratedDocument",
                attachment.GeneratedDocumentId, null, attachment.FileName,
                owner is null ? null : await DocumentLabelAsync(db, owner));
            await db.SaveChangesAsync();
            return new ArchiveOpResult(true, null);
        }

        public async Task<ArchiveOpResult> SaveAttachmentAsAsync(int attachmentId, string targetPath)
        {
            using var db = _dbFactory.CreateDbContext();
            var attachment = await db.DocumentAttachments.FirstOrDefaultAsync(a => a.Id == attachmentId);
            if (attachment is null) return new ArchiveOpResult(false, RecordGoneMessage);

            var bytes = _watermarkService.Apply(attachment.Content, attachment.FileName);
            await File.WriteAllBytesAsync(targetPath, bytes);

            _auditLogService.LogExport(db, "GeneratedDocument", 1,
                $"вкладення → {Path.GetFileName(targetPath)}");
            await db.SaveChangesAsync();
            return new ArchiveOpResult(true, null);
        }

        public async Task<List<DocumentVersionDto>> GetVersionsAsync(int recipientId, int templateId)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.GeneratedDocuments
                .Where(g => g.RecipientId == recipientId && g.TemplateId == templateId)
                .OrderByDescending(g => g.Version)
                .Select(g => new DocumentVersionDto(
                    g.Id, g.Version, g.GeneratedAt,
                    db.Users.IgnoreQueryFilters()
                        .Where(u => u.Id == g.GeneratedByUserId).Select(u => u.FullName).FirstOrDefault() ?? "-",
                    g.SizeBytes, g.SourceType, g.IsCurrent, g.HasContent, g.FileName))
                .ToListAsync();
        }

        public async Task<List<AttachmentDto>> GetAttachmentsAsync(int recipientId, int templateId)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.DocumentAttachments
                .Where(a => a.GeneratedDocument!.RecipientId == recipientId
                    && a.GeneratedDocument.TemplateId == templateId)
                .OrderByDescending(a => a.UploadedAt)
                .Select(a => new AttachmentDto(
                    a.Id, a.FileName, a.UploadedAt, a.Note, a.SizeBytes, a.GeneratedDocument!.Version))
                .ToListAsync();
        }

        public async Task DeleteAttachmentAsync(int attachmentId)
        {
            using var db = _dbFactory.CreateDbContext();
            var attachment = await db.DocumentAttachments.FirstOrDefaultAsync(a => a.Id == attachmentId);
            if (attachment is null) return;

            attachment.DeletedAt = DateTime.Now;
            attachment.DeletedBy = _currentUserContext.CurrentUserFullName;

            var owner = await db.GeneratedDocuments.IgnoreQueryFilters()
                .FirstOrDefaultAsync(g => g.Id == attachment.GeneratedDocumentId);
            _auditLogService.Log(db, "Видалено вкладення", "GeneratedDocument",
                attachment.GeneratedDocumentId, attachment.FileName, null,
                owner is null ? null : await DocumentLabelAsync(db, owner));
            await db.SaveChangesAsync();
        }

        public async Task<List<DeletedAttachmentInfo>> GetDeletedAttachmentsAsync()
        {
            using var db = _dbFactory.CreateDbContext();
            var rows = await db.DocumentAttachments.IgnoreQueryFilters()
                .Where(a => a.DeletedAt != null)
                .OrderByDescending(a => a.DeletedAt)
                .Select(a => new
                {
                    a.Id, a.FileName, a.DeletedAt, a.DeletedBy,
                    Document = db.GeneratedDocuments.IgnoreQueryFilters()
                        .Where(g => g.Id == a.GeneratedDocumentId)
                        .Select(g => new { g.RecipientId, g.TemplateId, g.Version })
                        .FirstOrDefault()
                })
                .ToListAsync();

            var recipientIds = rows.Where(r => r.Document != null).Select(r => r.Document!.RecipientId).Distinct().ToList();
            var templateIds = rows.Where(r => r.Document != null).Select(r => r.Document!.TemplateId).Distinct().ToList();
            var people = await db.Recipients.IgnoreQueryFilters()
                .Where(r => recipientIds.Contains(r.Id))
                .Select(r => new { r.Id, r.LastName, r.FirstName, r.MiddleName })
                .ToDictionaryAsync(r => r.Id);
            var templates = await db.Templates.IgnoreQueryFilters()
                .Where(t => templateIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name);

            return rows.Select(r =>
            {
                var person = r.Document is not null && people.TryGetValue(r.Document.RecipientId, out var p)
                    ? NameFormatter.FullName(p.LastName, p.FirstName, p.MiddleName)
                    : "-";
                var template = r.Document is not null ? templates.GetValueOrDefault(r.Document.TemplateId) ?? "-" : "-";
                return new DeletedAttachmentInfo(
                    r.Id, r.FileName, person, template, r.Document?.Version ?? 0, r.DeletedAt!.Value, r.DeletedBy);
            }).ToList();
        }

        public async Task RestoreAttachmentAsync(int attachmentId)
        {
            using var db = _dbFactory.CreateDbContext();
            var attachment = await db.DocumentAttachments.IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.Id == attachmentId);
            if (attachment is null) return;

            attachment.DeletedAt = null;
            attachment.DeletedBy = null;

            var owner = await db.GeneratedDocuments.IgnoreQueryFilters()
                .FirstOrDefaultAsync(g => g.Id == attachment.GeneratedDocumentId);
            _auditLogService.Log(db, "Відновлено вкладення", "GeneratedDocument",
                attachment.GeneratedDocumentId, null, attachment.FileName,
                owner is null ? null : await DocumentLabelAsync(db, owner));
            await db.SaveChangesAsync();
        }

        public async Task<int> MakeCurrentAsync(int versionDocumentId)
        {
            using var db = _dbFactory.CreateDbContext();
            var target = await db.GeneratedDocuments.FirstOrDefaultAsync(g => g.Id == versionDocumentId);
            if (target is null) return 0;

            foreach (var current in db.GeneratedDocuments
                .Where(g => g.RecipientId == target.RecipientId && g.TemplateId == target.TemplateId && g.IsCurrent)
                .ToList())
            {
                current.IsCurrent = false;
            }
            target.IsCurrent = true;

            _auditLogService.Log(db, "Змінено актуальну версію", "GeneratedDocument", target.Id,
                null, $"в.{target.Version}", await DocumentLabelAsync(db, target));
            await db.SaveChangesAsync();
            return target.Id;
        }

        public async Task<int> CountVersionsAsync(IReadOnlyList<int> documentIds)
        {
            using var db = _dbFactory.CreateDbContext();
            var pairs = await db.GeneratedDocuments
                .Where(g => documentIds.Contains(g.Id))
                .Select(g => new { g.RecipientId, g.TemplateId })
                .Distinct()
                .ToListAsync();

            var total = 0;
            foreach (var pair in pairs)
            {
                total += await db.GeneratedDocuments
                    .CountAsync(g => g.RecipientId == pair.RecipientId && g.TemplateId == pair.TemplateId);
            }
            return total;
        }

        public async Task DeleteAsync(IReadOnlyList<int> documentIds, bool allVersions = false)
        {
            using var db = _dbFactory.CreateDbContext();
            var docs = await db.GeneratedDocuments.Where(g => documentIds.Contains(g.Id)).ToListAsync();
            if (allVersions)
            {
                var pairs = docs.Select(d => (d.RecipientId, d.TemplateId)).Distinct().ToList();
                var known = docs.Select(d => d.Id).ToHashSet();
                foreach (var (recipientId, templateId) in pairs)
                {
                    var siblings = await db.GeneratedDocuments
                        .Where(g => g.RecipientId == recipientId && g.TemplateId == templateId && !known.Contains(g.Id))
                        .ToListAsync();
                    docs.AddRange(siblings);
                    foreach (var sibling in siblings) known.Add(sibling.Id);
                }
            }

            var now = DateTime.Now;
            var user = _currentUserContext.CurrentUserFullName;

            foreach (var doc in docs)
            {
                doc.DeletedAt = now;
                doc.DeletedBy = user;

                if (doc.IsCurrent)
                {
                    doc.IsCurrent = false;
                    var previous = await db.GeneratedDocuments
                        .Where(g => g.RecipientId == doc.RecipientId && g.TemplateId == doc.TemplateId
                            && g.Id != doc.Id && g.DeletedAt == null)
                        .OrderByDescending(g => g.Version)
                        .FirstOrDefaultAsync();
                    if (previous is not null) previous.IsCurrent = true;
                }

                _auditLogService.Log(db, "Видалено документ", "GeneratedDocument", doc.Id,
                    $"{doc.FileName} (в.{doc.Version})", null, await DocumentLabelAsync(db, doc));
            }

            await db.SaveChangesAsync();
        }

        public async Task<List<DeletedDocumentInfo>> GetDeletedDocumentsAsync()
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.GeneratedDocuments.IgnoreQueryFilters()
                .Where(g => g.DeletedAt != null)
                .OrderByDescending(g => g.DeletedAt)
                .Select(g => new DeletedDocumentInfo(
                    g.Id,
                    db.Recipients.IgnoreQueryFilters()
                        .Where(r => r.Id == g.RecipientId).Select(r => r.LastName + " " + r.FirstName).FirstOrDefault() ?? "-",
                    db.Templates.IgnoreQueryFilters()
                        .Where(t => t.Id == g.TemplateId).Select(t => t.Name).FirstOrDefault() ?? "-",
                    g.Version,
                    g.DeletedAt!.Value,
                    g.DeletedBy))
                .ToListAsync();
        }

        public async Task RestoreAsync(int documentId)
        {
            using var db = _dbFactory.CreateDbContext();
            var doc = await db.GeneratedDocuments.IgnoreQueryFilters().FirstAsync(g => g.Id == documentId);
            doc.DeletedAt = null;
            doc.DeletedBy = null;

            var maxAliveVersion = await db.GeneratedDocuments
                .Where(g => g.RecipientId == doc.RecipientId && g.TemplateId == doc.TemplateId && g.Id != doc.Id)
                .MaxAsync(g => (int?)g.Version) ?? 0;

            if (doc.Version >= maxAliveVersion)
            {
                foreach (var current in db.GeneratedDocuments
                    .Where(g => g.RecipientId == doc.RecipientId && g.TemplateId == doc.TemplateId && g.IsCurrent)
                    .ToList())
                {
                    current.IsCurrent = false;
                }
                doc.IsCurrent = true;
            }

            _auditLogService.Log(db, "Відновлено документ", "GeneratedDocument", doc.Id, null,
                $"{doc.FileName} (в.{doc.Version})", await DocumentLabelAsync(db, doc));
            await db.SaveChangesAsync();
        }

        public async Task<List<RunDto>> GetRunsAsync(int? intakeId, int? year, int? userId = null)
        {
            using var db = _dbFactory.CreateDbContext();
            var query = db.GenerationPackageRuns.AsNoTracking();

            if (intakeId is int i) query = query.Where(r => r.IntakeId == i || r.IntakeId == null);
            if (year is int y) query = query.Where(r => r.RunAt.Year == y);
            if (userId is int u) query = query.Where(r => r.RunByUserId == u);

            var runs = await query
                .OrderByDescending(r => r.RunAt)
                .Select(r => new
                {
                    r.Id, r.RunAt,
                    PackageName = r.GenerationPackageId != null
                        ? db.GenerationPackages.IgnoreQueryFilters()
                            .Where(p => p.Id == r.GenerationPackageId).Select(p => p.Name).FirstOrDefault()
                        : null,
                    PackageInTrash = r.GenerationPackageId != null
                        && db.GenerationPackages.IgnoreQueryFilters()
                            .Any(p => p.Id == r.GenerationPackageId && p.DeletedAt != null),
                    r.IntakeId, r.BranchName, r.GeneratedCount, r.SkippedCount, r.ErrorCount
                })
                .ToListAsync();

            var intakes = await db.Intakes.IgnoreQueryFilters()
                .Select(x => new { x.Id, x.Number, x.DisplayNumber }).ToListAsync();
            var intakeById = intakes.ToDictionary(x => x.Id);

            return runs.Select(r =>
            {
                var intake = r.IntakeId is int iid ? intakeById.GetValueOrDefault(iid) : null;
                return new RunDto(
                    r.Id, r.RunAt, r.PackageName ?? "Вибірково",
                    intake?.Number,
                    r.BranchName, r.GeneratedCount, r.SkippedCount, r.ErrorCount, r.IntakeId,
                    intake is null ? null : IntakeLabel.Of(intake.Number, intake.DisplayNumber),
                    r.PackageInTrash);
            }).ToList();
        }

        public async Task<List<RunItemDto>> GetRunItemsAsync(int runId)
        {
            using var db = _dbFactory.CreateDbContext();

            var docs = await db.GeneratedDocuments.IgnoreQueryFilters()
                .Where(g => g.RunId == runId)
                .Select(g => new
                {
                    g.Id, g.RecipientId, g.TemplateId, g.SizeBytes, g.HasContent, g.FileName, g.DeletedAt, g.IsCurrent,
                    Person = db.Recipients.IgnoreQueryFilters()
                        .Where(r => r.Id == g.RecipientId).Select(r => r.LastName + " " + r.FirstName).FirstOrDefault() ?? "-",
                    TemplateName = db.Templates.IgnoreQueryFilters()
                        .Where(t => t.Id == g.TemplateId).Select(t => t.Name).FirstOrDefault() ?? "-"
                })
                .ToListAsync();

            var superseded = docs.Where(d => d.DeletedAt is null && !d.IsCurrent).ToList();
            var recipientIds = superseded.Select(d => d.RecipientId).Distinct().ToList();
            var templateIds = superseded.Select(d => d.TemplateId).Distinct().ToList();
            var currents = superseded.Count == 0
                ? new List<(int RecipientId, int TemplateId, int Id, int Version, bool HasContent, string FileName, long SizeBytes)>()
                : (await db.GeneratedDocuments
                        .Where(c => c.IsCurrent && recipientIds.Contains(c.RecipientId) && templateIds.Contains(c.TemplateId))
                        .Select(c => new { c.RecipientId, c.TemplateId, c.Id, c.Version, c.HasContent, c.FileName, c.SizeBytes })
                        .ToListAsync())
                    .Select(c => (c.RecipientId, c.TemplateId, c.Id, c.Version, c.HasContent, c.FileName, c.SizeBytes))
                    .ToList();

            var items = docs
                .OrderBy(d => d.Person, UkrainianCollation.Surname)
                .Select(d =>
                {
                    if (d.DeletedAt is not null)
                        return new RunItemDto(d.Person, d.TemplateName, "видалено", false, 0, null, false, string.Empty);
                    if (!d.IsCurrent)
                    {
                        var current = currents.FirstOrDefault(c =>
                            c.RecipientId == d.RecipientId && c.TemplateId == d.TemplateId && c.Id != d.Id);
                        if (current.Id != 0)
                            return new RunItemDto(d.Person, d.TemplateName, $"є новіша в.{current.Version}", false,
                                current.SizeBytes, current.Id, current.HasContent, current.FileName);
                    }
                    return new RunItemDto(d.Person, d.TemplateName, "згенеровано", false, d.SizeBytes, d.Id, d.HasContent, d.FileName);
                })
                .ToList();

            var groupDocs = await db.GeneratedGroupDocuments.IgnoreQueryFilters()
                .Where(g => g.RunId == runId)
                .Select(g => new
                {
                    g.Id, g.SizeBytes, g.HasContent, g.FileName, g.DeletedAt, g.IsCurrent, g.RecipientCount,
                    TemplateName = g.ExportTemplateId != null
                        ? db.ExportTemplates.IgnoreQueryFilters()
                            .Where(t => t.Id == g.ExportTemplateId).Select(t => t.Name).FirstOrDefault()
                        : db.Templates.IgnoreQueryFilters()
                            .Where(t => t.Id == g.TemplateId).Select(t => t.Name).FirstOrDefault()
                })
                .ToListAsync();

            foreach (var g in groupDocs)
            {
                var person = $"груповий · {g.RecipientCount} {PluralHelper.Pluralize(g.RecipientCount, "особа", "особи", "осіб")}";
                items.Add(g.DeletedAt is not null
                    ? new RunItemDto(person, g.TemplateName ?? "-", "видалено", false, 0, null, false, string.Empty, IsGroup: true)
                    : new RunItemDto(person, g.TemplateName ?? "-", g.IsCurrent ? "згенеровано" : "є новіша версія",
                        false, g.SizeBytes, g.Id, g.HasContent, g.FileName, IsGroup: true));
            }

            var summary = await db.GenerationPackageRuns
                .Where(r => r.Id == runId).Select(r => r.Summary).FirstOrDefaultAsync();

            if (RunIssue.TryDeserialize(summary, out var issues))
            {
                foreach (var issue in issues)
                {
                    items.Add(new RunItemDto(
                        issue.Person.Length > 0 ? issue.Person : "-",
                        issue.TemplateName,
                        issue.IsError ? $"помилка: {issue.Message}" : issue.Message,
                        issue.IsError, 0, null, false, string.Empty));
                }
            }
            else if (!string.IsNullOrWhiteSpace(summary))
            {
                foreach (var line in summary.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split(':', 2);
                    var head = parts[0].Split('/', 2);
                    items.Add(new RunItemDto(
                        head[0].Trim(),
                        head.Length > 1 ? head[1].Trim() : "-",
                        parts.Length > 1 ? $"помилка: {parts[1].Trim()}" : "помилка",
                        true, 0, null, false, string.Empty));
                }
            }

            return items;
        }

        private static IQueryable<GeneratedGroupDocument> SameGroupSeries(
            IQueryable<GeneratedGroupDocument> source, int? exportTemplateId, int? templateId, int? intakeId)
            => source.Where(g =>
                g.ExportTemplateId == exportTemplateId
                && g.TemplateId == templateId
                && g.IntakeId == intakeId);

        private static IQueryable<GeneratedGroupDocument> SameGroupSeries(
            IQueryable<GeneratedGroupDocument> source, GeneratedGroupDocument doc)
            => SameGroupSeries(source, doc.ExportTemplateId, doc.TemplateId, doc.IntakeId);

        public async Task<List<GroupDocumentRowDto>> QueryGroupAsync(GroupArchiveFilter filter)
        {
            using var db = _dbFactory.CreateDbContext();
            var query = db.GeneratedGroupDocuments.Where(g => g.IsCurrent);

            if (filter.ExportTemplateId is int exportTemplateId)
                query = query.Where(g => g.ExportTemplateId == exportTemplateId);
            if (filter.DocxTemplateId is int docxTemplateId)
                query = query.Where(g => g.TemplateId == docxTemplateId);
            if (filter.Year is int year)
                query = query.Where(g => g.GeneratedAt.Year == year);
            if (filter.UserId is int uid)
                query = query.Where(g => g.GeneratedByUserId == uid);
            if (filter.IntakeId is int intakeId)
                query = query.Where(g => g.IntakeId == intakeId);

            return await query
                .OrderByDescending(g => g.GeneratedAt)
                .Skip(filter.Skip)
                .Take(filter.Take)
                .Select(g => new GroupDocumentRowDto(
                    g.Id,
                    g.ExportTemplateId ?? 0,
                    g.TemplateId,
                    (g.ExportTemplateId != null
                        ? db.ExportTemplates.IgnoreQueryFilters()
                            .Where(t => t.Id == g.ExportTemplateId).Select(t => t.Name).FirstOrDefault()
                        : db.Templates.IgnoreQueryFilters()
                            .Where(t => t.Id == g.TemplateId).Select(t => t.Name).FirstOrDefault()) ?? "-",
                    g.ExportTemplateId != null
                        ? db.ExportTemplates.IgnoreQueryFilters().Any(t => t.Id == g.ExportTemplateId && t.DeletedAt == null)
                        : db.Templates.IgnoreQueryFilters().Any(t => t.Id == g.TemplateId && t.DeletedAt == null),
                    g.Version,
                    g.RecipientCount,
                    g.GeneratedAt,
                    db.Users.IgnoreQueryFilters()
                        .Where(u => u.Id == g.GeneratedByUserId).Select(u => u.FullName).FirstOrDefault() ?? "-",
                    g.HasContent,
                    g.FileName,
                    g.SizeBytes,
                    g.IntakeId))
                .ToListAsync();
        }

        private static async Task<string> GroupLabelAsync(AppDbContext db, GeneratedGroupDocument doc)
        {
            var name = doc.ExportTemplateId is int exportId
                ? await db.ExportTemplates.IgnoreQueryFilters()
                    .Where(t => t.Id == exportId).Select(t => t.Name).FirstOrDefaultAsync()
                : await db.Templates.IgnoreQueryFilters()
                    .Where(t => t.Id == doc.TemplateId).Select(t => t.Name).FirstOrDefaultAsync();
            return $"{name ?? "-"} · {doc.RecipientCount} {PluralHelper.Pluralize(doc.RecipientCount, "особа", "особи", "осіб")} · в.{doc.Version}";
        }

        public async Task<List<GroupTemplateOption>> GetGroupTemplateOptionsAsync()
        {
            using var db = _dbFactory.CreateDbContext();

            var exportIds = await db.GeneratedGroupDocuments
                .Where(g => g.ExportTemplateId != null)
                .Select(g => g.ExportTemplateId!.Value).Distinct().ToListAsync();

            var options = (await db.ExportTemplates.IgnoreQueryFilters()
                    .Where(t => exportIds.Contains(t.Id))
                    .Select(t => new { t.Id, t.Name })
                    .ToListAsync())
                .Select(t => new GroupTemplateOption(t.Id, null, t.Name))
                .ToList();

            var docxIds = await db.GeneratedGroupDocuments
                .Where(g => g.TemplateId != null)
                .Select(g => g.TemplateId!.Value).Distinct().ToListAsync();

            options.AddRange((await db.Templates.IgnoreQueryFilters()
                    .Where(t => docxIds.Contains(t.Id))
                    .Select(t => new { t.Id, t.Name })
                    .ToListAsync())
                .Select(t => new GroupTemplateOption(null, t.Id, t.Name)));

            return options.OrderBy(o => o.Name, StringComparer.CurrentCulture).ToList();
        }

        public async Task<ArchiveOpResult> OpenGroupAsync(int groupDocumentId)
        {
            using var db = _dbFactory.CreateDbContext();
            var doc = await db.GeneratedGroupDocuments.FirstOrDefaultAsync(g => g.Id == groupDocumentId);
            if (doc is null) return new ArchiveOpResult(false, RecordGoneMessage);
            var content = await db.GeneratedGroupDocumentContents
                .FirstOrDefaultAsync(c => c.GeneratedGroupDocumentId == groupDocumentId);
            if (content is null) return new ArchiveOpResult(false, NoContentMessage);

            await _tempFileService.OpenAsync(doc.FileName, content.Content);

            _auditLogService.Log(db, "Відкрито документ", "GeneratedGroupDocument", groupDocumentId, null, doc.FileName,
                await GroupLabelAsync(db, doc));
            await db.SaveChangesAsync();
            return new ArchiveOpResult(true, null, ContentWarning(doc.ContentHash, content.Content));
        }

        private const string PrintNotStartedMessage =
            "Друк не запущено: система не знайшла програму, яка друкує цей тип файлу.";

        public async Task<ArchiveOpResult> PrintAsync(int documentId)
        {
            using var db = _dbFactory.CreateDbContext();
            var doc = await db.GeneratedDocuments.FirstOrDefaultAsync(g => g.Id == documentId);
            if (doc is null) return new ArchiveOpResult(false, RecordGoneMessage);
            var content = await db.GeneratedDocumentContents
                .FirstOrDefaultAsync(c => c.GeneratedDocumentId == documentId);
            if (content is null) return new ArchiveOpResult(false, NoContentMessage);

            if (!await _tempFileService.PrintAsync(doc.FileName, content.Content))
                return new ArchiveOpResult(false, PrintNotStartedMessage);

            _auditLogService.Log(db, "Надруковано документ", "GeneratedDocument", documentId, null, doc.FileName,
                await DocumentLabelAsync(db, doc));
            await db.SaveChangesAsync();
            return new ArchiveOpResult(true, null, ContentWarning(doc, content.Content));
        }

        public async Task<List<GroupParticipantDto>> GetGroupParticipantsAsync(int groupDocumentId)
        {
            using var db = _dbFactory.CreateDbContext();
            var rows = await db.GeneratedGroupDocumentRecipients
                .IgnoreQueryFilters()
                .Where(p => p.GeneratedGroupDocumentId == groupDocumentId)
                .Select(p => new
                {
                    p.RecipientId,
                    Rank = p.Recipient != null ? p.Recipient.Rank : "-",
                    LastName = p.Recipient != null ? p.Recipient.LastName : "-",
                    FirstName = p.Recipient != null ? p.Recipient.FirstName : null,
                    MiddleName = p.Recipient != null ? p.Recipient.MiddleName : null,
                    UnitName = p.Recipient != null && p.Recipient.Unit != null ? p.Recipient.Unit.Name : "-"
                })
                .ToListAsync();

            return rows
                .Select(r => new GroupParticipantDto(
                    r.RecipientId, r.Rank,
                    NameFormatter.FullName(r.LastName, r.FirstName, r.MiddleName), r.UnitName))
                .OrderBy(r => r.FullName, UkrainianCollation.Surname)
                .ToList();
        }

        public async Task<ArchiveOpResult> PrintGroupAsync(int groupDocumentId)
        {
            using var db = _dbFactory.CreateDbContext();
            var doc = await db.GeneratedGroupDocuments.FirstOrDefaultAsync(g => g.Id == groupDocumentId);
            if (doc is null) return new ArchiveOpResult(false, RecordGoneMessage);
            var content = await db.GeneratedGroupDocumentContents
                .FirstOrDefaultAsync(c => c.GeneratedGroupDocumentId == groupDocumentId);
            if (content is null) return new ArchiveOpResult(false, NoContentMessage);

            if (!await _tempFileService.PrintAsync(doc.FileName, content.Content))
                return new ArchiveOpResult(false, PrintNotStartedMessage);

            _auditLogService.Log(db, "Надруковано документ", "GeneratedGroupDocument", groupDocumentId, null, doc.FileName,
                await GroupLabelAsync(db, doc));
            await db.SaveChangesAsync();
            return new ArchiveOpResult(true, null, ContentWarning(doc.ContentHash, content.Content));
        }

        public async Task<ArchiveOpResult> SaveGroupAsAsync(int groupDocumentId, string targetPath)
        {
            using var db = _dbFactory.CreateDbContext();
            var doc = await db.GeneratedGroupDocuments.FirstOrDefaultAsync(g => g.Id == groupDocumentId);
            if (doc is null) return new ArchiveOpResult(false, RecordGoneMessage);
            var content = await db.GeneratedGroupDocumentContents
                .FirstOrDefaultAsync(c => c.GeneratedGroupDocumentId == groupDocumentId);
            if (content is null) return new ArchiveOpResult(false, NoContentMessage);

            var bytes = _watermarkService.Apply(content.Content, doc.FileName);

            var targetFolder = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetFolder)) Directory.CreateDirectory(targetFolder);

            await File.WriteAllBytesAsync(targetPath, bytes);

            _auditLogService.LogExport(db, "GeneratedGroupDocument", 1,
                $"{await GroupLabelAsync(db, doc)} → {Path.GetFileName(targetPath)}");
            await db.SaveChangesAsync();
            return new ArchiveOpResult(true, null, ContentWarning(doc.ContentHash, content.Content));
        }

        public async Task DeleteGroupAsync(IReadOnlyList<int> groupDocumentIds)
        {
            using var db = _dbFactory.CreateDbContext();
            var docs = await db.GeneratedGroupDocuments.Where(g => groupDocumentIds.Contains(g.Id)).ToListAsync();
            var now = DateTime.Now;
            var user = _currentUserContext.CurrentUserFullName;

            foreach (var doc in docs)
            {
                doc.DeletedAt = now;
                doc.DeletedBy = user;

                if (doc.IsCurrent)
                {
                    doc.IsCurrent = false;
                    var previous = await SameGroupSeries(db.GeneratedGroupDocuments, doc)
                        .Where(g => g.Id != doc.Id && g.DeletedAt == null)
                        .OrderByDescending(g => g.Version)
                        .FirstOrDefaultAsync();
                    if (previous is not null) previous.IsCurrent = true;
                }

                _auditLogService.Log(db, "Видалено групову відомість", "GeneratedGroupDocument", doc.Id,
                    $"{doc.FileName} (в.{doc.Version})", null, await GroupLabelAsync(db, doc));
            }

            await db.SaveChangesAsync();
        }

        public async Task<List<GroupVersionDto>> GetGroupVersionsAsync(int? exportTemplateId, int? docxTemplateId, int? intakeId)
        {
            using var db = _dbFactory.CreateDbContext();
            return await SameGroupSeries(db.GeneratedGroupDocuments, exportTemplateId, docxTemplateId, intakeId)
                .OrderByDescending(g => g.Version)
                .Select(g => new GroupVersionDto(
                    g.Id, g.Version, g.GeneratedAt,
                    db.Users.IgnoreQueryFilters()
                        .Where(u => u.Id == g.GeneratedByUserId).Select(u => u.FullName).FirstOrDefault() ?? "-",
                    g.SizeBytes, g.IsCurrent, g.HasContent, g.FileName, g.RecipientCount))
                .ToListAsync();
        }

        public async Task<int> MakeGroupCurrentAsync(int versionDocumentId)
        {
            using var db = _dbFactory.CreateDbContext();
            var target = await db.GeneratedGroupDocuments.FirstOrDefaultAsync(g => g.Id == versionDocumentId);
            if (target is null) return 0;

            foreach (var current in SameGroupSeries(db.GeneratedGroupDocuments, target)
                .Where(g => g.IsCurrent).ToList())
            {
                current.IsCurrent = false;
            }
            target.IsCurrent = true;

            _auditLogService.Log(db, "Змінено актуальну версію", "GeneratedGroupDocument", target.Id,
                null, $"в.{target.Version}", await GroupLabelAsync(db, target));
            await db.SaveChangesAsync();
            return target.Id;
        }

        public async Task<List<DeletedGroupDocumentInfo>> GetDeletedGroupDocumentsAsync()
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.GeneratedGroupDocuments.IgnoreQueryFilters()
                .Where(g => g.DeletedAt != null)
                .OrderByDescending(g => g.DeletedAt)
                .Select(g => new DeletedGroupDocumentInfo(
                    g.Id,
                    (g.ExportTemplateId != null
                        ? db.ExportTemplates.IgnoreQueryFilters()
                            .Where(t => t.Id == g.ExportTemplateId).Select(t => t.Name).FirstOrDefault()
                        : db.Templates.IgnoreQueryFilters()
                            .Where(t => t.Id == g.TemplateId).Select(t => t.Name).FirstOrDefault()) ?? "-",
                    g.Version,
                    g.RecipientCount,
                    g.DeletedAt!.Value,
                    g.DeletedBy))
                .ToListAsync();
        }

        public async Task RestoreGroupAsync(int groupDocumentId)
        {
            using var db = _dbFactory.CreateDbContext();
            var doc = await db.GeneratedGroupDocuments.IgnoreQueryFilters().FirstAsync(g => g.Id == groupDocumentId);
            doc.DeletedAt = null;
            doc.DeletedBy = null;

            var maxAliveVersion = await SameGroupSeries(db.GeneratedGroupDocuments, doc)
                .Where(g => g.Id != doc.Id)
                .MaxAsync(g => (int?)g.Version) ?? 0;

            if (doc.Version >= maxAliveVersion)
            {
                foreach (var current in SameGroupSeries(db.GeneratedGroupDocuments, doc)
                    .Where(g => g.IsCurrent).ToList())
                {
                    current.IsCurrent = false;
                }
                doc.IsCurrent = true;
            }

            _auditLogService.Log(db, "Відновлено групову відомість", "GeneratedGroupDocument", doc.Id, null,
                $"{doc.FileName} (в.{doc.Version})", await GroupLabelAsync(db, doc));
            await db.SaveChangesAsync();
        }
    }
}
