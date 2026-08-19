using GenDoc.Models.Common;

namespace GenDoc.Models
{
    public class Room : ISoftDeletable
    {
        public int Id { get; set; }
        public string Building { get; set; } = string.Empty;
        public string Number { get; set; } = string.Empty;
        public int Capacity { get; set; } = 1;
        public string? Note { get; set; }

        // Nullable, а не DateTime - щоб ALTER TABLE ADD COLUMN на існуючих рядках
        // (де значення стає NULL) не падав при читанні через EF (як HrOfficerFullName).
        public DateTime? CreatedAt { get; set; }
        public string? CreatedBy { get; set; }

        public DateTime? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }

        public ICollection<Recipient> Occupants { get; set; } = new List<Recipient>();
    }
}
