using GenDoc.Models.Common;

namespace GenDoc.Models
{
    public class GeneratedGroupDocument : ISoftDeletable
    {
        public int Id { get; set; }

        public int ExportTemplateId { get; set; }
        public ExportTemplate? ExportTemplate { get; set; }

        public int? RunId { get; set; }
        public int? IntakeId { get; set; }

        public DateTime GeneratedAt { get; set; }

        public int GeneratedByUserId { get; set; }
        public UserProfile? GeneratedByUser { get; set; }

        public string FileName { get; set; } = string.Empty;
        public string? ContentHash { get; set; }

        // Анти-дубль для групового документа: хеш впорядкованого складу (RecipientId, SourceHash) + шаблон.
        public string? RosterHash { get; set; }

        public long SizeBytes { get; set; }
        public int RecipientCount { get; set; }
        public int Version { get; set; } = 1;
        public bool IsCurrent { get; set; } = true;

        // Байти в окремій 1:1 таблиці; false — легасі-записи без збереженого файлу.
        public bool HasContent { get; set; }

        public DateTime? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }

        public GeneratedGroupDocumentContent? Content { get; set; }
    }
}
