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
            }
            catch (FileNotFoundException)
            {
                return new UploadResult(false, $"Файл не знайдено: {filePath}");
            }
            catch (DirectoryNotFoundException)
            {
                return new UploadResult(false, $"Теку не знайдено: {Path.GetDirectoryName(filePath)}");
            }
            catch (IOException ex)
            {
                return new UploadResult(false, $"Не вдалося прочитати файл: {ex.Message}");
            }
            catch (UnauthorizedAccessException)
            {
                return new UploadResult(false, "Немає прав на читання цього файлу.");
            }

            try
            {
                using var stream = new MemoryStream(content);
                using var doc = WordprocessingDocument.Open(stream, false);
                scan = ScanPlaceholders(doc);
            }
            catch (Exception ex)
            {
                return new UploadResult(false,
                    "Файл не є документом Word (.docx). Якщо це текстова чернетка - відкрийте її у Word "
                    + $"і збережіть як .docx. Технічна причина: {ex.Message}");
            }

            using var db = _dbFactory.CreateDbContext();

            var template = new Template
            {
                Name = TemplateNaming.Clean(Path.GetFileNameWithoutExtension(filePath)),
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

        public List<(int Id, string Name, string? ShortName, string OriginalFileName, DateTime UploadedAt, int TagCount, bool IsFromBuilder, TemplateAudience Audience, TemplateKind Kind)> GetTemplateListItems()
        {
            using var db = _dbFactory.CreateDbContext();
            return db.Templates
                .OrderBy(t => t.Name)
                .Select(t => new
                {
                    t.Id, t.Name, t.ShortName, t.OriginalFileName, t.UploadedAt,
                    TagCount = t.FieldMappings.Count,
                    IsFromBuilder = t.BuilderJson != null,
                    t.Audience, t.Kind
                })
                .AsEnumerable()
                .Select(t => (t.Id, t.Name, t.ShortName, t.OriginalFileName, t.UploadedAt, t.TagCount, t.IsFromBuilder, t.Audience, t.Kind))
                .ToList();
        }

        public void SaveAudience(int templateId, TemplateAudience audience)
        {
            using var db = _dbFactory.CreateDbContext();
            var template = db.Templates.First(t => t.Id == templateId);
            var old = template.Audience;
            template.Audience = audience;

            _auditLogService.LogUpdate(db, "Template", template.Id, old.ToString(), audience.ToString(),
                $"{template.Name}: змінено призначення шаблону");
            db.SaveChanges();
        }

        public void SaveShortName(int templateId, string? shortName)
        {
            using var db = _dbFactory.CreateDbContext();
            var template = db.Templates.First(t => t.Id == templateId);
            var old = template.ShortName;
            template.ShortName = string.IsNullOrWhiteSpace(shortName) ? null : shortName.Trim();

            _auditLogService.LogUpdate(db, "Template", template.Id, old, template.ShortName,
                $"{template.Name}: змінено коротку назву");
            db.SaveChanges();
        }

        public List<(int Id, string PlaceholderTag, MappingSourceType SourceType, string? FieldName, string? DateFormat)> GetMappings(int templateId)
        {
            using var db = _dbFactory.CreateDbContext();
            return db.TemplateFieldMappings
                .Where(m => m.TemplateId == templateId)
                .OrderBy(m => m.PlaceholderTag)
                .Select(m => new { m.Id, m.PlaceholderTag, m.SourceType, m.FieldName, m.DateFormat })
                .AsEnumerable()
                .Select(m => (m.Id, m.PlaceholderTag, m.SourceType, m.FieldName, m.DateFormat))
                .ToList();
        }

        public void SaveMappings(int templateId, List<(int Id, MappingSourceType SourceType, string? FieldName, string? DateFormat)> mappings)
        {
            using var db = _dbFactory.CreateDbContext();
            var existing = db.TemplateFieldMappings.Where(m => m.TemplateId == templateId).ToList();

            var oldSnapshot = string.Join(", ", existing.OrderBy(m => m.PlaceholderTag).Select(m => $"{m.PlaceholderTag}:{m.SourceType}/{m.FieldName}"));

            foreach (var (id, sourceType, fieldName, dateFormat) in mappings)
            {
                var mapping = existing.FirstOrDefault(m => m.Id == id);
                if (mapping is null) continue;

                mapping.SourceType = sourceType;
                mapping.FieldName = sourceType == MappingSourceType.Manual ? null : fieldName ?? mapping.FieldName;
                mapping.DateFormat = sourceType == MappingSourceType.Manual ? null : dateFormat;
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

        internal static ScanResult ScanPlaceholders(WordprocessingDocument doc)
        {
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            var tags = new List<(string Tag, bool IsInsideBlock)>();
            var hasBlock = false;

            void CollectTags(OpenXmlElement element, bool insideBlock)
            {
                var paragraphs = element is Paragraph paragraph
                    ? new[] { paragraph }.AsEnumerable()
                    : element.Descendants<Paragraph>();

                foreach (var inner in paragraphs)
                {
                    var text = BlockStructure.MarkerText(inner);

                    foreach (Match match in PlaceholderRegex.Matches(text))
                    {
                        if (ReservedBlockTags.Contains(match.Value)) continue;

                        if (BlockStructure.IsMarkerTag(match.Value)) continue;

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

            void ScanSiblings(List<OpenXmlElement> siblings, bool insideTable)
            {
                string? openName = null;

                foreach (var element in siblings)
                {
                    var markerText = BlockStructure.MarkerText(element);

                    if (BlockStructure.OpenName(markerText) is { } opened)
                    {
                        openName = opened;
                        hasBlock = true;
                        continue;
                    }

                    if (BlockStructure.CloseName(markerText) is not null)
                    {
                        openName = null;
                        continue;
                    }

                    if (openName is null && !insideTable && element is Table table)
                    {
                        ScanSiblings(BlockStructure.Rows(table).Cast<OpenXmlElement>().ToList(), insideTable: true);
                        continue;
                    }

                    CollectTags(element, insideBlock: openName is not null);
                }
            }

            void ScanContainer(OpenXmlCompositeElement? container)
            {
                if (container is null) return;
                ScanSiblings(BlockStructure.BlockChildren(container).ToList(), insideTable: false);

                foreach (var paragraph in container.Descendants<Paragraph>())
                    CollectTags(paragraph, insideBlock: false);
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
