using GenDoc.Models.Common;

namespace GenDoc.Models
{
    public class Vehicle : ISoftDeletable
    {
        public int Id { get; set; }

        // Марка/модель - напр. "КамАЗ-5350"
        public string Model { get; set; } = string.Empty;
        public string PlateNumber { get; set; } = string.Empty;
        public string? Note { get; set; }

        public DateTime? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }

        public ICollection<Recipient> Drivers { get; set; } = new List<Recipient>();
    }
}
