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
        private readonly IAuditLogService _auditLogService;
        private readonly ICurrentUserContext _currentUserContext;
        private readonly Documents.IDocumentHashService _documentHashService;

        public GenerationService(
            IDbContextFactory<AppDbContext> dbFactory,
            IDocumentGenerationService documentGenerationService,
            IAuditLogService auditLogService,
            ICurrentUserContext currentUserContext,
            Documents.IDocumentHashService documentHashService)
        {
            _dbFactory = dbFactory;
            _documentGenerationService = documentGenerationService;
            _auditLogService = auditLogService;
            _currentUserContext = currentUserContext;
            _documentHashService = documentHashService;
        }

        public List<(int Id, string Name, string? Description, int TemplateCount)> GetPackages()
        {
            using var db = _dbFactory.CreateDbContext();
            return db.GenerationPackages
                .OrderBy(p => p.Name)
                .Select(p => new { p.Id, p.Name, p.Description, TemplateCount = p.Templates.Count })
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

        public void CreatePackage(string name, string? description, List<int> templateIds)
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

            db.GenerationPackages.Add(package);
            db.SaveChanges();

            _auditLogService.LogCreate(db, "GenerationPackage", package.Id, package.Name, $"Шаблонів: {templateIds.Count}");
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

            return tags;
        }

        public int GetRecipientCount()
        {
            using var db = _dbFactory.CreateDbContext();
            return db.Recipients.Count();
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

            var mappingsByTemplate = templates.ToDictionary(
                t => t.Id,
                t => db.TemplateFieldMappings.Where(m => m.TemplateId == t.Id).ToList());

            var recipients = db.Recipients.Include(r => r.Unit).Include(r => r.Room).Include(r => r.OrgNode).ToList();
            var orgSettings = db.OrganizationSettings.FirstOrDefault();

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

            run.GeneratedCount = generated;
            run.SkippedCount = skipped;
            run.ErrorCount = errors;
            run.Summary = errorMessages.Count == 0 ? null : string.Join("\n", errorMessages);

            _auditLogService.LogGenerate(db, "GenerationPackage", packageId,
                $"{package.Name}: згенеровано {generated}, пропущено {skipped}, помилок {errors}");

            db.SaveChanges();

            return new RunResult(generated, skipped, errors);
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
    }
}
