using System.IO;
using System.Security.Cryptography;
using GenDoc.Data;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Generation;
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

        public async Task<List<ArchiveRowDto>> QueryAsync(ArchiveFilter filter)
        {
            using var db = _dbFactory.CreateDbContext();

            return await ApplyFilter(db, filter)
                .OrderByDescending(g => g.GeneratedAt)
                .Skip(filter.Skip)
                .Take(filter.Take)
                .Select(g => new ArchiveRowDto(
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
                        ? db.Intakes.Where(i => i.Id == g.IntakeId).Select(i => (int?)i.Number).FirstOrDefault()
                        : null,
                    g.OrgPathSnapshot,
                    g.GeneratedAt,
                    db.Users.IgnoreQueryFilters()
                        .Where(u => u.Id == g.GeneratedByUserId).Select(u => u.FullName).FirstOrDefault() ?? "-",
                    g.Attachments.Count(a => a.DeletedAt == null),
                    g.HasContent,
                    g.SourceType,
                    g.FileName,
                    g.SizeBytes))
                .ToListAsync();
        }

        public async Task<ArchiveStats> GetStatsAsync(ArchiveFilter filter)
        {
            using var db = _dbFactory.CreateDbContext();
            var query = ApplyFilter(db, filter);
            var count = await query.CountAsync();
            var totalBytes = await query.SumAsync(g => (long?)g.SizeBytes) ?? 0;
            return new ArchiveStats(count, totalBytes);
        }

        public async Task<ArchiveFilterOptions> GetFilterOptionsAsync()
        {
            using var db = _dbFactory.CreateDbContext();

            var intakes = (await db.Intakes.AsNoTracking()
                    .OrderByDescending(i => i.Number)
                    .Select(i => new { i.Id, i.Number, i.Status })
                    .ToListAsync())
                .Select(i => (i.Id, $"Набір №{i.Number} · {StatusLabel(i.Status)}"))
                .ToList();

            var templates = (await db.Templates
                    .OrderBy(t => t.Name).Select(t => new { t.Id, t.Name }).ToListAsync())
                .Select(t => (t.Id, t.Name)).ToList();

            var packages = (await db.GenerationPackages
                    .OrderBy(p => p.Name).Select(p => new { p.Id, p.Name }).ToListAsync())
                .Select(p => (p.Id, p.Name)).ToList();

            var authorIds = await db.GeneratedDocuments.Select(g => g.GeneratedByUserId).Distinct().ToListAsync();
            var authors = (await db.Users.Where(u => authorIds.Contains(u.Id))
                    .OrderBy(u => u.FullName).Select(u => new { u.Id, u.FullName }).ToListAsync())
                .Select(u => (u.Id, u.FullName)).ToList();

            var years = await db.GeneratedDocuments
                .Select(g => g.GeneratedAt.Year).Distinct().OrderByDescending(y => y).ToListAsync();

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

            _auditLogService.Log(db, "Відкрито документ", "GeneratedDocument", documentId, null, doc.FileName);
            await db.SaveChangesAsync();
            return new ArchiveOpResult(true, null);
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

            _auditLogService.LogExport(db, "GeneratedDocument", 1, $"1 документ → {Path.GetFileName(targetPath)}");
            await db.SaveChangesAsync();
            return new ArchiveOpResult(true, null);
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

        public async Task<ArchiveOpResult> RegenerateAsync(int documentId, Dictionary<string, string> manualValues)
        {
            using var db = _dbFactory.CreateDbContext();
            var doc = await db.GeneratedDocuments
                .Include(g => g.Recipient!).ThenInclude(r => r.Unit)
                .Include(g => g.Recipient!).ThenInclude(r => r.Room)
                .Include(g => g.Recipient!).ThenInclude(r => r.OrgNode)
                .Include(g => g.Recipient!).ThenInclude(r => r.Weapons)
                .FirstAsync(g => g.Id == documentId);

            var template = await db.Templates.FirstOrDefaultAsync(t => t.Id == doc.TemplateId);
            if (template is null || doc.Recipient is null)
                return new ArchiveOpResult(false, "Шаблон видалено - перегенерація неможлива");

            if (template.Kind == TemplateKind.Group)
                return new ArchiveOpResult(false,
                    $"Шаблон «{template.Name}» - груповий: він формує один документ для всього складу, "
                    + "а не для однієї людини. Перегенерувати з нього документ для окремого одержувача не можна.");

            var mappings = await db.TemplateFieldMappings.Where(m => m.TemplateId == template.Id).ToListAsync();
            var orgSettings = await db.OrganizationSettings.FirstOrDefaultAsync();
            var values = GenerationService.BuildValues(mappings, doc.Recipient, orgSettings, manualValues,
                GenerationService.CourseOfficerSignatureFor(db, mappings));

            var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.docx");
            try
            {
                var result = _documentGenerationService.GenerateOne(template, template.Content, values, tempPath);
                if (!result.Success)
                    return new ArchiveOpResult(false, result.ErrorMessage);

                var bytes = await File.ReadAllBytesAsync(tempPath);
                await AddVersionAsync(db, doc, bytes,
                    Path.GetFileNameWithoutExtension(doc.FileName) is { Length: > 0 } stem
                        ? stem + ".docx"
                        : $"{template.Name}.docx",
                    DocumentSourceType.Generated,
                    _documentHashService.ComputeSourceHash(mappings, doc.Recipient, orgSettings));

                _auditLogService.Log(db, "Перегенеровано", "GeneratedDocument", doc.Id, null, template.Name);
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
                IntakeId = previous.IntakeId,
                OrgNodeIdSnapshot = previous.OrgNodeIdSnapshot,
                OrgPathSnapshot = previous.OrgPathSnapshot,
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

            var maxKb = await db.AppSettings.Select(s => s.MaxDocumentSizeKb).FirstOrDefaultAsync()
                ?? DefaultMaxDocumentSizeKb;
            var info = new FileInfo(filePath);
            if (info.Length > maxKb * 1024L)
                return new ArchiveOpResult(false,
                    $"Файл {info.Length / 1024 / 1024.0:0.#} МБ перевищує ліміт {maxKb / 1024.0:0.#} МБ. Нічого не збережено.");

            var bytes = await File.ReadAllBytesAsync(filePath);
            await AddVersionAsync(db, doc, bytes, Path.GetFileName(filePath), DocumentSourceType.ManualUpload);

            _auditLogService.Log(db, "Завантажено версію", "GeneratedDocument", doc.Id, null,
                Path.GetFileName(filePath), note);
            await db.SaveChangesAsync();
            return new ArchiveOpResult(true, null);
        }

        public async Task<ArchiveOpResult> AttachAsync(int documentId, string filePath, string? note)
        {
            using var db = _dbFactory.CreateDbContext();
            var bytes = await File.ReadAllBytesAsync(filePath);

            db.DocumentAttachments.Add(new DocumentAttachment
            {
                GeneratedDocumentId = documentId,
                FileName = Path.GetFileName(filePath),
                Content = bytes,
                SizeBytes = bytes.LongLength,
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                UploadedBy = _currentUserContext.CurrentUserFullName ?? string.Empty,
                UploadedAt = DateTime.Now
            });

            _auditLogService.Log(db, "Додано вкладення", "GeneratedDocument", documentId, null,
                Path.GetFileName(filePath), note);
            await db.SaveChangesAsync();
            return new ArchiveOpResult(true, null);
        }

        public async Task<ArchiveRowDto?> GetCurrentRowAsync(int recipientId, int templateId)
        {
            using var db = _dbFactory.CreateDbContext();

            return await db.GeneratedDocuments
                .IgnoreQueryFilters()
                .Where(g => g.DeletedAt == null
                    && g.RecipientId == recipientId && g.TemplateId == templateId && g.IsCurrent)
                .Select(g => new ArchiveRowDto(
                    g.Id, g.RecipientId, g.TemplateId,
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
                    g.Version, g.IntakeId,
                    g.IntakeId != null
                        ? db.Intakes.IgnoreQueryFilters()
                            .Where(i => i.Id == g.IntakeId).Select(i => (int?)i.Number).FirstOrDefault()
                        : null,
                    g.OrgPathSnapshot, g.GeneratedAt,
                    db.Users.IgnoreQueryFilters()
                        .Where(u => u.Id == g.GeneratedByUserId).Select(u => u.FullName).FirstOrDefault() ?? "-",
                    g.Attachments.Count(a => a.DeletedAt == null),
                    g.HasContent, g.SourceType, g.FileName, g.SizeBytes))
                .FirstOrDefaultAsync();
        }

        public async Task<ArchiveOpResult> OpenAttachmentAsync(int attachmentId)
        {
            using var db = _dbFactory.CreateDbContext();
            var attachment = await db.DocumentAttachments.FirstOrDefaultAsync(a => a.Id == attachmentId);
            if (attachment is null) return new ArchiveOpResult(false, RecordGoneMessage);

            await _tempFileService.OpenAsync(attachment.FileName, attachment.Content);

            _auditLogService.Log(db, "Відкрито документ", "GeneratedDocument",
                attachment.GeneratedDocumentId, null, attachment.FileName);
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
                    g.GeneratedByUser != null ? g.GeneratedByUser.FullName : "-",
                    g.SizeBytes, g.SourceType, g.IsCurrent, g.HasContent, g.FileName))
                .ToListAsync();
        }

        public async Task<List<AttachmentDto>> GetAttachmentsAsync(int documentId)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.DocumentAttachments
                .Where(a => a.GeneratedDocumentId == documentId)
                .OrderByDescending(a => a.UploadedAt)
                .Select(a => new AttachmentDto(a.Id, a.FileName, a.UploadedAt, a.Note, a.SizeBytes))
                .ToListAsync();
        }

        public async Task DeleteAttachmentAsync(int attachmentId)
        {
            using var db = _dbFactory.CreateDbContext();
            var attachment = await db.DocumentAttachments.FirstOrDefaultAsync(a => a.Id == attachmentId);
            if (attachment is null) return;

            attachment.DeletedAt = DateTime.Now;
            attachment.DeletedBy = _currentUserContext.CurrentUserFullName;

            _auditLogService.Log(db, "Видалено вкладення", "GeneratedDocument",
                attachment.GeneratedDocumentId, attachment.FileName, null);
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
                null, $"в.{target.Version}");
            await db.SaveChangesAsync();
            return target.Id;
        }

        public async Task DeleteAsync(IReadOnlyList<int> documentIds)
        {
            using var db = _dbFactory.CreateDbContext();
            var docs = await db.GeneratedDocuments.Where(g => documentIds.Contains(g.Id)).ToListAsync();
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
                    $"{doc.FileName} (в.{doc.Version})", null);
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
                    g.Recipient != null ? g.Recipient.LastName + " " + g.Recipient.FirstName : "-",
                    g.Template != null ? g.Template.Name : "-",
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
                $"{doc.FileName} (в.{doc.Version})");
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
                    PackageName = r.GenerationPackage != null ? r.GenerationPackage.Name : "Вибірково",
                    r.IntakeId, r.BranchName, r.GeneratedCount, r.SkippedCount, r.ErrorCount
                })
                .ToListAsync();

            var intakeNumbers = await db.Intakes.IgnoreQueryFilters()
                .Select(x => new { x.Id, x.Number }).ToListAsync();
            var numberById = intakeNumbers.ToDictionary(x => x.Id, x => x.Number);

            return runs.Select(r => new RunDto(
                r.Id, r.RunAt, r.PackageName,
                r.IntakeId is int iid ? numberById.GetValueOrDefault(iid) : null,
                r.BranchName, r.GeneratedCount, r.SkippedCount, r.ErrorCount)).ToList();
        }

        public async Task<List<RunItemDto>> GetRunItemsAsync(int runId)
        {
            using var db = _dbFactory.CreateDbContext();

            var items = await db.GeneratedDocuments.IgnoreQueryFilters()
                .Where(g => g.RunId == runId)
                .OrderBy(g => g.Recipient!.LastName)
                .Select(g => new RunItemDto(
                    g.Recipient != null ? g.Recipient.LastName + " " + g.Recipient.FirstName : "-",
                    g.Template != null ? g.Template.Name : "-",
                    "згенеровано", false, g.SizeBytes, g.Id, g.HasContent, g.FileName))
                .ToListAsync();

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
                    g.ExportTemplate != null
                        ? g.ExportTemplate.Name
                        : g.Template != null ? g.Template.Name : "-",
                    g.ExportTemplate != null
                        ? g.ExportTemplate.DeletedAt == null
                        : g.Template != null && g.Template.DeletedAt == null,
                    g.Version,
                    g.RecipientCount,
                    g.GeneratedAt,
                    g.GeneratedByUser != null ? g.GeneratedByUser.FullName : "-",
                    g.HasContent,
                    g.FileName,
                    g.SizeBytes,
                    g.IntakeId))
                .ToListAsync();
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

            _auditLogService.Log(db, "Відкрито документ", "GeneratedGroupDocument", groupDocumentId, null, doc.FileName);
            await db.SaveChangesAsync();
            return new ArchiveOpResult(true, null);
        }

        public async Task<ArchiveOpResult> PrintAsync(int documentId)
        {
            using var db = _dbFactory.CreateDbContext();
            var doc = await db.GeneratedDocuments.FirstOrDefaultAsync(g => g.Id == documentId);
            if (doc is null) return new ArchiveOpResult(false, RecordGoneMessage);
            var content = await db.GeneratedDocumentContents
                .FirstOrDefaultAsync(c => c.GeneratedDocumentId == documentId);
            if (content is null) return new ArchiveOpResult(false, NoContentMessage);

            await _tempFileService.PrintAsync(doc.FileName, content.Content);

            _auditLogService.Log(db, "Надруковано документ", "GeneratedDocument", documentId, null, doc.FileName);
            await db.SaveChangesAsync();
            return new ArchiveOpResult(true, null);
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

            await _tempFileService.PrintAsync(doc.FileName, content.Content);

            _auditLogService.Log(db, "Надруковано документ", "GeneratedGroupDocument", groupDocumentId, null, doc.FileName);
            await db.SaveChangesAsync();
            return new ArchiveOpResult(true, null);
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

            _auditLogService.LogExport(db, "GeneratedGroupDocument", 1, $"1 групова відомість → {Path.GetFileName(targetPath)}");
            await db.SaveChangesAsync();
            return new ArchiveOpResult(true, null);
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
                    $"{doc.FileName} (в.{doc.Version})", null);
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
                    g.GeneratedByUser != null ? g.GeneratedByUser.FullName : "-",
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
                null, $"в.{target.Version}");
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
                    g.ExportTemplate != null
                        ? g.ExportTemplate.Name
                        : g.Template != null ? g.Template.Name : "-",
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
                $"{doc.FileName} (в.{doc.Version})");
            await db.SaveChangesAsync();
        }
    }
}
