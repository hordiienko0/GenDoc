using GenDoc.Models.Common;

namespace GenDoc.Models
{
    public class Unit : ISoftDeletable
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        public DateTime? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }

        public ICollection<Recipient> Recipients { get; set; } = new List<Recipient>();
    }
}
