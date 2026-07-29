using System.IO;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using GenDoc.Data;
using GenDoc.Models;
using GenDoc.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services.Templates
{
    public class TemplateService : ITemplateService
    {
        private static readonly Regex PlaceholderRegex = new(@"\{\{[^{}]+\}\}", RegexOptions.Compiled);

        private static readonly Dictionary<string, string> RecipientTagMap = new()
        {
            ["піб"] = "FullNameFormatted",
            ["звання"] = "Rank",
            ["посада"] = "Position",
            ["підрозділ"] = "UnitName",
            ["особовий_номер"] = "ServiceNumber",
            ["дата_народження"] = "DateOfBirth",
            ["національність"] = "Nationality",
            ["вос"] = "Vos",
            ["сімейний_стан"] = "MaritalStatus",
            ["адреса_реєстрації"] = "RegistrationAddress",
            ["адреса_проживання"] = "ResidenceAddress",
            ["телефон"] = "Phone",
            ["примітка"] = "Note",
            ["група"] = "GroupName",
            ["піб_іноземною"] = "NameTransliterated",
            ["служив"] = "ServedBefore",
            ["автомобіль"] = "Vehicle"
        };

        private static readonly Dictionary<string, string> OrganizationTagMap = new()
        {
            ["номер_вч"] = "UnitNumber",
            ["місто"] = "City",
            ["звання_командира"] = "CommanderRank",
            ["піб_командира"] = "CommanderFullName",
            ["піб_кадровика"] = "HrOfficerFullName"
        };

        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly IAuditLogService _auditLogService;
        private readonly ICurrentUserContext _currentUserContext;

        public TemplateService(
            IDbContextFactory<AppDbContext> dbFactory,
            IAuditLogService auditLogService,
            ICurrentUserContext currentUserContext)
        {
            _dbFactory = dbFactory;
            _auditLogService = auditLogService;
            _currentUserContext = currentUserContext;
        }

        public UploadResult Upload(string filePath)
        {
            byte[] content;
            List<string> tags;

            try
            {
                content = File.ReadAllBytes(filePath);
                using var stream = new MemoryStream(content);
                using var doc = WordprocessingDocument.Open(stream, false);
                tags = ScanPlaceholders(doc);
            }
            catch
            {
                return new UploadResult(false,
                    "Файл не є документом Word (.docx). Якщо це текстова чернетка — відкрийте її у Word і збережіть як .docx.");
            }

            using var db = _dbFactory.CreateDbContext();

            var template = new Template
            {
                Name = Path.GetFileNameWithoutExtension(filePath),
                OriginalFileName = Path.GetFileName(filePath),
                Content = content,
                UploadedAt = DateTime.Now
            };

            foreach (var tag in tags)
            {
                var (sourceType, fieldName) = ClassifyTag(tag);
                template.FieldMappings.Add(new TemplateFieldMapping
                {
                    PlaceholderTag = tag,
                    SourceType = sourceType,
                    FieldName = fieldName
                });
            }

            db.Templates.Add(template);
            db.SaveChanges();

            _auditLogService.LogCreate(db, "Template", template.Id, template.Name,
                $"Завантажено файл {template.OriginalFileName}, міток: {tags.Count}");
            db.SaveChanges();

            return new UploadResult(true, null);
        }

        public List<(int Id, string Name, string? ShortName, string OriginalFileName, DateTime UploadedAt, int TagCount)> GetTemplateListItems()
        {
            using var db = _dbFactory.CreateDbContext();
            return db.Templates
                .OrderBy(t => t.Name)
                .Select(t => new { t.Id, t.Name, t.ShortName, t.OriginalFileName, t.UploadedAt, TagCount = t.FieldMappings.Count })
                .AsEnumerable()
                .Select(t => (t.Id, t.Name, t.ShortName, t.OriginalFileName, t.UploadedAt, t.TagCount))
                .ToList();
        }

        public void SaveShortName(int templateId, string? shortName)
        {
            using var db = _dbFactory.CreateDbContext();
            var template = db.Templates.First(t => t.Id == templateId);
            template.ShortName = string.IsNullOrWhiteSpace(shortName) ? null : shortName.Trim();
            db.SaveChanges();
        }

        public List<(int Id, string PlaceholderTag, MappingSourceType SourceType, string? FieldName)> GetMappings(int templateId)
        {
            using var db = _dbFactory.CreateDbContext();
            return db.TemplateFieldMappings
                .Where(m => m.TemplateId == templateId)
                .OrderBy(m => m.PlaceholderTag)
                .Select(m => new { m.Id, m.PlaceholderTag, m.SourceType, m.FieldName })
                .AsEnumerable()
                .Select(m => (m.Id, m.PlaceholderTag, m.SourceType, m.FieldName))
                .ToList();
        }

        public void SaveMappings(int templateId, List<(int Id, MappingSourceType SourceType, string? FieldName)> mappings)
        {
            using var db = _dbFactory.CreateDbContext();
            var existing = db.TemplateFieldMappings.Where(m => m.TemplateId == templateId).ToList();

            var oldSnapshot = string.Join(", ", existing.OrderBy(m => m.PlaceholderTag).Select(m => $"{m.PlaceholderTag}:{m.SourceType}/{m.FieldName}"));

            foreach (var (id, sourceType, fieldName) in mappings)
            {
                var mapping = existing.FirstOrDefault(m => m.Id == id);
                if (mapping is null) continue;

                mapping.SourceType = sourceType;
                mapping.FieldName = sourceType == MappingSourceType.Manual ? null : fieldName;
            }

            var newSnapshot = string.Join(", ", existing.OrderBy(m => m.PlaceholderTag).Select(m => $"{m.PlaceholderTag}:{m.SourceType}/{m.FieldName}"));

            _auditLogService.LogUpdate(db, "Template", templateId, oldSnapshot, newSnapshot, "Оновлено мапінг міток");
            db.SaveChanges();
        }

        public (bool Success, string? ErrorMessage) Delete(int templateId)
        {
            using var db = _dbFactory.CreateDbContext();
            var template = db.Templates.First(t => t.Id == templateId);

            var packageNames = db.GenerationPackageTemplates
                .Where(pt => pt.TemplateId == templateId)
                .Select(pt => pt.GenerationPackage!.Name)
                .Distinct()
                .ToList();

            if (packageNames.Count > 0)
            {
                return (false, $"Неможливо видалити: шаблон використовується в пакетах: {string.Join(", ", packageNames)}.");
            }

            var snapshot = template.Name;
            template.DeletedAt = DateTime.Now;
            template.DeletedBy = _currentUserContext.CurrentUserFullName;

            _auditLogService.LogDelete(db, "Template", template.Id, snapshot);
            db.SaveChanges();

            return (true, null);
        }

        private static (MappingSourceType SourceType, string? FieldName) ClassifyTag(string tagWithBraces)
        {
            var inner = tagWithBraces.Trim('{', '}').Trim().ToLowerInvariant();

            if (RecipientTagMap.TryGetValue(inner, out var recipientField))
                return (MappingSourceType.Recipient, recipientField);

            if (OrganizationTagMap.TryGetValue(inner, out var organizationField))
                return (MappingSourceType.Organization, organizationField);

            return (MappingSourceType.Manual, null);
        }

        private static List<string> ScanPlaceholders(WordprocessingDocument doc)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var tags = new List<string>();

            void Scan(string? text)
            {
                if (string.IsNullOrEmpty(text)) return;
                foreach (Match match in PlaceholderRegex.Matches(text))
                {
                    if (seen.Add(match.Value)) tags.Add(match.Value);
                }
            }

            var mainPart = doc.MainDocumentPart;
            if (mainPart?.Document?.Body is not null)
                Scan(mainPart.Document.Body.InnerText);

            if (mainPart is not null)
            {
                foreach (var header in mainPart.HeaderParts)
                    Scan(header.Header?.InnerText);

                foreach (var footer in mainPart.FooterParts)
                    Scan(footer.Footer?.InnerText);
            }

            return tags;
        }
    }
}
