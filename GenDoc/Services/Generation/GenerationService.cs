using System.Globalization;
using System.IO;
using GenDoc.Data;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Documents;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services.Generation
{
    public class GenerationService : IGenerationService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly IDocumentGenerationService _documentGenerationService;
        private readonly IXlsxGenerationService _xlsxGenerationService;
        private readonly IAuditLogService _auditLogService;
        private readonly ICurrentUserContext _currentUserContext;
        private readonly Documents.IDocumentHashService _documentHashService;

        public GenerationService(
            IDbContextFactory<AppDbContext> dbFactory,
            IDocumentGenerationService documentGenerationService,
            IXlsxGenerationService xlsxGenerationService,
            IAuditLogService auditLogService,
            ICurrentUserContext currentUserContext,
            Documents.IDocumentHashService documentHashService)
        {
            _dbFactory = dbFactory;
            _documentGenerationService = documentGenerationService;
            _xlsxGenerationService = xlsxGenerationService;
            _auditLogService = auditLogService;
            _currentUserContext = currentUserContext;
            _documentHashService = documentHashService;
        }

        public List<(int Id, string Name, string? Description, int TemplateCount)> GetPackages()
        {
            using var db = _dbFactory.CreateDbContext();
            return db.GenerationPackages
                .OrderBy(p => p.Name)
                .Select(p => new { p.Id, p.Name, p.Description, TemplateCount = p.Templates.Count + p.ExportTemplates.Count })
                .AsEnumerable()
                .Select(p => (p.Id, p.Name, p.Description, p.TemplateCount))
                .ToList();
        }

        public List<(int Id, string Name)> GetAllTemplates(
            Models.Enums.TemplateAudience audience = Models.Enums.TemplateAudience.Intake)
        {
            using var db = _dbFactory.CreateDbContext();
            return db.Templates
                .Where(t => t.Audience == audience)
                .OrderBy(t => t.Name)
                .Select(t => new { t.Id, t.Name })
                .AsEnumerable()
                .Select(t => (t.Id, t.Name))
                .ToList();
        }

        public List<(int Id, string Name)> GetPerRecipientTemplates(
            Models.Enums.TemplateAudience audience = Models.Enums.TemplateAudience.Intake)
        {
            using var db = _dbFactory.CreateDbContext();
            return db.Templates
                .Where(t => t.Kind == Models.Enums.TemplateKind.PerRecipient && t.Audience == audience)
                .OrderBy(t => t.Name)
                .Select(t => new { t.Id, t.Name })
                .AsEnumerable()
                .Select(t => (t.Id, t.Name))
                .ToList();
        }

        public List<(int Id, string Name)> GetAllExportTemplates()
        {
            using var db = _dbFactory.CreateDbContext();
            return db.ExportTemplates
                .Where(t => t.UsesPlaceholders)
                .OrderBy(t => t.Name)
                .Select(t => new { t.Id, t.Name })
                .AsEnumerable()
                .Select(t => (t.Id, t.Name))
                .ToList();
        }

        public void CreatePackage(
            string name, string? description, List<int> templateIds,
            List<(int ExportTemplateId, FitnessFilter Filter)> exportTemplates)
        {
            using var db = _dbFactory.CreateDbContext();

            var package = new GenerationPackage
            {
                Name = name.Trim(),
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim()
            };

            for (var i = 0; i < templateIds.Count; i++)
            {
                package.Templates.Add(new GenerationPackageTemplate { TemplateId = templateIds[i], SortOrder = i });
            }

            for (var i = 0; i < exportTemplates.Count; i++)
            {
                package.ExportTemplates.Add(new GenerationPackageExportTemplate
                {
                    ExportTemplateId = exportTemplates[i].ExportTemplateId,
                    FitnessFilter = exportTemplates[i].Filter,
                    SortOrder = i
                });
            }

            db.GenerationPackages.Add(package);
            db.SaveChanges();

            _auditLogService.LogCreate(db, "GenerationPackage", package.Id, package.Name,
                $"Шаблонів: {templateIds.Count}, XLSX: {exportTemplates.Count}");
            db.SaveChanges();
        }

        public void DeletePackage(int packageId)
        {
            using var db = _dbFactory.CreateDbContext();
            var package = db.GenerationPackages.First(p => p.Id == packageId);
            var snapshot = package.Name;

            package.DeletedAt = DateTime.Now;
            package.DeletedBy = _currentUserContext.CurrentUserFullName;

            _auditLogService.LogDelete(db, "GenerationPackage", package.Id, snapshot);
            db.SaveChanges();
        }

        public List<(int TemplateId, string TemplateName)> GetPackageTemplates(int packageId)
        {
            using var db = _dbFactory.CreateDbContext();
            return db.GenerationPackageTemplates
                .Where(pt => pt.GenerationPackageId == packageId)
                .OrderBy(pt => pt.SortOrder)
                .Select(pt => new { pt.TemplateId, Name = pt.Template!.Name })
                .AsEnumerable()
                .Select(pt => (pt.TemplateId, pt.Name))
                .ToList();
        }

        public List<(int LinkId, int ExportTemplateId, string Name, int SortOrder, FitnessFilter FitnessFilter)> GetPackageExportTemplates(int packageId)
        {
            using var db = _dbFactory.CreateDbContext();
            return db.GenerationPackageExportTemplates
                .Where(pt => pt.GenerationPackageId == packageId)
                .OrderBy(pt => pt.SortOrder)
                .Select(pt => new { pt.Id, pt.ExportTemplateId, Name = pt.ExportTemplate!.Name, pt.SortOrder, pt.FitnessFilter })
                .AsEnumerable()
                .Select(pt => (pt.Id, pt.ExportTemplateId, pt.Name, pt.SortOrder, pt.FitnessFilter))
                .ToList();
        }

        public List<(int Id, string Name)> GetExportTemplatesNotInPackage(int packageId)
        {
            using var db = _dbFactory.CreateDbContext();
            var usedIds = db.GenerationPackageExportTemplates
                .Where(pt => pt.GenerationPackageId == packageId)
                .Select(pt => pt.ExportTemplateId)
                .ToList();

            return db.ExportTemplates
                .Where(t => t.UsesPlaceholders && !usedIds.Contains(t.Id))
                .OrderBy(t => t.Name)
                .Select(t => new { t.Id, t.Name })
                .AsEnumerable()
                .Select(t => (t.Id, t.Name))
                .ToList();
        }

        public void SaveExportTemplates(int packageId, List<(int? LinkId, int ExportTemplateId, int SortOrder, FitnessFilter FitnessFilter)> rows)
        {
            using var db = _dbFactory.CreateDbContext();
            var existing = db.GenerationPackageExportTemplates.Where(pt => pt.GenerationPackageId == packageId).ToList();

            var oldSnapshot = string.Join(", ", existing.OrderBy(e => e.SortOrder).Select(e => $"{e.ExportTemplateId}:{e.FitnessFilter}"));

            var keptIds = new HashSet<int>();
            foreach (var row in rows)
            {
                if (row.LinkId is int linkId)
                {
                    var link = existing.First(e => e.Id == linkId);
                    link.SortOrder = row.SortOrder;
                    link.FitnessFilter = row.FitnessFilter;
                    keptIds.Add(link.Id);
                }
                else
                {
                    var link = new GenerationPackageExportTemplate
                    {
                        GenerationPackageId = packageId,
                        ExportTemplateId = row.ExportTemplateId,
                        SortOrder = row.SortOrder,
                        FitnessFilter = row.FitnessFilter
                    };
                    db.GenerationPackageExportTemplates.Add(link);
                }
            }

            foreach (var stale in existing.Where(e => !keptIds.Contains(e.Id)))
                db.GenerationPackageExportTemplates.Remove(stale);

            db.SaveChanges();

            var newSnapshot = string.Join(", ", db.GenerationPackageExportTemplates
                .Where(pt => pt.GenerationPackageId == packageId)
                .OrderBy(e => e.SortOrder)
                .Select(e => $"{e.ExportTemplateId}:{e.FitnessFilter}"));

            _auditLogService.LogUpdate(db, "GenerationPackage", packageId, oldSnapshot, newSnapshot, "Оновлено XLSX-шаблони пакета");
            db.SaveChanges();
        }

        public List<string> GetManualTags(int packageId)
        {
            using var db = _dbFactory.CreateDbContext();

            var templateIds = db.GenerationPackageTemplates
                .Where(pt => pt.GenerationPackageId == packageId)
                .OrderBy(pt => pt.SortOrder)
                .Select(pt => pt.TemplateId)
                .ToList();

            var exportTemplateIds = db.GenerationPackageExportTemplates
                .Where(pt => pt.GenerationPackageId == packageId)
                .OrderBy(pt => pt.SortOrder)
                .Select(pt => pt.ExportTemplateId)
                .ToList();

            return CollectManualTags(db, templateIds, exportTemplateIds);
        }

        public List<string> GetManualTagsForTemplates(IReadOnlyList<int> templateIds, IReadOnlyList<int> exportTemplateIds)
        {
            using var db = _dbFactory.CreateDbContext();
            return CollectManualTags(db, templateIds, exportTemplateIds);
        }

        private static List<string> CollectManualTags(
            AppDbContext db, IReadOnlyList<int> templateIds, IReadOnlyList<int> exportTemplateIds)
        {
            var seen = new HashSet<string>();
            var tags = new List<string>();

            foreach (var templateId in templateIds)
            {
                var manualTags = db.TemplateFieldMappings
                    .Where(m => m.TemplateId == templateId && m.SourceType == MappingSourceType.Manual && !m.IsInsideRepeatingBlock)
                    .OrderBy(m => m.PlaceholderTag)
                    .Select(m => m.PlaceholderTag)
                    .ToList();

                foreach (var tag in manualTags)
                {
                    if (seen.Add(tag)) tags.Add(tag);
                }
            }

            foreach (var templateId in exportTemplateIds)
            {
                var manualTags = db.ExportTemplateColumnMappings
                    .Where(m => m.ExportTemplateId == templateId && m.SourceType == MappingSourceType.Manual)
                    .OrderBy(m => m.PlaceholderTag)
                    .Select(m => m.PlaceholderTag)
                    .ToList();

                foreach (var tag in manualTags)
                {
                    if (seen.Add(tag)) tags.Add(tag);
                }
            }

            return tags;
        }

        public bool ExportTemplatesNeedCourseOfficer(IReadOnlyList<int> exportTemplateIds)
        {
            if (exportTemplateIds.Count == 0) return false;
            using var db = _dbFactory.CreateDbContext();
            return db.ExportTemplateColumnMappings.Any(m =>
                exportTemplateIds.Contains(m.ExportTemplateId)
                && m.FieldKey == nameof(ExportFieldKey.CourseOfficerSignature));
        }

        public bool PackageNeedsCourseOfficer(int packageId)
        {
            using var db = _dbFactory.CreateDbContext();

            var exportTemplateIds = db.GenerationPackageExportTemplates
                .Where(pt => pt.GenerationPackageId == packageId)
                .Select(pt => pt.ExportTemplateId);

            var templateIds = db.GenerationPackageTemplates
                .Where(pt => pt.GenerationPackageId == packageId)
                .Select(pt => pt.TemplateId);

            return db.ExportTemplateColumnMappings.Any(m =>
                       exportTemplateIds.Contains(m.ExportTemplateId)
                       && m.FieldKey == nameof(ExportFieldKey.CourseOfficerSignature))
                   || db.TemplateFieldMappings.Any(m =>
                       templateIds.Contains(m.TemplateId)
                       && m.SourceType == MappingSourceType.Recipient
                       && m.FieldName == nameof(ExportFieldKey.CourseOfficerSignature));
        }

        public LastRunInfo? GetLastRun()
        {
            using var db = _dbFactory.CreateDbContext();

            return db.GenerationPackageRuns
                .Where(r => r.GenerationPackageId != null)
                .OrderByDescending(r => r.RunAt)
                .ThenByDescending(r => r.Id)
                .Join(db.GenerationPackages,
                    run => run.GenerationPackageId!.Value,
                    package => package.Id,
                    (run, package) => new LastRunInfo(
                        package.Id, package.Name, run.RunAt,
                        run.GeneratedCount, run.SkippedCount, run.ErrorCount))
                .FirstOrDefault();
        }

        public int GetRecipientCount(RosterSelection selection)
        {
            using var db = _dbFactory.CreateDbContext();
            return LoadRosterRecipients(db, selection).Count;
        }

        public RunResult RunPackage(
            int packageId,
            string outputFolder,
            Dictionary<string, string> manualValues,
            bool regenerateExisting,
            RosterSelection rosterSelection,
            IProgress<string> progress,
            int? courseOfficerId = null)
        {
            using var db = _dbFactory.CreateDbContext();

            var package = db.GenerationPackages.First(p => p.Id == packageId);

            var allTemplates = db.GenerationPackageTemplates
                .Where(pt => pt.GenerationPackageId == packageId)
                .OrderBy(pt => pt.SortOrder)
                .Select(pt => pt.Template!)
                .ToList();

            var templates = allTemplates.Where(t => t.Kind == TemplateKind.PerRecipient).ToList();
            var groupDocxTemplates = allTemplates.Where(t => t.Kind == TemplateKind.Group).ToList();

            var exportLinks = db.GenerationPackageExportTemplates
                .Where(pt => pt.GenerationPackageId == packageId)
                .OrderBy(pt => pt.SortOrder)
                .Include(pt => pt.ExportTemplate)
                .ToList();

            var recipients = LoadRosterRecipients(db, rosterSelection);
            var orgSettings = db.OrganizationSettings.FirstOrDefault();

            var run = new GenerationPackageRun
            {
                GenerationPackageId = packageId,
                RunAt = DateTime.Now,
                RunByUserId = _currentUserContext.CurrentUserId ?? 0,
                IntakeId = RunIntakeResolver.Resolve(recipients.Select(r => r.IntakeId))
            };
            db.GenerationPackageRuns.Add(run);
            db.SaveChanges();

            Directory.CreateDirectory(outputFolder);
            var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var runStamp = ResolveRunStamp(outputFolder);

            var docx = RunDocxPhase(db, templates, recipients, orgSettings, run, manualValues, outputFolder, usedFileNames, runStamp, regenerateExisting, progress, courseOfficerId);
            var xlsx = RunXlsxPhase(db, exportLinks, recipients, orgSettings, run, manualValues, outputFolder, usedFileNames, runStamp, regenerateExisting, progress, courseOfficerId);
            var docxGroup = RunDocxGroupPhase(db, groupDocxTemplates, recipients, orgSettings, run, manualValues, outputFolder, usedFileNames, runStamp, regenerateExisting, progress);

            run.GeneratedCount = docx.Generated + xlsx.Generated + docxGroup.Generated;
            run.SkippedCount = docx.Skipped + xlsx.Skipped + docxGroup.Skipped;
            run.ErrorCount = docx.Errors + xlsx.Errors + docxGroup.Errors;

            var issues = new List<RunIssue>(docx.Issues);
            issues.AddRange(xlsx.Issues);
            issues.AddRange(docxGroup.Issues);
            run.Summary = RunIssue.Serialize(issues);

            _auditLogService.LogGenerate(db, "GenerationPackage", packageId,
                $"{package.Name}: docx - згенеровано {docx.Generated}, пропущено {docx.Skipped}, помилок {docx.Errors}; " +
                $"xlsx - згенеровано {xlsx.Generated}, пропущено {xlsx.Skipped}, помилок {xlsx.Errors}; " +
                $"груповий docx - згенеровано {docxGroup.Generated}, пропущено {docxGroup.Skipped}, помилок {docxGroup.Errors}");

            db.SaveChanges();

            return new RunResult(
                docx.Generated, docx.Skipped, docx.Errors,
                xlsx.Generated, xlsx.Skipped, xlsx.Errors,
                docxGroup.Generated, docxGroup.Skipped, docxGroup.Errors,
                run.Id, issues);
        }

        public RunResult GenerateTemplatesForRecipients(
            IReadOnlyList<int> templateIds,
            IReadOnlyList<int> exportTemplateIds,
            IReadOnlyList<int> recipientIds,
            string outputFolder,
            Dictionary<string, string> manualValues,
            IProgress<string> progress,
            int? courseOfficerId = null)
        {
            using var db = _dbFactory.CreateDbContext();

            var requested = db.Templates
                .Where(t => templateIds.Contains(t.Id))
                .ToList();

            var templates = requested.Where(t => t.Kind == TemplateKind.PerRecipient).ToList();

            var groupTemplates = requested.Where(t => t.Kind == TemplateKind.Group).ToList();

            var exportTemplates = db.ExportTemplates
                .Where(t => exportTemplateIds.Contains(t.Id))
                .AsNoTracking()
                .ToList();
            var exportLinks = exportTemplates
                .Select((t, i) => new GenerationPackageExportTemplate
                {
                    ExportTemplateId = t.Id, ExportTemplate = t, FitnessFilter = FitnessFilter.All, SortOrder = i
                })
                .ToList();

            var recipients = LoadRosterRecipients(db, new RosterSelection(
                false, recipientIds, FitnessFilter.All, false, Array.Empty<RankCategory>(), Array.Empty<string>()));
            var orgSettings = db.OrganizationSettings.FirstOrDefault();

            var run = new GenerationPackageRun
            {
                GenerationPackageId = null,
                RunAt = DateTime.Now,
                RunByUserId = _currentUserContext.CurrentUserId ?? 0,
                IntakeId = RunIntakeResolver.Resolve(recipients.Select(r => r.IntakeId))
            };
            db.GenerationPackageRuns.Add(run);
            db.SaveChanges();

            Directory.CreateDirectory(outputFolder);
            var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var runStamp = ResolveRunStamp(outputFolder);

            var docx = RunDocxPhase(db, templates, recipients, orgSettings, run, manualValues, outputFolder,
                usedFileNames, runStamp, regenerateExisting: true, progress, courseOfficerId);
            var xlsx = RunXlsxPhase(db, exportLinks, recipients, orgSettings, run, manualValues, outputFolder,
                usedFileNames, runStamp, regenerateExisting: true, progress, courseOfficerId);
            var docxGroup = RunDocxGroupPhase(db, groupTemplates, recipients, orgSettings, run, manualValues,
                outputFolder, usedFileNames, runStamp, regenerateExisting: true, progress);

            run.GeneratedCount = docx.Generated + xlsx.Generated + docxGroup.Generated;
            run.SkippedCount = docx.Skipped + xlsx.Skipped + docxGroup.Skipped;
            run.ErrorCount = docx.Errors + xlsx.Errors + docxGroup.Errors;
            var issues = new List<RunIssue>(docx.Issues);
            issues.AddRange(xlsx.Issues);
            issues.AddRange(docxGroup.Issues);
            run.Summary = RunIssue.Serialize(issues);

            _auditLogService.LogGenerate(db, "GenerationPackageRun", run.Id,
                $"Вибірково: шаблонів {templates.Count}, групових {groupTemplates.Count}, "
                + $"відомостей {exportTemplates.Count}, осіб {recipients.Count}; "
                + $"згенеровано {run.GeneratedCount}, помилок {run.ErrorCount}");
            db.SaveChanges();

            return new RunResult(
                docx.Generated, docx.Skipped, docx.Errors,
                xlsx.Generated, xlsx.Skipped, xlsx.Errors,
                docxGroup.Generated, docxGroup.Skipped, docxGroup.Errors, run.Id, issues);
        }

        private static List<Recipient> LoadRosterRecipients(AppDbContext db, RosterSelection selection)
        {
            IQueryable<Recipient> query = db.Recipients.WithHashSources();

            if (!selection.AllRecipients)
                query = query.Where(r => selection.RecipientIds.Contains(r.Id));

            if (selection.PermanentStaffOnly)
                query = query.Where(r => r.IntakeId == null);

            var recipients = query.ToList();

            if (selection.AllRecipients && !selection.PermanentStaffOnly)
                recipients = recipients.Where(r => selection.Covers(r.IntakeId)).ToList();

            if (selection.FitnessFilter != FitnessFilter.All)
                recipients = recipients.Where(r => FitnessCategoryHelper.Matches(selection.FitnessFilter, r.FitnessCategory)).ToList();

            if (selection.RankCategories.Count > 0)
                recipients = recipients.Where(r => selection.RankCategories.Contains(RankOrder.Category(r.Rank))).ToList();

            if (selection.Ranks.Count > 0)
            {
                var normalizedRanks = new HashSet<string>(selection.Ranks.Select(RankOrder.Normalize), StringComparer.Ordinal);
                recipients = recipients.Where(r => normalizedRanks.Contains(RankOrder.Normalize(r.Rank))).ToList();
            }

            return recipients;
        }

        private sealed record DocxPhaseResult(int Generated, int Skipped, int Errors, List<RunIssue> Issues);

        private DocxPhaseResult RunDocxPhase(
            AppDbContext db,
            List<Template> templates,
            List<Recipient> recipients,
            OrganizationSettings? orgSettings,
            GenerationPackageRun run,
            Dictionary<string, string> manualValues,
            string outputFolder,
            HashSet<string> usedFileNames,
            string runStamp,
            bool regenerateExisting,
            IProgress<string> progress,
            int? courseOfficerId)
        {
            var mappingsByTemplate = templates.ToDictionary(
                t => t.Id,
                t => db.TemplateFieldMappings.Where(m => m.TemplateId == t.Id).ToList());

            var courseOfficerSignature = CourseOfficerSignatureFor(db, mappingsByTemplate.Values.SelectMany(m => m), courseOfficerId);

            var existingFileNames = db.GeneratedDocuments
                .Where(g => g.IsCurrent)
                .Select(g => new { g.RecipientId, g.TemplateId, g.FileName })
                .AsEnumerable()
                .GroupBy(g => (g.RecipientId, g.TemplateId))
                .ToDictionary(g => g.Key, g => g.First().FileName);

            var maxVersions = db.GeneratedDocuments.IgnoreQueryFilters()
                .GroupBy(g => new { g.RecipientId, g.TemplateId })
                .Select(g => new { g.Key.RecipientId, g.Key.TemplateId, MaxVersion = g.Max(x => x.Version) })
                .AsEnumerable()
                .ToDictionary(g => (g.RecipientId, g.TemplateId), g => g.MaxVersion);

            var orgNodeNames = db.OrgNodes.IgnoreQueryFilters()
                .Select(o => new { o.Id, o.Name, o.ParentId })
                .AsEnumerable()
                .ToDictionary(o => o.Id, o => (o.Name, o.ParentId));

            var intakeNames = LoadIntakeNames(db);

            var generated = 0;
            var skipped = 0;
            var errors = 0;
            var issues = new List<RunIssue>();

            var total = recipients.Count * templates.Count;
            var n = 0;

            foreach (var recipient in recipients)
            {
                foreach (var template in templates)
                {
                    n++;
                    progress.Report($"Генерація {n} з {total}…");

                    if (!regenerateExisting
                        && existingFileNames.TryGetValue((recipient.Id, template.Id), out var existingFileName)
                        && ExistsInOutputFolder(outputFolder, existingFileName))
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        var values = BuildValues(mappingsByTemplate[template.Id], recipient, orgSettings, manualValues, courseOfficerSignature);
                        var intakeName = recipient.IntakeId is int id && intakeNames.TryGetValue(id, out var display)
                            ? display
                            : null;

                        var fileName = BuildFileName(recipient, template.Name, intakeName, runStamp, usedFileNames);
                        var outputPath = Path.Combine(outputFolder, fileName);
                        EnsureFolder(outputPath);

                        var result = _documentGenerationService.GenerateOne(template, template.Content, values, outputPath);
                        if (!result.Success)
                        {
                            errors++;
                            issues.Add(new RunIssue(RunIssue.PhaseDocx,
                                $"{recipient.LastName} {recipient.FirstName}", template.Name,
                                result.ErrorMessage ?? "невідома помилка"));
                            continue;
                        }

                        var bytes = File.ReadAllBytes(outputPath);
                        var pair = (recipient.Id, template.Id);
                        var version = maxVersions.GetValueOrDefault(pair) + 1;
                        maxVersions[pair] = version;

                        if (version > 1)
                        {
                            foreach (var old in db.GeneratedDocuments.IgnoreQueryFilters()
                                .Where(g => g.RecipientId == recipient.Id && g.TemplateId == template.Id && g.IsCurrent))
                            {
                                old.IsCurrent = false;
                            }
                        }

                        var doc = new GeneratedDocument
                        {
                            RecipientId = recipient.Id,
                            TemplateId = template.Id,
                            GeneratedAt = DateTime.Now,
                            GeneratedByUserId = _currentUserContext.CurrentUserId ?? 0,
                            FileName = fileName,
                            SizeBytes = bytes.LongLength,
                            ContentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
                            SourceHash = _documentHashService.ComputeSourceHash(
                                mappingsByTemplate[template.Id], recipient, orgSettings),
                            Version = version,
                            IsCurrent = true,
                            SourceType = Models.Enums.DocumentSourceType.Generated,
                            RunId = run.Id,
                            IntakeId = recipient.IntakeId,
                            OrgNodeIdSnapshot = recipient.OrgNodeId,
                            OrgPathSnapshot = BuildOrgPathSnapshot(recipient.OrgNodeId, orgNodeNames),
                            HasContent = true,
                            Content = new GeneratedDocumentContent { Content = bytes }
                        };
                        db.GeneratedDocuments.Add(doc);

                        existingFileNames[pair] = fileName;
                        generated++;

                        if (result.UnfilledTags.Count > 0)
                            issues.Add(new RunIssue(RunIssue.PhaseDocx,
                                $"{recipient.LastName} {recipient.FirstName}", template.Name,
                                $"не заповнено теги - {string.Join(", ", result.UnfilledTags)}", IsError: false));
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        issues.Add(new RunIssue(RunIssue.PhaseDocx,
                            $"{recipient.LastName} {recipient.FirstName}", template.Name, ex.Message));
                    }
                }
            }

            return new DocxPhaseResult(generated, skipped, errors, issues);
        }

        private sealed record XlsxPhaseResult(int Generated, int Skipped, int Errors, List<RunIssue> Issues);

        private XlsxPhaseResult RunXlsxPhase(
            AppDbContext db,
            List<GenerationPackageExportTemplate> exportLinks,
            List<Recipient> allRecipients,
            OrganizationSettings? orgSettings,
            GenerationPackageRun run,
            Dictionary<string, string> manualValues,
            string outputFolder,
            HashSet<string> usedFileNames,
            string runStamp,
            bool regenerateExisting,
            IProgress<string> progress,
            int? courseOfficerId)
        {
            var generated = 0;
            var skipped = 0;
            var errors = 0;
            var issues = new List<RunIssue>();

            var intakeNames = LoadIntakeNames(db);

            foreach (var link in exportLinks)
            {
                var template = link.ExportTemplate;
                if (template is null) continue;

                progress.Report($"Групова відомість «{template.Name}»…");

                var roster = RosterOrdering.Apply(
                        allRecipients.Where(r => FitnessCategoryHelper.Matches(link.FitnessFilter, r.FitnessCategory)))
                    .ToList();

                if (roster.Count == 0)
                {
                    skipped++;
                    issues.Add(new RunIssue(RunIssue.PhaseXlsx, string.Empty, template.Name,
                        "пропущено - немає людей за фільтром придатності", IsError: false));
                    continue;
                }

                try
                {
                    var mappings = db.ExportTemplateColumnMappings
                        .Where(m => m.ExportTemplateId == template.Id)
                        .OrderBy(m => m.ColumnIndex)
                        .ToList();

                    var rosterEntries = roster.Select(r => (r.Id, SourceHash: ComputeRecipientSourceHash(mappings, r, orgSettings))).ToList();
                    var rosterHash = _documentHashService.ComputeRosterHash(template.Id, rosterEntries);

                    var currents = db.GeneratedGroupDocuments
                        .Where(g => g.ExportTemplateId == template.Id && g.IntakeId == run.IntakeId && g.IsCurrent)
                        .ToList();
                    var current = currents.OrderByDescending(g => g.Version).FirstOrDefault();

                    if (!regenerateExisting && current is not null && current.RosterHash == rosterHash
                        && ExistsInOutputFolder(outputFolder, current.FileName))
                    {
                        skipped++;
                        continue;
                    }

                    var courseOfficerSignature = ResolveCourseOfficerSignature(db, courseOfficerId);

                    if (mappings.Any(m => m.FieldKey == nameof(ExportFieldKey.CourseOfficerSignature))
                        && string.IsNullOrEmpty(courseOfficerSignature))
                    {
                        errors++;
                        issues.Add(new RunIssue(RunIssue.PhaseXlsx, string.Empty, template.Name,
                            "немає курсового офіцера серед постійного складу - відомість не сформовано"));
                        continue;
                    }

                    var result = _xlsxGenerationService.Generate(
                        template.Content, template.TemplateRowIndex, template.UsesPlaceholders,
                        mappings, roster, orgSettings, manualValues,
                        template.RepeatSheetPerDate, courseOfficerSignature);

                    if (!result.Success)
                    {
                        errors++;
                        issues.Add(new RunIssue(RunIssue.PhaseXlsx, string.Empty, template.Name,
                            result.ErrorMessage ?? "невідома помилка"));
                        continue;
                    }

                    var fileName = BuildGroupFileName(
                        template.Name, IntakeNamesOf(roster, intakeNames), runStamp, usedFileNames);
                    var outputPath = Path.Combine(outputFolder, fileName);
                    EnsureFolder(outputPath);
                    File.WriteAllBytes(outputPath, result.Content!);

                    var maxVersion = db.GeneratedGroupDocuments.IgnoreQueryFilters()
                        .Where(g => g.ExportTemplateId == template.Id && g.IntakeId == run.IntakeId)
                        .Select(g => (int?)g.Version)
                        .Max() ?? 0;

                    foreach (var stale in currents) stale.IsCurrent = false;

                    var groupDoc = new GeneratedGroupDocument
                    {
                        ExportTemplateId = template.Id,
                        RunId = run.Id,
                        IntakeId = run.IntakeId,
                        GeneratedAt = DateTime.Now,
                        GeneratedByUserId = _currentUserContext.CurrentUserId ?? 0,
                        FileName = fileName,
                        SizeBytes = result.Content!.LongLength,
                        ContentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(result.Content)),
                        RosterHash = rosterHash,
                        RecipientCount = roster.Count,
                        Version = maxVersion + 1,
                        IsCurrent = true,
                        HasContent = true,
                        Content = new GeneratedGroupDocumentContent { Content = result.Content }
                    };
                    foreach (var person in roster)
                        groupDoc.Recipients.Add(new GeneratedGroupDocumentRecipient { RecipientId = person.Id });
                    db.GeneratedGroupDocuments.Add(groupDoc);

                    generated++;

                    if (result.UnfilledTags.Count > 0)
                        issues.Add(new RunIssue(RunIssue.PhaseXlsx, string.Empty, template.Name,
                            $"не заповнено теги - {string.Join(", ", result.UnfilledTags)}", IsError: false));
                }
                catch (Exception ex)
                {
                    errors++;
                    issues.Add(new RunIssue(RunIssue.PhaseXlsx, string.Empty, template.Name, ex.Message));
                }
            }

            return new XlsxPhaseResult(generated, skipped, errors, issues);

            string ComputeRecipientSourceHash(List<ExportTemplateColumnMapping> mappings, Recipient r, OrganizationSettings? org)
            {
                var auto = mappings.Where(m => m.SourceType != MappingSourceType.Manual);
                return string.Join("|", auto
                    .OrderBy(m => m.PlaceholderTag, StringComparer.Ordinal)
                    .Select(m => $"{m.PlaceholderTag}={ResolveHashField(m, r, org)}"));
            }
        }

        private static string ResolveHashField(ExportTemplateColumnMapping mapping, Recipient r, OrganizationSettings? org) => mapping.SourceType switch
        {
            MappingSourceType.Recipient => GetRecipientFieldValue(r, mapping.FieldKey, dateFormat: null),
            MappingSourceType.Organization => GetOrganizationFieldValue(org, mapping.FieldKey),
            _ => string.Empty
        };

        private sealed record DocxGroupPhaseResult(int Generated, int Skipped, int Errors, List<RunIssue> Issues);

        private DocxGroupPhaseResult RunDocxGroupPhase(
            AppDbContext db,
            List<Template> groupTemplates,
            List<Recipient> allRecipients,
            OrganizationSettings? orgSettings,
            GenerationPackageRun run,
            Dictionary<string, string> manualValues,
            string outputFolder,
            HashSet<string> usedFileNames,
            string runStamp,
            bool regenerateExisting,
            IProgress<string> progress)
        {
            var generated = 0;
            var skipped = 0;
            var errors = 0;
            var issues = new List<RunIssue>();

            var roster = RosterOrdering.Apply(allRecipients).ToList();

            var intakeNames = LoadIntakeNames(db);

            foreach (var template in groupTemplates)
            {
                progress.Report($"Груповий DOCX «{template.Name}»…");

                if (roster.Count == 0)
                {
                    skipped++;
                    issues.Add(new RunIssue(RunIssue.PhaseDocxGroup, string.Empty, template.Name,
                        "пропущено - немає людей за обраним складом", IsError: false));
                    continue;
                }

                try
                {
                    var mappings = db.TemplateFieldMappings.Where(m => m.TemplateId == template.Id).ToList();
                    var perRecipientMappings = mappings.Where(m => m.IsInsideRepeatingBlock).ToList();
                    var sharedMappings = mappings.Where(m => !m.IsInsideRepeatingBlock).ToList();

                    var perRecipientValues = roster
                        .Select(r => (IDictionary<string, string>)BuildValues(perRecipientMappings, r, orgSettings, manualValues))
                        .ToList();
                    var sharedValues = BuildValues(sharedMappings, roster[0], orgSettings, manualValues);

                    var rosterEntries = roster
                        .Select(r => (r.Id, SourceHash: _documentHashService.ComputeSourceHash(perRecipientMappings, r, orgSettings)))
                        .ToList();
                    var rosterHash = _documentHashService.ComputeRosterHash(template.Id, rosterEntries);

                    var currents = db.GeneratedGroupDocuments
                        .Where(g => g.TemplateId == template.Id && g.IntakeId == run.IntakeId && g.IsCurrent)
                        .ToList();
                    var current = currents.OrderByDescending(g => g.Version).FirstOrDefault();

                    if (!regenerateExisting && current is not null && current.RosterHash == rosterHash
                        && ExistsInOutputFolder(outputFolder, current.FileName))
                    {
                        skipped++;
                        continue;
                    }

                    var fileName = BuildGroupFileName(
                        template.Name, IntakeNamesOf(roster, intakeNames), runStamp, usedFileNames, ".docx");
                    var outputPath = Path.Combine(outputFolder, fileName);
                    EnsureFolder(outputPath);

                    var result = _documentGenerationService.GenerateGroup(
                        template, template.Content, perRecipientValues, sharedValues, outputPath);

                    if (!result.Success)
                    {
                        errors++;
                        issues.Add(new RunIssue(RunIssue.PhaseDocxGroup, string.Empty, template.Name,
                            result.ErrorMessage ?? "невідома помилка"));
                        continue;
                    }

                    var bytes = File.ReadAllBytes(outputPath);
                    var maxVersion = db.GeneratedGroupDocuments.IgnoreQueryFilters()
                        .Where(g => g.TemplateId == template.Id && g.IntakeId == run.IntakeId)
                        .Select(g => (int?)g.Version).Max() ?? 0;

                    foreach (var stale in currents) stale.IsCurrent = false;

                    var groupDoc = new GeneratedGroupDocument
                    {
                        TemplateId = template.Id,
                        ExportTemplateId = null,
                        RunId = run.Id,
                        IntakeId = run.IntakeId,
                        GeneratedAt = DateTime.Now,
                        GeneratedByUserId = _currentUserContext.CurrentUserId ?? 0,
                        FileName = fileName,
                        SizeBytes = bytes.LongLength,
                        ContentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
                        RosterHash = rosterHash,
                        RecipientCount = roster.Count,
                        Version = maxVersion + 1,
                        IsCurrent = true,
                        HasContent = true,
                        Content = new GeneratedGroupDocumentContent { Content = bytes }
                    };
                    foreach (var person in roster)
                        groupDoc.Recipients.Add(new GeneratedGroupDocumentRecipient { RecipientId = person.Id });
                    db.GeneratedGroupDocuments.Add(groupDoc);

                    generated++;

                    if (result.UnfilledTags.Count > 0)
                        issues.Add(new RunIssue(RunIssue.PhaseDocxGroup, string.Empty, template.Name,
                            $"не заповнено теги - {string.Join(", ", result.UnfilledTags)}", IsError: false));
                }
                catch (Exception ex)
                {
                    errors++;
                    issues.Add(new RunIssue(RunIssue.PhaseDocxGroup, string.Empty, template.Name, ex.Message));
                }
            }

            return new DocxGroupPhaseResult(generated, skipped, errors, issues);
        }

        private static string? BuildOrgPathSnapshot(int? orgNodeId, Dictionary<int, (string Name, int? ParentId)> nodes)
        {
            if (orgNodeId is not int id || !nodes.ContainsKey(id)) return null;

            var names = new List<string>();
            int? current = id;
            while (current is int cid && nodes.TryGetValue(cid, out var node))
            {
                names.Insert(0, node.Name);
                current = node.ParentId;
            }
            return string.Join(" / ", names);
        }

        private static string? ResolveCourseOfficerSignature(AppDbContext db, int? courseOfficerId)
            => courseOfficerId is int chosenId
                ? Services.CourseOfficerSignature.BuildFor(db, chosenId)
                : Services.CourseOfficerSignature.Build(db);

        internal static string? CourseOfficerSignatureFor(
            AppDbContext db, IEnumerable<TemplateFieldMapping> mappings, int? courseOfficerId = null)
            => mappings.Any(m => m.SourceType == MappingSourceType.Recipient
                                 && m.FieldName == nameof(ExportFieldKey.CourseOfficerSignature))
                ? ResolveCourseOfficerSignature(db, courseOfficerId)
                : null;

        internal static Dictionary<string, string> BuildValues(
            List<TemplateFieldMapping> mappings, Recipient recipient, OrganizationSettings? org,
            Dictionary<string, string> manualValues, string? courseOfficerSignature = null)
        {
            var values = new Dictionary<string, string>();

            var gradeSlots = mappings
                .Where(m => m.SourceType == MappingSourceType.Recipient && m.FieldName == nameof(ExportFieldKey.GradeRandom34))
                .Select(m => GradeSlot(m.PlaceholderTag))
                .Distinct()
                .ToList();

            foreach (var mapping in mappings)
            {
                values[mapping.PlaceholderTag] = mapping.SourceType switch
                {
                    MappingSourceType.Recipient => ResolveRecipientValue(mapping, recipient, manualValues, courseOfficerSignature, gradeSlots),
                    MappingSourceType.Organization => GetOrganizationFieldValue(org, mapping.FieldName),
                    MappingSourceType.Manual => manualValues.TryGetValue(mapping.PlaceholderTag, out var manualValue) ? manualValue : string.Empty,
                    _ => string.Empty
                };
            }

            return values;
        }

        private static string ResolveRecipientValue(
            TemplateFieldMapping mapping, Recipient recipient, Dictionary<string, string> manualValues,
            string? courseOfficerSignature, IReadOnlyList<int> gradeSlots) => mapping.FieldName switch
        {
            nameof(ExportFieldKey.CourseOfficerSignature) => courseOfficerSignature ?? string.Empty,
            nameof(ExportFieldKey.RowNumber) => manualValues.TryGetValue(mapping.PlaceholderTag, out var rowNumber) ? rowNumber : string.Empty,
            nameof(ExportFieldKey.GradeRandom34) => XlsxGenerationService
                .ComputeGradeRandom34(recipient.Id, GradeSlot(mapping.PlaceholderTag))
                .ToString(CultureInfo.InvariantCulture),
            nameof(ExportFieldKey.GradeOverall34) => XlsxGenerationService
                .ComputeGradeOverall34(recipient.Id, gradeSlots)?
                .ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            _ => GetRecipientFieldValue(recipient, mapping.FieldName, mapping.DateFormat)
        };

        internal static int GradeSlot(string placeholderTag)
        {
            var inner = placeholderTag.Trim('{', '}').Trim();
            var underscore = inner.LastIndexOf('_');
            return underscore >= 0
                   && int.TryParse(inner[(underscore + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var slot)
                ? slot
                : 0;
        }

        private static string GetRecipientFieldValue(Recipient r, string? fieldName, string? dateFormat) => fieldName switch
        {
            "FullNameFormatted" => FormatFullName(r),
            "LastName" => r.LastName,
            "FirstName" => r.FirstName,
            "MiddleName" => r.MiddleName ?? string.Empty,
            "Rank" => r.Rank,
            "Position" => r.Position,
            "UnitName" => r.Unit?.Name ?? string.Empty,
            "ServiceNumber" => r.ServiceNumber,
            "DateOfBirth" => r.DateOfBirth is { } dob ? DateFormatCatalog.Format(dob, dateFormat, DateFormatCatalog.DdMmYyyy) : string.Empty,
            "Nationality" => r.Nationality ?? string.Empty,
            "Vos" => r.Vos ?? string.Empty,
            "CourseArrivalDate" => r.CourseArrivalDate is { } cad ? DateFormatCatalog.Format(cad, dateFormat, DateFormatCatalog.DdMmYyyy) : string.Empty,
            "MaritalStatus" => r.MaritalStatus ?? string.Empty,
            "RegistrationAddress" => r.RegistrationAddress ?? string.Empty,
            "ResidenceAddress" => r.ResidenceAddress ?? string.Empty,
            "Phone" => r.Phone ?? string.Empty,
            "Note" => r.Note ?? string.Empty,
            "GroupName" => r.GroupName ?? string.Empty,
            "NameTransliterated" => r.NameTransliterated ?? string.Empty,
            "ServedBefore" => r.ServedBefore ?? string.Empty,
            "ExtraNote" => r.ExtraNote ?? string.Empty,
            "CommanderContact" => r.CommanderContact ?? string.Empty,
            "TravelCertificateNumber" => r.TravelCertificateNumber ?? string.Empty,
            "TravelCertificateDate" => r.TravelCertificateDate is { } tcd ? DateFormatCatalog.Format(tcd, dateFormat, DateFormatCatalog.Long) : string.Empty,
            "FoodCertificate" => r.FoodCertificate ?? string.Empty,
            "IdDocumentNumber" => r.IdDocumentNumber ?? string.Empty,
            "MedicalBoard" => r.MedicalBoard ?? string.Empty,
            "MedicalBoardConclusion" => r.MedicalBoardConclusion ?? string.Empty,
            "OriginUnit" => r.OriginUnit ?? string.Empty,
            "Vehicle" => r.Vehicle ?? string.Empty,
            "IsCourseOfficer" => r.IsCourseOfficer ? "Так" : "Ні",
            "WeaponName" => FirstWeapon(r)?.Name ?? string.Empty,
            "WeaponSerialNumber" => FirstWeapon(r)?.SerialNumber ?? string.Empty,
            "WeaponFull" => FirstWeapon(r) is { } w ? $"{w.Name} № {w.SerialNumber}".Trim() : string.Empty,
            "RoomDisplay" => FormatRoom(r.Room),
            "ShortName" => Services.NameFormatter.ShortName(r.LastName, r.FirstName, r.MiddleName),
            "FitnessCategory" => r.FitnessCategory ?? string.Empty,
            "RankAccusative" => r.RankAccusative is { Length: > 0 }
                ? r.RankAccusative
                : Services.UkrainianGrammar.Accusative(r.Rank, Models.Enums.GrammaticalKind.Rank, Services.UkrainianGrammar.Detect(r)),
            "FullNameAccusative" => r.FullNameAccusative is { Length: > 0 }
                ? r.FullNameAccusative
                : FormatFullNameAccusative(r),
            "ArrivedVerb" => Services.UkrainianGrammar.ArrivedVerb(Services.UkrainianGrammar.Detect(r)),
            "SuchPronoun" => Services.UkrainianGrammar.SuchPronoun(Services.UkrainianGrammar.Detect(r)),
            _ => string.Empty
        };

        private static Weapon? FirstWeapon(Recipient r) => r.Weapons.OrderBy(w => w.Id).FirstOrDefault();

        private static string FormatFullNameAccusative(Recipient r)
        {
            var gender = Services.UkrainianGrammar.Detect(r);
            var lastName = Services.UkrainianGrammar.Accusative(r.LastName, Models.Enums.GrammaticalKind.Surname, gender)
                .ToUpper(new CultureInfo("uk-UA"));
            var firstName = Services.UkrainianGrammar.Accusative(r.FirstName, Models.Enums.GrammaticalKind.GivenName, gender);
            var middleName = string.IsNullOrWhiteSpace(r.MiddleName)
                ? null
                : Services.UkrainianGrammar.Accusative(r.MiddleName, Models.Enums.GrammaticalKind.Patronymic, gender);
            return string.Join(' ', new[] { lastName, firstName, middleName }.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        private static string GetOrganizationFieldValue(OrganizationSettings? org, string? fieldName)
        {
            if (org is null) return string.Empty;

            return fieldName switch
            {
                "UnitNumber" => org.UnitNumber,
                "City" => org.City,
                "CommanderRank" => org.CommanderRank,
                "CommanderFullName" => org.CommanderFullName,
                "HrOfficerFullName" => org.HrOfficerFullName,
                "CommanderPosition" => org.CommanderPosition,
                "UnitFullName" => org.UnitFullName,
                _ => string.Empty
            };
        }

        private static string FormatFullName(Recipient r)
        {
            var lastName = (r.LastName ?? string.Empty).ToUpper(new CultureInfo("uk-UA"));
            return string.Join(' ', new[] { lastName, r.FirstName, r.MiddleName }.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        private static string FormatRoom(Room? room)
        {
            if (room is null || string.IsNullOrWhiteSpace(room.Number)) return string.Empty;
            return string.IsNullOrWhiteSpace(room.Building) ? room.Number : $"{room.Building} {room.Number}";
        }

        internal static bool ExistsInOutputFolder(string outputFolder, string? fileName)
            => !string.IsNullOrWhiteSpace(fileName)
               && File.Exists(Path.Combine(outputFolder, fileName));

        internal static string ResolveRunStamp(string outputFolder)
        {
            var now = DateTime.Now;
            var dateFolder = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            var alreadyUsed = Directory.Exists(outputFolder)
                && (Directory.EnumerateDirectories(outputFolder, dateFolder, SearchOption.AllDirectories).Any()
                    || Directory.EnumerateFiles(outputFolder, dateFolder + ".*", SearchOption.AllDirectories).Any());

            return DocumentFolderLayout.RunStamp(now, alreadyUsed);
        }

        private static Dictionary<int, string> LoadIntakeNames(AppDbContext db)
            => db.Intakes.IgnoreQueryFilters()
                .Select(i => new { i.Id, i.DisplayNumber })
                .AsEnumerable()
                .ToDictionary(i => i.Id, i => i.DisplayNumber);

        private static IEnumerable<string?> IntakeNamesOf(
            IEnumerable<Recipient> roster, Dictionary<int, string> intakeNames)
            => roster.Select(r => r.IntakeId is int id && intakeNames.TryGetValue(id, out var name)
                ? name
                : null);

        private static void EnsureFolder(string outputPath)
        {
            var folder = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        }

        private static string BuildFileName(
            Recipient recipient, string templateName, string? intakeName, string runStamp,
            HashSet<string> usedFileNames)
        {
            var placement = DocumentFolderLayout.ForPerson(
                intakeName, TemplateNaming.Clean(templateName), runStamp,
                $"{recipient.LastName} {recipient.FirstName}", recipient.ServiceNumber);

            return MakeUnique(placement, usedFileNames, ".docx");
        }

        private static string BuildGroupFileName(
            string templateName, IEnumerable<string?> memberIntakeNames, string runStamp,
            HashSet<string> usedFileNames, string extension = ".xlsx")
        {
            var placement = DocumentFolderLayout.ForGroup(
                memberIntakeNames, TemplateNaming.Clean(templateName), runStamp);

            return MakeUnique(placement, usedFileNames, extension);
        }

        private static string MakeUnique(
            DocumentPlacement placement, HashSet<string> usedFileNames, string extension)
        {
            var relative = placement.RelativePath(extension);
            var suffix = 2;

            while (!usedFileNames.Add(relative))
            {
                relative = placement.RelativePath($" ({suffix}){extension}");
                suffix++;
            }

            return relative;
        }
    }
}
