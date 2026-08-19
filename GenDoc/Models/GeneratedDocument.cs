using GenDoc.Models.Common;
using GenDoc.Models.Enums;

namespace GenDoc.Models
{
    public class GeneratedDocument : ISoftDeletable
    {
        public int Id { get; set; }

        public int RecipientId { get; set; }
        public Recipient? Recipient { get; set; }

        public int TemplateId { get; set; }
        public Template? Template { get; set; }

        public DateTime GeneratedAt { get; set; }

        public int GeneratedByUserId { get; set; }
        public UserProfile? GeneratedByUser { get; set; }

        // Мапиться на історичну колонку OutputFileName.
        public string FileName { get; set; } = string.Empty;

        public string? ContentHash { get; set; }

        // Хеш замаплених (нерукописних) значень на момент генерації - для стану «застарів».
        public string? SourceHash { get; set; }
        public long SizeBytes { get; set; }
        public int Version { get; set; } = 1;
        public bool IsCurrent { get; set; } = true;
        public DocumentSourceType SourceType { get; set; } = DocumentSourceType.Generated;

        public int? RunId { get; set; }
        public int? IntakeId { get; set; }
        public int? OrgNodeIdSnapshot { get; set; }
        public string? OrgPathSnapshot { get; set; }

        // Байти в окремій 1:1 таблиці; false - легасі-записи без збереженого файлу.
        public bool HasContent { get; set; }

        public DateTime? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }

        public GeneratedDocumentContent? Content { get; set; }
        public ICollection<DocumentAttachment> Attachments { get; set; } = new List<DocumentAttachment>();
    }
}
