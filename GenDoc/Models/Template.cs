using GenDoc.Models.Common;

namespace GenDoc.Models
{
    public class Template : ISoftDeletable
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public byte[] Content { get; set; } = Array.Empty<byte>();
        public DateTime UploadedAt { get; set; }

        public DateTime? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }

        public ICollection<TemplateFieldMapping> FieldMappings { get; set; } = new List<TemplateFieldMapping>();
        public ICollection<GeneratedDocument> GeneratedDocuments { get; set; } = new List<GeneratedDocument>();
    }
}