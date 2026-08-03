using System.Globalization;
using System.IO;
using GenDoc.Data;
using GenDoc.Models;
using GenDoc.Models.Enums;
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

        public List<(int Id, string Name)> GetAllTemplates()
        {
            using var db = _dbFactory.CreateDbContext();
            return db.Templates
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

            var seen = new HashSet<string>();
            var tags = new List<string>();

            foreach (var templateId in templateIds)
            {
                var manualTags = db.TemplateFieldMappings
                    .Where(m => m.TemplateId == templateId && m.SourceType == MappingSourceType.Manual)
                    .OrderBy(m => m.PlaceholderTag)
                    .Select(m => m.PlaceholderTag)
                    .ToList();

                foreach (var tag in manualTags)
                {
                    if (seen.Add(tag)) tags.Add(tag);
                }
            }

            var exportTemplateIds = db.GenerationPackageExportTemplates
                .Where(pt => pt.GenerationPackageId == packageId)
                .OrderBy(pt => pt.SortOrder)
                .Select(pt => pt.ExportTemplateId)
                .ToList();

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

        public int GetRecipientCount()
        {
            using var db = _dbFactory.CreateDbContext();
            return db.Recipients.Count();
        }

        public int GetRecipientCount(FitnessFilter filter)
        {
            if (filter == FitnessFilter.All) return GetRecipientCount();

            using var db = _dbFactory.CreateDbContext();
            return db.Recipients.Select(r => r.FitnessCategory).AsEnumerable()
                .Count(f => FitnessCategoryHelper.Matches(filter, f));
        }

        public RunResult RunPackage(
            int packageId,
            string outputFolder,
            Dictionary<string, string> manualValues,
            bool regenerateExisting,
            IProgress<string> progress)
        {
            using var db = _dbFactory.CreateDbContext();

            var package = db.GenerationPackages.First(p => p.Id == packageId);

            var templates = db.GenerationPackageTemplates
                .Where(pt => pt.GenerationPackageId == packageId)
                .OrderBy(pt => pt.SortOrder)
                .Select(pt => pt.Template!)
                .ToList();

            var exportLinks = db.GenerationPackageExportTemplates
                .Where(pt => pt.GenerationPackageId == packageId)
                .OrderBy(pt => pt.SortOrder)
                .Include(pt => pt.ExportTemplate)
                .ToList();

            var recipients = db.Recipients.Include(r => r.Unit).Include(r => r.Room).Include(r => r.OrgNode).ToList();
            var orgSettings = db.OrganizationSettings.FirstOrDefault();

            var run = new GenerationPackageRun
            {
                GenerationPackageId = packageId,
                RunAt = DateTime.Now,
                RunByUserId = _currentUserContext.CurrentUserId ?? 0
            };
            db.GenerationPackageRuns.Add(run);
            db.SaveChanges();

            Directory.CreateDirectory(outputFolder);
            var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var docx = RunDocxPhase(db, templates, recipients, orgSettings, run, manualValues, outputFolder, usedFileNames, regenerateExisting, progress);
            var xlsx = RunXlsxPhase(db, exportLinks, recipients, orgSettings, run, manualValues, outputFolder, usedFileNames, regenerateExisting, progress);

            run.GeneratedCount = docx.Generated;
            run.SkippedCount = docx.Skipped;
            run.ErrorCount = docx.Errors;

            var summaryLines = new List<string>(docx.ErrorMessages);
            summaryLines.AddRange(xlsx.SummaryLines);
            run.Summary = summaryLines.Count == 0 ? null : string.Join("\n", summaryLines);

            _auditLogService.LogGenerate(db, "GenerationPackage", packageId,
                $"{package.Name}: docx — згенеровано {docx.Generated}, пропущено {docx.Skipped}, помилок {docx.Errors}; " +
                $"xlsx — згенеровано {xlsx.Generated}, пропущено {xlsx.Skipped}, помилок {xlsx.Errors}");

            db.SaveChanges();

            return new RunResult(docx.Generated, docx.Skipped, docx.Errors, xlsx.Generated, xlsx.Skipped, xlsx.Errors);
        }

        private sealed record DocxPhaseResult(int Generated, int Skipped, int Errors, List<string> ErrorMessages);

        // Phase A — по одному документу на людину. Чистий перенос попередньої логіки RunPackage,
        // без змін поведінки: anti-дубль за (RecipientId, TemplateId), версійність, SourceHash.
        private DocxPhaseResult RunDocxPhase(
            AppDbContext db,
            List<Template> templates,
            List<Recipient> recipients,
            OrganizationSettings? orgSettings,
            GenerationPackageRun run,
            Dictionary<string, string> manualValues,
            string outputFolder,
            HashSet<string> usedFileNames,
            bool regenerateExisting,
            IProgress<string> progress)
        {
            var mappingsByTemplate = templates.ToDictionary(
                t => t.Id,
                t => db.TemplateFieldMappings.Where(m => m.TemplateId == t.Id).ToList());

            // Anti-дубль: лише актуальні живі документи (видалені відсікає query filter).
            var existingPairs = new HashSet<(int RecipientId, int TemplateId)>(
                db.GeneratedDocuments
                    .Where(g => g.IsCurrent)
                    .Select(g => new { g.RecipientId, g.TemplateId })
                    .AsEnumerable()
                    .Select(g => (g.RecipientId, g.TemplateId)));

            var maxVersions = db.GeneratedDocuments.IgnoreQueryFilters()
                .GroupBy(g => new { g.RecipientId, g.TemplateId })
                .Select(g => new { g.Key.RecipientId, g.Key.TemplateId, MaxVersion = g.Max(x => x.Version) })
                .AsEnumerable()
                .ToDictionary(g => (g.RecipientId, g.TemplateId), g => g.MaxVersion);

            var orgNodeNames = db.OrgNodes.IgnoreQueryFilters()
                .Select(o => new { o.Id, o.Name, o.ParentId })
                .AsEnumerable()
                .ToDictionary(o => o.Id, o => (o.Name, o.ParentId));

            var generated = 0;
            var skipped = 0;
            var errors = 0;
            var errorMessages = new List<string>();

            var total = recipients.Count * templates.Count;
            var n = 0;

            foreach (var recipient in recipients)
            {
                foreach (var template in templates)
                {
                    n++;
                    progress.Report($"Генерація {n} з {total}…");

                    if (!regenerateExisting && existingPairs.Contains((recipient.Id, template.Id)))
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        var values = BuildValues(mappingsByTemplate[template.Id], recipient, orgSettings, manualValues);
                        var fileName = BuildFileName(recipient, template.Name, usedFileNames);
                        var outputPath = Path.Combine(outputFolder, fileName);

                        var result = _documentGenerationService.GenerateOne(template, template.Content, values, outputPath);
                        if (!result.Success)
                        {
                            errors++;
                            errorMessages.Add($"{recipient.FullName} / {template.Name}: {result.ErrorMessage}");
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

                        existingPairs.Add(pair);
                        generated++;
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        errorMessages.Add($"{recipient.FullName} / {template.Name}: {ex.Message}");
                    }
                }
            }

            return new DocxPhaseResult(generated, skipped, errors, errorMessages);
        }

        private sealed record XlsxPhaseResult(int Generated, int Skipped, int Errors, List<string> SummaryLines);

        // Phase B — один документ на весь список людей (форма-відомість). Немає єдиного
        // Recipient, тому anti-дубль тримається на RosterHash складу, а не на парі (Recipient, Template).
        private XlsxPhaseResult RunXlsxPhase(
            AppDbContext db,
            List<GenerationPackageExportTemplate> exportLinks,
            List<Recipient> allRecipients,
            OrganizationSettings? orgSettings,
            GenerationPackageRun run,
            Dictionary<string, string> manualValues,
            string outputFolder,
            HashSet<string> usedFileNames,
            bool regenerateExisting,
            IProgress<string> progress)
        {
            var generated = 0;
            var skipped = 0;
            var errors = 0;
            var summaryLines = new List<string>();

            foreach (var link in exportLinks)
            {
                var template = link.ExportTemplate;
                if (template is null) continue;

                progress.Report($"Групова відомість «{template.Name}»…");

                // Сортування — так само, як за замовчуванням на екрані «Особовий склад».
                var roster = allRecipients
                    .Where(r => FitnessCategoryHelper.Matches(link.FitnessFilter, r.FitnessCategory))
                    .OrderBy(r => r.LastName, StringComparer.Ordinal)
                    .ThenBy(r => r.FirstName, StringComparer.Ordinal)
                    .ToList();

                if (roster.Count == 0)
                {
                    skipped++;
                    summaryLines.Add($"ГРУПА: {template.Name}: пропущено — немає людей за фільтром придатності");
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

                    var current = db.GeneratedGroupDocuments
                        .FirstOrDefault(g => g.ExportTemplateId == template.Id && g.IntakeId == null && g.IsCurrent);

                    if (!regenerateExisting && current is not null && current.RosterHash == rosterHash)
                    {
                        skipped++;
                        continue;
                    }

                    var result = _xlsxGenerationService.Generate(
                        template.Content, template.TemplateRowIndex, template.UsesPlaceholders,
                        mappings, roster, orgSettings, manualValues);

                    if (!result.Success)
                    {
                        errors++;
                        summaryLines.Add($"ГРУПА: {template.Name}: {result.ErrorMessage}");
                        continue;
                    }

                    var fileName = BuildGroupFileName(template.Name, usedFileNames);
                    var outputPath = Path.Combine(outputFolder, fileName);
                    File.WriteAllBytes(outputPath, result.Content!);

                    var maxVersion = db.GeneratedGroupDocuments.IgnoreQueryFilters()
                        .Where(g => g.ExportTemplateId == template.Id && g.IntakeId == null)
                        .Select(g => (int?)g.Version)
                        .Max() ?? 0;

                    if (current is not null) current.IsCurrent = false;

                    db.GeneratedGroupDocuments.Add(new GeneratedGroupDocument
                    {
                        ExportTemplateId = template.Id,
                        RunId = run.Id,
                        IntakeId = null,
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
                    });

                    generated++;

                    if (result.UnfilledTags.Count > 0)
                        summaryLines.Add($"ГРУПА: {template.Name}: не заповнено теги — {string.Join(", ", result.UnfilledTags)}");
                }
                catch (Exception ex)
                {
                    errors++;
                    summaryLines.Add($"ГРУПА: {template.Name}: {ex.Message}");
                }
            }

            return new XlsxPhaseResult(generated, skipped, errors, summaryLines);

            string ComputeRecipientSourceHash(List<ExportTemplateColumnMapping> mappings, Recipient r, OrganizationSettings? org)
            {
                // Той самий підхід, що ComputeSourceHash для docx: тільки автоматичні
                // (не Manual) значення визначають, чи "застаріла" людина у відомості.
                var auto = mappings.Where(m => m.SourceType != MappingSourceType.Manual);
                return string.Join("|", auto
                    .OrderBy(m => m.PlaceholderTag, StringComparer.Ordinal)
                    .Select(m => $"{m.PlaceholderTag}={ResolveHashField(m, r, org)}"));
            }
        }

        private static string ResolveHashField(ExportTemplateColumnMapping mapping, Recipient r, OrganizationSettings? org) => mapping.SourceType switch
        {
            MappingSourceType.Recipient => GetRecipientFieldValue(r, mapping.FieldKey),
            MappingSourceType.Organization => GetOrganizationFieldValue(org, mapping.FieldKey),
            _ => string.Empty
        };

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

        internal static Dictionary<string, string> BuildValues(
            List<TemplateFieldMapping> mappings, Recipient recipient, OrganizationSettings? org, Dictionary<string, string> manualValues)
        {
            var values = new Dictionary<string, string>();

            foreach (var mapping in mappings)
            {
                values[mapping.PlaceholderTag] = mapping.SourceType switch
                {
                    MappingSourceType.Recipient => GetRecipientFieldValue(recipient, mapping.FieldName),
                    MappingSourceType.Organization => GetOrganizationFieldValue(org, mapping.FieldName),
                    MappingSourceType.Manual => manualValues.TryGetValue(mapping.PlaceholderTag, out var manualValue) ? manualValue : string.Empty,
                    _ => string.Empty
                };
            }

            return values;
        }

        private static string GetRecipientFieldValue(Recipient r, string? fieldName) => fieldName switch
        {
            "FullNameFormatted" => FormatFullName(r),
            "LastName" => r.LastName,
            "FirstName" => r.FirstName,
            "MiddleName" => r.MiddleName ?? string.Empty,
            "Rank" => r.Rank,
            "Position" => r.Position,
            "UnitName" => r.Unit?.Name ?? string.Empty,
            "ServiceNumber" => r.ServiceNumber,
            "DateOfBirth" => r.DateOfBirth?.ToString("dd.MM.yyyy") ?? string.Empty,
            "Nationality" => r.Nationality ?? string.Empty,
            "Vos" => r.Vos ?? string.Empty,
            "CourseArrivalDate" => r.CourseArrivalDate?.ToString("dd.MM.yyyy") ?? string.Empty,
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
            "FoodCertificate" => r.FoodCertificate ?? string.Empty,
            "IdDocumentNumber" => r.IdDocumentNumber ?? string.Empty,
            "MedicalBoard" => r.MedicalBoard ?? string.Empty,
            "MedicalBoardConclusion" => r.MedicalBoardConclusion ?? string.Empty,
            "OriginUnit" => r.OriginUnit ?? string.Empty,
            "Vehicle" => r.Vehicle ?? string.Empty,
            "RoomDisplay" => FormatRoom(r.Room),
            "ShortName" => Services.NameFormatter.ShortName(r.LastName, r.FirstName, r.MiddleName),
            "FitnessCategory" => r.FitnessCategory ?? string.Empty,
            "RowNumber" => string.Empty,
            _ => string.Empty
        };

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

        private static string BuildFileName(Recipient recipient, string templateName, HashSet<string> usedFileNames)
        {
            var baseName = $"{recipient.LastName}_{recipient.FirstName}_{templateName}";
            foreach (var invalidChar in Path.GetInvalidFileNameChars())
                baseName = baseName.Replace(invalidChar, '_');

            var fileName = baseName + ".docx";
            var suffix = 2;
            while (!usedFileNames.Add(fileName))
            {
                fileName = $"{baseName}_{suffix}.docx";
                suffix++;
            }

            return fileName;
        }

        private static string BuildGroupFileName(string templateName, HashSet<string> usedFileNames)
        {
            var baseName = templateName;
            foreach (var invalidChar in Path.GetInvalidFileNameChars())
                baseName = baseName.Replace(invalidChar, '_');

            var fileName = baseName + ".xlsx";
            var suffix = 2;
            while (!usedFileNames.Add(fileName))
            {
                fileName = $"{baseName}_{suffix}.xlsx";
                suffix++;
            }

            return fileName;
        }
    }
}
