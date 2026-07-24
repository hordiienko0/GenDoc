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

        // Анкетні дані (прикомандировані) — Б.1
        public string? Nationality { get; set; }
        public string? Vos { get; set; }
        public DateOnly? CourseArrivalDate { get; set; }
        public string? MaritalStatus { get; set; }
        public string? RegistrationAddress { get; set; }
        public string? ResidenceAddress { get; set; }
        public string? Phone { get; set; }
        public string? Note { get; set; }
        public string? GroupName { get; set; }
        public string? NameTransliterated { get; set; }
        public string? ServedBefore { get; set; }
        public string? ExtraNote { get; set; }
        public string? CommanderContact { get; set; }
        public string? TravelCertificateNumber { get; set; }
        public string? FoodCertificate { get; set; }
        public string? IdDocumentNumber { get; set; }
        public string? MedicalBoard { get; set; }
        public string? MedicalBoardConclusion { get; set; }
        public string? OriginUnit { get; set; }
        public string? Vehicle { get; set; }

        public DateTime? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }

        public ICollection<GeneratedDocument> GeneratedDocuments { get; set; } = new List<GeneratedDocument>();

        [NotMapped]
        public string FullName => string.Join(' ', new[] { LastName, FirstName, MiddleName }
            .Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}
