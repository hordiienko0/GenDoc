using GenDoc.Models.Enums;

namespace GenDoc.Models
{
    public class TemplateFieldMapping
    {
        public int Id { get; set; }

        public int TemplateId { get; set; }
        public Template? Template { get; set; }

        public string PlaceholderTag { get; set; } = string.Empty;
        public MappingSourceType SourceType { get; set; }
        public string? FieldName { get; set; }
    }
}
