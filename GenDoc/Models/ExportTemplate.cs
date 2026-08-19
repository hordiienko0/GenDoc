using GenDoc.Models.Common;

namespace GenDoc.Models
{
    public class ExportTemplate : ISoftDeletable
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public byte[] Content { get; set; } = Array.Empty<byte>();
        public bool IsBuiltIn { get; set; }
        public DateTime UploadedAt { get; set; }

        // Рядок шаблону (з {{тегами}}), який клонується по одному на людину; за
        // замовчуванням 2 - стара поведінка "заголовок у рядку 1, дані з рядка 2".
        public int TemplateRowIndex { get; set; } = 2;
        public bool UsesPlaceholders { get; set; }

        // Перший аркуш книги - еталон; клонується на кожну дату з ручного тега
        // {{період}}, під назвою дд.ММ.рррр. Інші аркуші (довідкові) не чіпаються.
        public bool RepeatSheetPerDate { get; set; }

        // Парне до Template.BuilderJson: джерело блоків відомості, зібраної
        // конструктором. NULL - книга завантажена файлом, розібрати її назад
        // у блоки неможливо.
        public string? BuilderJson { get; set; }

        public DateTime? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }

        public ICollection<ExportTemplateColumnMapping> ColumnMappings { get; set; } = new List<ExportTemplateColumnMapping>();
    }
}
