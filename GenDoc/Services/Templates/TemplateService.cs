using System.IO;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GenDoc.Data;
using GenDoc.Models;
using GenDoc.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services.Templates
{
    public class TemplateService : ITemplateService
    {
        private static readonly Regex PlaceholderRegex = new(@"\{\{[^{}]+\}\}", RegexOptions.Compiled);

        // Ті самі маркери повторюваного блоку, що й у DocumentGenerationService —
        // тримати регекси в синхроні, якщо синтаксис блоку колись зміниться.
        private static readonly Regex BlockOpenRegex = new(@"^\{\{#([^{}]+)\}\}$", RegexOptions.Compiled);
        private static readonly Regex BlockCloseRegex = new(@"^\{\{/([^{}]+)\}\}$", RegexOptions.Compiled);

        // Обчислювані тегі рушія блоків — не поля, тому в мапінг не потрапляють.
        private static readonly HashSet<string> ReservedBlockTags = new(StringComparer.Ordinal)
        {
            "{{роздільник}}", "{{номер}}", "{{кількість_осіб}}"
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
            ScanResult scan;

            try
            {
                content = File.ReadAllBytes(filePath);
                using var stream = new MemoryStream(content);
                using var doc = WordprocessingDocument.Open(stream, false);
                scan = ScanPlaceholders(doc);
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
                UploadedAt = DateTime.Now,
                Kind = scan.HasBlock ? TemplateKind.Group : TemplateKind.PerRecipient
            };

            foreach (var (tag, isInsideBlock) in scan.Tags)
            {
                var (sourceType, fieldName) = ClassifyTag(tag);
                template.FieldMappings.Add(new TemplateFieldMapping
                {
                    PlaceholderTag = tag,
                    SourceType = sourceType,
                    FieldName = fieldName,
                    IsInsideRepeatingBlock = isInsideBlock
                });
            }

            db.Templates.Add(template);
            db.SaveChanges();

            _auditLogService.LogCreate(db, "Template", template.Id, template.Name,
                $"Завантажено файл {template.OriginalFileName}, міток: {scan.Tags.Count}, тип: {template.Kind}");
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
            => PlaceholderTagMaps.Classify(tagWithBraces);

        internal sealed record ScanResult(List<(string Tag, bool IsInsideBlock)> Tags, bool HasBlock);

        // Абзац-за-абзацом (а не InnerText усього контейнера) — інакше не видно меж
        // повторюваного блоку {{#…}}/{{/…}}, які завжди займають цілий абзац.
        internal static ScanResult ScanPlaceholders(WordprocessingDocument doc)
        {
            var seen = new Dictionary<string, int>(StringComparer.Ordinal); // тег → індекс у tags
            var tags = new List<(string Tag, bool IsInsideBlock)>();
            var hasBlock = false;

            void ScanContainer(OpenXmlCompositeElement? container)
            {
                if (container is null) return;

                string? openBlockName = null;
                foreach (var paragraph in container.Descendants<Paragraph>())
                {
                    var text = string.Concat(paragraph.Descendants<Text>().Select(t => t.Text)).Trim();

                    if (BlockOpenRegex.IsMatch(text))
                    {
                        openBlockName = BlockOpenRegex.Match(text).Groups[1].Value;
                        hasBlock = true;
                        continue;
                    }

                    if (BlockCloseRegex.IsMatch(text))
                    {
                        openBlockName = null;
                        continue;
                    }

                    var insideBlock = openBlockName is not null;
                    foreach (Match match in PlaceholderRegex.Matches(text))
                    {
                        if (ReservedBlockTags.Contains(match.Value)) continue;

                        if (seen.TryGetValue(match.Value, out var index))
                        {
                            if (insideBlock && !tags[index].IsInsideBlock)
                                tags[index] = (match.Value, true);
                        }
                        else
                        {
                            seen[match.Value] = tags.Count;
                            tags.Add((match.Value, insideBlock));
                        }
                    }
                }
            }

            var mainPart = doc.MainDocumentPart;
            if (mainPart?.Document?.Body is not null)
                ScanContainer(mainPart.Document.Body);

            if (mainPart is not null)
            {
                foreach (var header in mainPart.HeaderParts)
                    ScanContainer(header.Header);

                foreach (var footer in mainPart.FooterParts)
                    ScanContainer(footer.Footer);
            }

            return new ScanResult(tags, hasBlock);
        }
    }
}
