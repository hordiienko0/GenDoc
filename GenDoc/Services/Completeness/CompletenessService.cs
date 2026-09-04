using System.IO;
using System.Security.Cryptography;
using GenDoc.Data;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Documents;
using GenDoc.Services.Generation;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services.Completeness
{
    public class CompletenessService : ICompletenessService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly IAuditLogService _auditLogService;
        private readonly ICurrentUserContext _currentUserContext;
        private readonly IDocumentGenerationService _documentGenerationService;
        private readonly IDocumentHashService _documentHashService;
        private readonly IWatermarkService _watermarkService;
        private readonly IIntakeServiceAccessor _intakeAccessor;
        private readonly IUserSettingsService _userSettings;

        public CompletenessService(
            IDbContextFactory<AppDbContext> dbFactory,
            IAuditLogService auditLogService,
            ICurrentUserContext currentUserContext,
            IDocumentGenerationService documentGenerationService,
            IDocumentHashService documentHashService,
            IWatermarkService watermarkService,
            IIntakeServiceAccessor intakeAccessor,
            IUserSettingsService userSettings)
        {
            _dbFactory = dbFactory;
            _auditLogService = auditLogService;
            _currentUserContext = currentUserContext;
            _documentGenerationService = documentGenerationService;
            _documentHashService = documentHashService;
            _watermarkService = watermarkService;
            _intakeAccessor = intakeAccessor;
            _userSettings = userSettings;
        }

        public async Task<MatrixData> BuildAsync(int intakeId, int packageId)
        {
            using var db = _dbFactory.CreateDbContext();
            return await LoadAsync(db, intakeId, packageId);
        }

        public async Task<(int Percent, int IncompletePeople, int RequiredCells, int SatisfiedCells)> GetIntakeSummaryAsync(
            int intakeId, int packageId)
        {
            using var db = _dbFactory.CreateDbContext();
            var data = await LoadAsync(db, intakeId, packageId);

            var requiredCells = 0;
            var satisfiedCells = 0;
            var incompletePeople = 0;

            foreach (var person in data.People)
            {
                var personIncomplete = false;
                foreach (var template in data.Templates)
                {
                    if (ICompletenessService.Resolve(template, person.FitnessCategory) != TemplateRequirement.Required)
                        continue;

                    requiredCells++;
                    var hasDoc = data.Docs.TryGetValue((person.Id, template.TemplateId, template.IsExport), out var doc);
                    if (hasDoc && !doc!.IsStale) satisfiedCells++;
                    else personIncomplete = true;
                }
                if (personIncomplete) incompletePeople++;
            }

            var percent = requiredCells == 0 ? 0 : (int)Math.Round(satisfiedCells * 100.0 / requiredCells);
            return (percent, incompletePeople, requiredCells, satisfiedCells);
        }

        private async Task<MatrixData> LoadAsync(AppDbContext db, int intakeId, int packageId)
        {
            var people = await db.Recipients
                .Where(r => r.IntakeId == intakeId)
                .WithHashSources()
                .AsNoTracking()
                .ToListAsync();
            people = people.OrderBy(r => r.LastName).ThenBy(r => r.FirstName).ToList();

            var templates = await GetPackageLinksInternalAsync(db, packageId, includeGroup: true, includeSheets: true);

            var templateIds = templates.Where(t => !t.IsGroup).Select(t => t.TemplateId).ToList();

            var peopleIds = people.Select(p => p.Id).ToList();
            var docs = await db.GeneratedDocuments
                .Where(g => peopleIds.Contains(g.RecipientId) && g.IsCurrent && templateIds.Contains(g.TemplateId))
                .Select(g => new { g.Id, g.RecipientId, g.TemplateId, g.Version, g.HasContent, g.SourceHash, g.SourceType })
                .ToListAsync();

            var detectStale = await db.AppSettings.Select(s => s.DetectStaleDocuments).FirstOrDefaultAsync() ?? true;

            var mappingsByTemplate = detectStale
                ? (await db.TemplateFieldMappings.Where(m => templateIds.Contains(m.TemplateId)).AsNoTracking().ToListAsync())
                    .GroupBy(m => m.TemplateId).ToDictionary(g => g.Key, g => g.ToList())
                : new Dictionary<int, List<TemplateFieldMapping>>();
            var orgSettings = detectStale ? await db.OrganizationSettings.AsNoTracking().FirstOrDefaultAsync() : null;

            var peopleById = people.ToDictionary(p => p.Id);
            var dict = new Dictionary<(int, int, bool), MatrixDocDto>();
            foreach (var doc in docs)
            {
                var stale = false;
                if (detectStale && doc.SourceHash is not null
                    && peopleById.TryGetValue(doc.RecipientId, out var person)
                    && mappingsByTemplate.TryGetValue(doc.TemplateId, out var mappings))
                {
                    stale = _documentHashService.ComputeSourceHash(mappings, person, orgSettings) != doc.SourceHash;
                }

                dict[(doc.RecipientId, doc.TemplateId, false)] =
                    new MatrixDocDto(doc.Id, doc.RecipientId, doc.TemplateId, doc.Version, doc.HasContent, stale, doc.SourceType);
            }

            var groupColumns = templates.Where(t => t.IsGroup).ToList();
            if (groupColumns.Count > 0)
            {
                var docxIds = groupColumns.Where(t => !t.IsExport).Select(t => t.TemplateId).ToList();
                var sheetIds = groupColumns.Where(t => t.IsExport).Select(t => t.TemplateId).ToList();

                var groupDocs = await db.GeneratedGroupDocuments
                    .Where(g => g.IsCurrent
                                && (g.IntakeId == intakeId || g.IntakeId == null)
                                && ((g.TemplateId != null && docxIds.Contains(g.TemplateId.Value))
                                    || (g.ExportTemplateId != null && sheetIds.Contains(g.ExportTemplateId.Value))))
                    .Select(g => new
                    {
                        g.TemplateId, g.ExportTemplateId, g.IntakeId, g.Id, g.Version, g.HasContent, g.RecipientCount,
                        ParticipantIds = g.Recipients.Select(r => r.RecipientId).ToList()
                    })
                    .ToListAsync();

                foreach (var column in groupColumns)
                {
                    var doc = groupDocs
                        .Where(d => column.IsExport
                            ? d.ExportTemplateId == column.TemplateId
                            : d.TemplateId == column.TemplateId)
                        .OrderByDescending(d => d.IntakeId == intakeId)
                        .ThenByDescending(d => d.Version)
                        .ThenByDescending(d => d.Id)
                        .FirstOrDefault();
                    if (doc is null) continue;

                    var rosterUnknown = doc.RecipientCount > 0 && doc.ParticipantIds.Count == 0;
                    var participants = rosterUnknown ? null : doc.ParticipantIds.ToHashSet();

                    foreach (var person in people)
                    {
                        if (rosterUnknown || participants!.Contains(person.Id))
                            dict[(person.Id, column.TemplateId, column.IsExport)] = new MatrixDocDto(
                                doc.Id, person.Id, column.TemplateId, doc.Version, doc.HasContent,
                                IsStale: false, DocumentSourceType.Generated,
                                IsGroup: true, RosterUnknown: rosterUnknown, IsExport: column.IsExport);
                    }
                }
            }

            return new MatrixData(people, templates, dict, detectStale);
        }

        public async Task<MatrixDocDto?> GetCellAsync(int recipientId, int templateId)
        {
            using var db = _dbFactory.CreateDbContext();
            var doc = await db.GeneratedDocuments
                .Where(g => g.RecipientId == recipientId && g.TemplateId == templateId && g.IsCurrent)
                .Select(g => new { g.Id, g.Version, g.HasContent, g.SourceHash, g.SourceType })
                .FirstOrDefaultAsync();
            if (doc is null) return null;

            var detectStale = await db.AppSettings.Select(s => s.DetectStaleDocuments).FirstOrDefaultAsync() ?? true;
            var stale = false;
            if (detectStale && doc.SourceHash is not null)
            {
                var recipient = await db.Recipients
                    .WithHashSources()
                    .AsNoTracking().FirstOrDefaultAsync(r => r.Id == recipientId);
                var mappings = await db.TemplateFieldMappings
                    .Where(m => m.TemplateId == templateId).AsNoTracking().ToListAsync();
                var orgSettings = await db.OrganizationSettings.AsNoTracking().FirstOrDefaultAsync();
                if (recipient is not null)
                    stale = _documentHashService.ComputeSourceHash(mappings, recipient, orgSettings) != doc.SourceHash;
            }

            return new MatrixDocDto(doc.Id, recipientId, templateId, doc.Version, doc.HasContent, stale, doc.SourceType);
        }

        public async Task<ArchiveOpResult> GenerateForPairAsync(
            int recipientId, int templateId, Dictionary<string, string> manualValues)
        {
            using var db = _dbFactory.CreateDbContext();
            var recipient = await db.Recipients
                .WithHashSources()
                .FirstOrDefaultAsync(r => r.Id == recipientId);
            var template = await db.Templates.FirstOrDefaultAsync(t => t.Id == templateId);
            if (recipient is null || template is null)
                return new ArchiveOpResult(false, "Людину або шаблон не знайдено");

            if (template.Kind == TemplateKind.Group)
                return new ArchiveOpResult(false,
                    $"Шаблон «{template.Name}» - груповий: він формує один документ для всього складу, "
                    + "а не для однієї людини. Сформувати з нього відсутній документ із матриці для "
                    + "одного одержувача не можна.");

            var mappings = await db.TemplateFieldMappings.Where(m => m.TemplateId == templateId).ToListAsync();
            var orgSettings = await db.OrganizationSettings.FirstOrDefaultAsync();
            var values = GenerationService.BuildValues(mappings, recipient, orgSettings, manualValues,
                GenerationService.CourseOfficerSignatureFor(db, mappings));

            var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.docx");
            try
            {
                var result = _documentGenerationService.GenerateOne(template, template.Content, values, tempPath);
                if (!result.Success) return new ArchiveOpResult(false, result.ErrorMessage);

                var bytes = await File.ReadAllBytesAsync(tempPath);

                var maxVersion = await db.GeneratedDocuments.IgnoreQueryFilters()
                    .Where(g => g.RecipientId == recipientId && g.TemplateId == templateId)
                    .MaxAsync(g => (int?)g.Version) ?? 0;

                foreach (var current in db.GeneratedDocuments
                    .Where(g => g.RecipientId == recipientId && g.TemplateId == templateId && g.IsCurrent)
                    .ToList())
                {
                    current.IsCurrent = false;
                }

                var orgPath = await BuildOrgPathAsync(db, recipient.OrgNodeId);
                var doc = new GeneratedDocument
                {
                    RecipientId = recipientId,
                    TemplateId = templateId,
                    GeneratedAt = DateTime.Now,
                    GeneratedByUserId = _currentUserContext.CurrentUserId ?? 0,
                    FileName = SecureTempFileService.SanitizeFileName(
                        $"{recipient.LastName} {recipient.FirstName} - {template.Name}.docx"),
                    SizeBytes = bytes.LongLength,
                    ContentHash = Convert.ToHexString(SHA256.HashData(bytes)),
                    SourceHash = _documentHashService.ComputeSourceHash(mappings, recipient, orgSettings),
                    Version = maxVersion + 1,
                    IsCurrent = true,
                    SourceType = DocumentSourceType.Generated,
                    IntakeId = recipient.IntakeId,
                    OrgNodeIdSnapshot = recipient.OrgNodeId,
                    OrgPathSnapshot = orgPath,
                    HasContent = true,
                    Content = new GeneratedDocumentContent { Content = bytes }
                };
                db.GeneratedDocuments.Add(doc);
                await db.SaveChangesAsync();

                _auditLogService.LogGenerate(db, "GeneratedDocument", doc.Id,
                    $"{recipient.LastName} {recipient.FirstName} · {template.Name}");
                await db.SaveChangesAsync();
                return new ArchiveOpResult(true, null);
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }

        private static async Task<string?> BuildOrgPathAsync(AppDbContext db, int? orgNodeId)
        {
            if (orgNodeId is not int id) return null;
            var nodes = await db.OrgNodes.IgnoreQueryFilters()
                .Select(o => new { o.Id, o.Name, o.ParentId }).ToListAsync();
            var byId = nodes.ToDictionary(n => n.Id, n => (n.Name, n.ParentId));

            var names = new List<string>();
            int? current = id;
            while (current is int cid && byId.TryGetValue(cid, out var node))
            {
                names.Insert(0, node.Name);
                current = node.ParentId;
            }
            return names.Count == 0 ? null : string.Join(" / ", names);
        }

        public async Task<(int People, int Files, List<string> Warnings)> ExportPackagesAsync(
            IReadOnlyList<int> recipientIds, int packageId, string targetFolder)
        {
            using var db = _dbFactory.CreateDbContext();
            var templateIds = await db.GenerationPackageTemplates
                .Where(pt => pt.GenerationPackageId == packageId)
                .Select(pt => pt.TemplateId).ToListAsync();

            var nameTemplate = await db.AppSettings.Select(s => s.ExportFileNameTemplate).FirstOrDefaultAsync();
            if (string.IsNullOrWhiteSpace(nameTemplate)) nameTemplate = "{ПІБ} - {Шаблон}";

            var warnings = new List<string>();
            var peopleExported = 0;
            var filesExported = 0;

            foreach (var recipientId in recipientIds)
            {
                var recipient = await db.Recipients.AsNoTracking().FirstOrDefaultAsync(r => r.Id == recipientId);
                if (recipient is null) continue;

                var docs = await db.GeneratedDocuments
                    .Where(g => g.RecipientId == recipientId && g.IsCurrent && g.HasContent
                        && templateIds.Contains(g.TemplateId))
                    .Include(g => g.Template)
                    .AsNoTracking()
                    .ToListAsync();

                var personDisplay = $"{recipient.LastName} {recipient.FirstName}".Trim();
                if (docs.Count == 0)
                {
                    warnings.Add($"{personDisplay}: немає жодного збереженого документа пакета");
                    continue;
                }

                var initials = string.Join("_", new[] { recipient.FirstName, recipient.MiddleName }
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => char.ToUpperInvariant(p![0]).ToString()));
                var folderName = SecureTempFileService.SanitizeFileName(
                    initials.Length > 0
                        ? $"{recipient.LastName.ToUpper(System.Globalization.CultureInfo.GetCultureInfo("uk-UA"))}_{initials}"
                        : recipient.LastName.ToUpper(System.Globalization.CultureInfo.GetCultureInfo("uk-UA")));
                var personFolder = Path.Combine(targetFolder, folderName);
                Directory.CreateDirectory(personFolder);

                var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var exportedForPerson = 0;

                foreach (var doc in docs)
                {
                    var content = await db.GeneratedDocumentContents
                        .Where(c => c.GeneratedDocumentId == doc.Id)
                        .Select(c => c.Content)
                        .FirstOrDefaultAsync();
                    if (content is null)
                    {
                        warnings.Add($"{personDisplay} / {doc.Template?.Name}: файл не збережено");
                        continue;
                    }

                    var person = string.Join(' ', new[] { recipient.LastName, recipient.FirstName, recipient.MiddleName }
                        .Where(p => !string.IsNullOrWhiteSpace(p)));
                    var baseName = SecureTempFileService.SanitizeFileName(nameTemplate
                        .Replace("{ПІБ}", person)
                        .Replace("{Шаблон}", doc.Template?.Name ?? "документ")
                        .Replace("{Дата}", doc.GeneratedAt.ToString("dd.MM.yyyy")));
                    var extension = Path.GetExtension(doc.FileName);

                    var path = Path.Combine(personFolder, baseName + extension);
                    var suffix = 2;
                    while (usedPaths.Contains(path) || File.Exists(path))
                        path = Path.Combine(personFolder, $"{baseName}_{suffix++}{extension}");
                    usedPaths.Add(path);

                    await File.WriteAllBytesAsync(path, _watermarkService.Apply(content, doc.FileName));
                    filesExported++;
                    exportedForPerson++;
                }

                if (exportedForPerson > 0) peopleExported++;
            }

            if (filesExported > 0)
            {
                _auditLogService.LogExport(db, "GeneratedDocument", filesExported,
                    $"пакети {peopleExported} осіб, {filesExported} файлів → {targetFolder}");
                await db.SaveChangesAsync();
            }

            return (peopleExported, filesExported, warnings);
        }

        public async Task<List<RecipientDocStatus>> GetRecipientStatusAsync(int recipientId, int packageId)
        {
            List<MatrixTemplateInfo> templates;
            string? fitnessCategory;
            using (var db = _dbFactory.CreateDbContext())
            {
                templates = await GetPackageLinksInternalAsync(db, packageId, includeGroup: false);
                fitnessCategory = await db.Recipients
                    .Where(r => r.Id == recipientId).Select(r => r.FitnessCategory).FirstOrDefaultAsync();
            }

            var result = new List<RecipientDocStatus>(templates.Count);

            foreach (var template in templates)
            {
                var requirement = ICompletenessService.Resolve(template, fitnessCategory);
                var cell = await GetCellAsync(recipientId, template.TemplateId);

                result.Add(cell is null
                    ? new RecipientDocStatus(template.TemplateId, template.Name, null, 0, false, false, requirement)
                    : new RecipientDocStatus(template.TemplateId, template.Name, cell.Id, cell.Version,
                        cell.HasContent, cell.IsStale, requirement));
            }

            return result;
        }

        public async Task<(int Generated, int Skipped, List<string> Errors)> GenerateMissingForRecipientAsync(
            int recipientId, int packageId, Dictionary<string, string> manualValues)
        {
            var statuses = await GetRecipientStatusAsync(recipientId, packageId);
            var generated = 0;
            var skipped = 0;
            var errors = new List<string>();

            foreach (var status in statuses)
            {
                if (status.Requirement == TemplateRequirement.NotApplicable) { skipped++; continue; }
                if (status.HasContent) { skipped++; continue; }

                var result = await GenerateForPairAsync(recipientId, status.TemplateId, manualValues);
                if (result.Success) generated++;
                else errors.Add($"{status.TemplateName}: {result.ErrorMessage}");
            }

            return (generated, skipped, errors);
        }

        public async Task<int?> GetDefaultPackageIdAsync()
        {
            using var db = _dbFactory.CreateDbContext();

            var mine = (await _userSettings.GetForCurrentUserAsync()).LastPackageId;
            if (mine is int m && await db.GenerationPackages.AnyAsync(p => p.Id == m)) return m;

            var configured = await db.AppSettings.Select(s => s.DefaultGenerationPackageId).FirstOrDefaultAsync();
            if (configured is int id && await db.GenerationPackages.AnyAsync(p => p.Id == id)) return id;

            return await db.GenerationPackages.OrderBy(p => p.Name).Select(p => (int?)p.Id).FirstOrDefaultAsync();
        }

        public async Task<int> GetBadgeCountAsync()
        {
            var breakdown = await GetBadgeBreakdownAsync();
            return breakdown.Missing + breakdown.Stale;
        }

        public async Task<(int Missing, int Stale)> GetBadgeBreakdownAsync()
        {
            var intake = _intakeAccessor.ActiveIntake;
            if (intake is null) return (0, 0);

            var packageId = await GetDefaultPackageIdAsync();
            if (packageId is not int pid) return (0, 0);

            var data = await BuildAsync(intake.Id, pid);
            var missing = 0;
            var stale = 0;
            foreach (var person in data.People)
            {
                foreach (var template in data.Templates)
                {
                    if (ICompletenessService.Resolve(template, person.FitnessCategory) != TemplateRequirement.Required)
                        continue;

                    var hasDoc = data.Docs.TryGetValue((person.Id, template.TemplateId, template.IsExport), out var doc);

                    if (!hasDoc)
                    {
                        if (!template.IsGroup) missing++;
                    }
                    else if (doc!.IsStale)
                    {
                        stale++;
                    }
                }
            }
            return (missing, stale);
        }

        public async Task<List<MatrixTemplateInfo>> GetPackageLinksAsync(int packageId)
        {
            using var db = _dbFactory.CreateDbContext();
            return await GetPackageLinksInternalAsync(db, packageId, includeGroup: true);
        }

        private static async Task<List<MatrixTemplateInfo>> GetPackageLinksInternalAsync(
            AppDbContext db, int packageId, bool includeGroup, bool includeSheets = false)
        {
            var query = db.GenerationPackageTemplates
                .Where(pt => pt.GenerationPackageId == packageId && pt.Template != null && pt.Template.DeletedAt == null);
            if (!includeGroup) query = query.Where(pt => pt.Template!.Kind != TemplateKind.Group);

            var links = await query
                .OrderBy(pt => pt.SortOrder)
                .Select(pt => new MatrixTemplateInfo(
                    pt.Id, pt.TemplateId, pt.Template!.Name, pt.Template.ShortName,
                    pt.SortOrder, pt.RequirementRegular, pt.RequirementLimited,
                    pt.Template.Kind == TemplateKind.Group, false))
                .ToListAsync();

            if (!includeSheets) return links;

            var sheets = await db.GenerationPackageExportTemplates
                .Where(pt => pt.GenerationPackageId == packageId
                             && pt.ExportTemplate != null && pt.ExportTemplate.DeletedAt == null)
                .OrderBy(pt => pt.SortOrder)
                .Select(pt => new
                {
                    pt.Id,
                    pt.ExportTemplateId,
                    pt.ExportTemplate!.Name,
                    pt.SortOrder,
                    pt.FitnessFilter
                })
                .ToListAsync();

            links.AddRange(sheets.Select(s => new MatrixTemplateInfo(
                s.Id, s.ExportTemplateId, s.Name, null, s.SortOrder,
                RequiredWhen(s.FitnessFilter, FitnessFilter.RegularOnly),
                RequiredWhen(s.FitnessFilter, FitnessFilter.LimitedOnly),
                IsGroup: true, IsExport: true)));

            return links;
        }

        private static TemplateRequirement RequiredWhen(FitnessFilter filter, FitnessFilter category)
            => filter == FitnessFilter.All || filter == category
                ? TemplateRequirement.Required
                : TemplateRequirement.NotApplicable;

        public async Task<List<PackageGroupDocumentStatus>> GetPackageGroupDocumentsAsync(int packageId, int? intakeId, int? recipientId = null)
        {
            using var db = _dbFactory.CreateDbContext();
            var groupTemplates = (await GetPackageLinksInternalAsync(db, packageId, includeGroup: true))
                .Where(t => t.IsGroup).ToList();
            if (groupTemplates.Count == 0) return new List<PackageGroupDocumentStatus>();

            var ids = groupTemplates.Select(t => t.TemplateId).ToList();
            var docs = await db.GeneratedGroupDocuments
                .Where(g => g.TemplateId != null && ids.Contains(g.TemplateId.Value) && g.IsCurrent
                            && (g.IntakeId == intakeId || g.IntakeId == null))
                .Select(g => new
                {
                    g.TemplateId, g.IntakeId, g.Id, g.Version, g.RecipientCount,
                    ParticipantCount = g.Recipients.Count,
                    IsParticipant = recipientId != null && g.Recipients.Any(r => r.RecipientId == recipientId)
                })
                .ToListAsync();

            return groupTemplates.Select(t =>
            {
                var doc = docs.Where(d => d.TemplateId == t.TemplateId)
                    .OrderByDescending(d => d.IntakeId == intakeId)
                    .FirstOrDefault();
                return new PackageGroupDocumentStatus(
                    t.TemplateId, t.Name, doc?.Id, doc?.Version ?? 0,
                    IsParticipant: doc?.IsParticipant ?? false,
                    RosterUnknown: doc is not null && doc.RecipientCount > 0 && doc.ParticipantCount == 0);
            }).ToList();
        }

        public async Task<List<(int Id, string Name)>> GetTemplatesNotInPackageAsync(int packageId)
        {
            using var db = _dbFactory.CreateDbContext();
            var inPackage = await db.GenerationPackageTemplates
                .Where(pt => pt.GenerationPackageId == packageId)
                .Select(pt => pt.TemplateId).ToListAsync();
            var templates = await db.Templates
                .Where(t => !inPackage.Contains(t.Id))
                .OrderBy(t => t.Name)
                .Select(t => new { t.Id, t.Name })
                .ToListAsync();
            return templates.Select(t => (t.Id, t.Name)).ToList();
        }

        public async Task<List<int>> GetTemplateIdsWithDocumentsAsync(IReadOnlyList<int> templateIds)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.GeneratedDocuments.IgnoreQueryFilters()
                .Where(g => templateIds.Contains(g.TemplateId))
                .Select(g => g.TemplateId)
                .Distinct()
                .ToListAsync();
        }

        public async Task SaveRequirementsAsync(int packageId, IReadOnlyList<RequirementRow> rows)
        {
            using var db = _dbFactory.CreateDbContext();
            var existing = await db.GenerationPackageTemplates
                .Where(pt => pt.GenerationPackageId == packageId)
                .ToListAsync();

            async Task ApplyAsync()
            {
                var oldSummary = Summarize(existing.Select(e =>
                    (e.TemplateId, e.RequirementRegular, e.RequirementLimited)));

                var keptLinkIds = rows.Where(r => r.LinkId is not null).Select(r => r.LinkId!.Value).ToHashSet();
                foreach (var link in existing.Where(l => !keptLinkIds.Contains(l.Id)))
                    db.GenerationPackageTemplates.Remove(link);

                foreach (var row in rows)
                {
                    var link = row.LinkId is int linkId ? existing.FirstOrDefault(l => l.Id == linkId) : null;

                    if (link is not null)
                    {
                        link.RequirementRegular = row.RequirementRegular;
                        link.RequirementLimited = row.RequirementLimited;
                        link.SortOrder = row.SortOrder;
                    }
                    else
                    {
                        db.GenerationPackageTemplates.Add(new GenerationPackageTemplate
                        {
                            GenerationPackageId = packageId,
                            TemplateId = row.TemplateId,
                            RequirementRegular = row.RequirementRegular,
                            RequirementLimited = row.RequirementLimited,
                            SortOrder = row.SortOrder
                        });
                    }
                }

                var newSummary = Summarize(rows.Select(r =>
                    (r.TemplateId, r.RequirementRegular, r.RequirementLimited)));

                _auditLogService.Log(db, "Змінено вимоги пакета", "GenerationPackage", packageId,
                    oldSummary, newSummary);
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

        private static string Summarize(
            IEnumerable<(int TemplateId, TemplateRequirement Regular, TemplateRequirement Limited)> rows)
            => string.Join("; ", rows.Select(r => $"T{r.TemplateId}:{Abbr(r.Regular)}/{Abbr(r.Limited)}"));

        private static string Abbr(TemplateRequirement requirement) => requirement switch
        {
            TemplateRequirement.Optional => "опц",
            TemplateRequirement.NotApplicable => "н/п",
            _ => "об"
        };
    }

    public interface IIntakeServiceAccessor
    {
        Intake? ActiveIntake { get; }
    }

    public class ActiveIntakeAccessor : IIntakeServiceAccessor
    {
        private readonly ActiveIntakeState _state;
        public ActiveIntakeAccessor(ActiveIntakeState state) => _state = state;
        public Intake? ActiveIntake => _state.Current;
    }
}
