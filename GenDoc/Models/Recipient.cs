using GenDoc.Models.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace GenDoc.Models
{
    public class Recipient : ISoftDeletable
    {
        public int Id { get; set; }

        public string LastName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string? MiddleName { get; set; }

        public string Rank { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public string ServiceNumber { get; set; } = string.Empty;
        public DateOnly? DateOfBirth { get; set; }

        public int? UnitId { get; set; }
        public Unit? Unit { get; set; }

        public int? RoomId { get; set; }
        public Room? Room { get; set; }

        public DateTime? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }

        public ICollection<GeneratedDocument> GeneratedDocuments { get; set; } = new List<GeneratedDocument>();

        [NotMapped]
        public string FullName => string.Join(' ', new[] { LastName, FirstName, MiddleName }
            .Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}
