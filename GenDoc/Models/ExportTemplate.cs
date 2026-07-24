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

        public DateTime? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }

        public ICollection<ExportTemplateColumnMapping> ColumnMappings { get; set; } = new List<ExportTemplateColumnMapping>();
    }
}
