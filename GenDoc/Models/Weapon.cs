using GenDoc.Models.Common;

namespace GenDoc.Models
{
    public class Weapon : ISoftDeletable
    {
        public int Id { get; set; }

        public int RecipientId { get; set; }
        public Recipient? Recipient { get; set; }

        public string Name { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;

        public string? RawText { get; set; }

        public DateOnly? IssuedAt { get; set; }
        public string? Note { get; set; }

        public DateTime? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }
    }
}
