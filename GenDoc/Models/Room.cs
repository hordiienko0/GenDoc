using GenDoc.Models.Common;

namespace GenDoc.Models
{
    public class Room : ISoftDeletable
    {
        public int Id { get; set; }
        public string Building { get; set; } = string.Empty;
        public string Number { get; set; } = string.Empty;
        public int Capacity { get; set; } = 1;

        public DateTime? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }

        public ICollection<Recipient> Occupants { get; set; } = new List<Recipient>();
    }
}
