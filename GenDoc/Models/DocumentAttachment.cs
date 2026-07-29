using GenDoc.Models.Common;

namespace GenDoc.Models
{
    public class DocumentAttachment : ISoftDeletable
    {
        public int Id { get; set; }

        public int GeneratedDocumentId { get; set; }
        public GeneratedDocument? GeneratedDocument { get; set; }

        public string FileName { get; set; } = string.Empty;
        public byte[] Content { get; set; } = Array.Empty<byte>();
        public long SizeBytes { get; set; }
        public string? Note { get; set; }

        public string UploadedBy { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; }

        public DateTime? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }
    }
}
