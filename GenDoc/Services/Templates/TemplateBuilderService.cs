using System.IO;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using GenDoc.Data;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Models.TemplateBuilder;
using GenDoc.Services.Generation;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services.Templates
{
    public class TemplateBuilderService : ITemplateBuilderService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly IAuditLogService _auditLogService;

        public TemplateBuilderService(
            IDbContextFactory<AppDbContext> dbFactory,
            IAuditLogService auditLogService)
        {
            _dbFactory = dbFactory;
            _auditLogService = auditLogService;
        }

        public IReadOnlyList<BuilderTestPerson> GetTestPeople()
        {
            using var db = _dbFactory.CreateDbContext();

            var activeIntakeId = db.Intakes
                .Where(i => i.Status == IntakeStatus.Active)
                .OrderByDescending(i => i.Number)
                .Select(i => (int?)i.Id)
                .FirstOrDefault();

            // Людина активного набору — найтиповіший адресат документа. Якщо набору
            // нема, показуємо постійний склад, щоб прев'ю не лишалось порожнім.
            var query = activeIntakeId is int intakeId
                ? db.Recipients.Where(r => r.IntakeId == intakeId)
                : db.Recipients.Where(r => r.IntakeId == null);

            return query
                .OrderBy(r => r.LastName).ThenBy(r => r.FirstName)
                .Select(r => new { r.Id, r.Rank, r.LastName, r.FirstName, r.MiddleName })
                .AsEnumerable()
                .Select(r => new BuilderTestPerson(
                    r.Id,
                    Join(r.Rank, NameFormatter.FullName(r.LastName, r.FirstName, r.MiddleName))))
                .ToList();
        }

        public IReadOnlyList<BuilderSignatory> GetSignatories()
        {
            using var db = _dbFactory.CreateDbContext();

            return db.Recipients
                .Where(r => r.IntakeId == null)
                .OrderBy(r => r.LastName).ThenBy(r => r.FirstName)
                .Select(r => new { r.Id, r.Rank, r.Position, r.LastName, r.FirstName, r.MiddleName })
                .AsEnumerable()
                .Select(r =>
                {
                    var shortName = NameFormatter.ShortName(r.LastName, r.FirstName, r.MiddleName);
                    var display = Join(r.Rank, shortName);
                    if (!string.IsNullOrWhiteSpace(r.Position)) display += $" — {r.Position}";
                    return new BuilderSignatory(r.Id, display, r.Rank, shortName);
                })
                .ToList();
        }

        public IReadOnlyDictionary<string, string> ResolveValues(int recipientId, IReadOnlyList<string> tags)
        {
            using var db = _dbFactory.CreateDbContext();

            // Explicit Include: lazy loading вимкнено, тож без цього {{підрозділ}},
            // {{кімната}} і теги зброї мовчки віддали б порожнє.
            var recipient = db.Recipients
                .Include(r => r.Unit)
                .Include(r => r.Room)
                .Include(r => r.Weapons)
                .AsNoTracking()
                .FirstOrDefault(r => r.Id == recipientId);

            if (recipient is null) return new Dictionary<string, string>();

            var organization = db.OrganizationSettings.AsNoTracking().FirstOrDefault();

            var mappings = tags
                .Select(tag =>
                {
                    var (sourceType, fieldName) = PlaceholderTagMaps.Classify(tag);
                    return new TemplateFieldMapping
                    {
                        PlaceholderTag = tag,
                        SourceType = sourceType,
                        FieldName = fieldName
                    };
                })
                .ToList();

            // Той самий підставник, що й на генерації — прев'ю не має власного
            // тлумачення полів, інакше воно розійшлося б із документом.
            return GenerationService.BuildValues(
                mappings, recipient, organization, new Dictionary<string, string>());
        }

        public byte[] BuildDocx(TemplateBuilderDocument document)
            => TemplateBlockDocxWriter.Write(document, LoadSignatories(document));

        public XlsxBuildResult BuildXlsx(TemplateBuilderDocument document)
            => TemplateBlockXlsxWriter.Write(document, LoadSignatories(document));

        public int Save(int? templateId, string name, TemplateBuilderDocument document)
            => document.Mode == TemplateBuilderMode.Excel
                ? SaveExcel(templateId, name, document)
                : SaveWord(templateId, name, document);

        public BuilderTemplateSource? Load(int templateId, TemplateBuilderMode mode)
            => mode == TemplateBuilderMode.Excel ? LoadExcel(templateId) : LoadWord(templateId);

        private int SaveWord(int? templateId, string name, TemplateBuilderDocument document)
        {
            var content = BuildDocx(document);
            var cleanName = TemplateNaming.Clean(name);
            var builderJson = TemplateBuilderJson.Serialize(document);

            using var db = _dbFactory.CreateDbContext();

            var template = templateId is int id
                ? db.Templates.Include(t => t.FieldMappings).First(t => t.Id == id)
                : new Template();

            var isNew = templateId is null;
            var oldSnapshot = isNew ? null : $"{template.Name} ({template.FieldMappings.Count} міток)";

            template.Name = cleanName;
            template.OriginalFileName = BuildFileName(cleanName);
            template.Content = content;
            template.UploadedAt = DateTime.Now;
            template.BuilderJson = builderJson;

            if (isNew) db.Templates.Add(template);

            // Kind визначає сканер, а не конструктор: таблиця з повторюваним рядком
            // дає маркери {{#…}}, а отже це груповий документ на весь список — так
            // само, як для шаблону, завантаженого файлом.
            template.Kind = SyncMappings(template, content);

            db.SaveChanges();

            if (isNew)
            {
                _auditLogService.LogCreate(db, "Template", template.Id, template.Name,
                    $"Зібрано в конструкторі, блоків: {document.Blocks.Count}, міток: {template.FieldMappings.Count}");
            }
            else
            {
                _auditLogService.LogUpdate(db, "Template", template.Id, oldSnapshot,
                    $"{template.Name} ({template.FieldMappings.Count} міток)", "Оновлено в конструкторі");
            }

            db.SaveChanges();

            return template.Id;
        }

        private BuilderTemplateSource? LoadWord(int templateId)
        {
            using var db = _dbFactory.CreateDbContext();

            var template = db.Templates
                .AsNoTracking()
                .Where(t => t.Id == templateId)
                .Select(t => new { t.Id, t.Name, t.BuilderJson })
                .FirstOrDefault();

            if (template is null) return null;

            var document = TemplateBuilderJson.Deserialize(template.BuilderJson);
            if (document is null) return null;

            return new BuilderTemplateSource(template.Id, template.Name, document);
        }

        private int SaveExcel(int? templateId, string name, TemplateBuilderDocument document)
        {
            var built = BuildXlsx(document);
            var cleanName = TemplateNaming.Clean(name);
            var builderJson = TemplateBuilderJson.Serialize(document);

            using var db = _dbFactory.CreateDbContext();

            var template = templateId is int id
                ? db.ExportTemplates.Include(t => t.ColumnMappings).First(t => t.Id == id)
                : new ExportTemplate();

            var isNew = templateId is null;
            var oldSnapshot = isNew ? null : $"{template.Name} ({template.ColumnMappings.Count} міток)";

            template.Name = cleanName;
            template.OriginalFileName = BuildFileName(cleanName, ".xlsx");
            template.Content = built.Content;
            template.UploadedAt = DateTime.Now;
            template.IsBuiltIn = false;
            // Відомість конструктора — завжди книга за тегами: рядок під шапкою
            // таблиці клонується по одному на людину.
            template.UsesPlaceholders = true;
            template.TemplateRowIndex = built.TemplateRowIndex;
            template.RepeatSheetPerDate = document.RepeatSheetPerDate;
            template.BuilderJson = builderJson;

            if (isNew) db.ExportTemplates.Add(template);

            SyncExportMappings(template, built.Content, built.TemplateRowIndex);

            db.SaveChanges();

            if (isNew)
            {
                _auditLogService.LogCreate(db, "ExportTemplate", template.Id, template.Name,
                    $"Зібрано в конструкторі, блоків: {document.Blocks.Count}, міток: {template.ColumnMappings.Count}");
            }
            else
            {
                _auditLogService.LogUpdate(db, "ExportTemplate", template.Id, oldSnapshot,
                    $"{template.Name} ({template.ColumnMappings.Count} міток)", "Оновлено в конструкторі");
            }

            db.SaveChanges();

            return template.Id;
        }

        private BuilderTemplateSource? LoadExcel(int exportTemplateId)
        {
            using var db = _dbFactory.CreateDbContext();

            var template = db.ExportTemplates
                .AsNoTracking()
                .Where(t => t.Id == exportTemplateId)
                .Select(t => new { t.Id, t.Name, t.BuilderJson })
                .FirstOrDefault();

            if (template is null) return null;

            var document = TemplateBuilderJson.Deserialize(template.BuilderJson);
            if (document is null) return null;

            return new BuilderTemplateSource(template.Id, template.Name, document);
        }

        /// <summary>Мітки описує той самий сканер, що й при завантаженні книги файлом
        /// (ExportTemplateService.BuildPlaceholderMappings) — інакше зібрана відомість
        /// і така сама завантажена поводились би на генерації по-різному.</summary>
        private static void SyncExportMappings(ExportTemplate template, byte[] content, int templateRowIndex)
        {
            using var stream = new MemoryStream(content);
            using var workbook = new XLWorkbook(stream);
            var usedRange = workbook.Worksheets.First().RangeUsed();

            var scanned = new ExportTemplate();
            if (usedRange is not null)
                ExportTemplateService.BuildPlaceholderMappings(scanned, usedRange, templateRowIndex);

            var existing = template.ColumnMappings.ToList();
            template.ColumnMappings.Clear();

            foreach (var mapping in scanned.ColumnMappings)
            {
                // Ручні правки джерела для тега, що лишився на тому самому місці,
                // переживають перезбереження — як і в Word-гілці.
                var previous = existing.FirstOrDefault(e =>
                    e.PlaceholderTag == mapping.PlaceholderTag && e.ColumnIndex == mapping.ColumnIndex);

                template.ColumnMappings.Add(previous ?? mapping);
            }
        }

        /// <summary>Мітки беруться з уже зібраних байтів, а не з моделі блоків: так
        /// мапінг за побудовою описує саме те, що лежить у шаблоні. Налаштування
        /// джерела для тегів, які лишились, зберігаються — інакше кожне збереження
        /// скидало б ручні правки оператора.</summary>
        private static TemplateKind SyncMappings(Template template, byte[] content)
        {
            using var stream = new MemoryStream(content);
            using var word = WordprocessingDocument.Open(stream, false);
            var scan = TemplateService.ScanPlaceholders(word);

            var existing = template.FieldMappings.ToList();
            var scanned = scan.Tags.Select(t => t.Tag).ToHashSet(StringComparer.Ordinal);

            foreach (var mapping in existing.Where(m => !scanned.Contains(m.PlaceholderTag)))
                template.FieldMappings.Remove(mapping);

            foreach (var (tag, isInsideBlock) in scan.Tags)
            {
                var mapping = existing.FirstOrDefault(m => m.PlaceholderTag == tag);
                if (mapping is not null)
                {
                    mapping.IsInsideRepeatingBlock = isInsideBlock;
                    continue;
                }

                var (sourceType, fieldName) = PlaceholderTagMaps.Classify(tag);
                template.FieldMappings.Add(new TemplateFieldMapping
                {
                    PlaceholderTag = tag,
                    SourceType = sourceType,
                    FieldName = fieldName,
                    IsInsideRepeatingBlock = isInsideBlock
                });
            }

            return scan.HasBlock ? TemplateKind.Group : TemplateKind.PerRecipient;
        }

        private IReadOnlyDictionary<int, SignatoryInfo> LoadSignatories(TemplateBuilderDocument document)
        {
            var ids = document.Blocks
                .SelectMany(b => b.Signatures ?? Array.Empty<SignatureLine>())
                .Select(s => s.RecipientId)
                .OfType<int>()
                .Distinct()
                .ToList();

            if (ids.Count == 0) return new Dictionary<int, SignatoryInfo>();

            using var db = _dbFactory.CreateDbContext();

            // Звання і ПІБ читаються на момент збирання документа, а не зберігаються
            // в блоці: підвищили людину — наступний .docx підхопить нове звання сам.
            return db.Recipients
                .Where(r => ids.Contains(r.Id))
                .Select(r => new { r.Id, r.Rank, r.LastName, r.FirstName, r.MiddleName })
                .AsEnumerable()
                .ToDictionary(
                    r => r.Id,
                    r => new SignatoryInfo(r.Rank, NameFormatter.ShortName(r.LastName, r.FirstName, r.MiddleName)));
        }

        private static string BuildFileName(string name, string extension = ".docx")
        {
            var safe = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
            if (string.IsNullOrWhiteSpace(safe)) safe = "Шаблон";
            return $"{safe.Trim()}{extension}";
        }

        private static string Join(params string?[] parts)
            => string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}
