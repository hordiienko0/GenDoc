using GenDoc.Models.Enums;

namespace GenDoc.Models
{
    public class ExportTemplateColumnMapping
    {
        public int Id { get; set; }

        public int ExportTemplateId { get; set; }
        public ExportTemplate? ExportTemplate { get; set; }

        public int ColumnIndex { get; set; }
        public string HeaderText { get; set; } = string.Empty;
        public string FieldKey { get; set; } = string.Empty;

        public string PlaceholderTag { get; set; } = string.Empty;
        public MappingSourceType SourceType { get; set; }
    }
}
