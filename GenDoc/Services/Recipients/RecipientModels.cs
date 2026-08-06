using GenDoc.Models.Enums;

namespace GenDoc.Services.Recipients
{
    public record RecipientListItem(int Id, string FullName, string Rank, string Position, string UnitName, string RoomDisplay);

    public enum RecipientSortColumn
    {
        FullName,
        Rank,
        Position,
        UnitName,
        Room
    }

    public record RoomOccupancyInfo(int OccupantCount, int Capacity);

    public class RecipientEditModel
    {
        public int Id { get; set; }
        public string LastName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string? MiddleName { get; set; }
        public string Rank { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public string ServiceNumber { get; set; } = string.Empty;
        public DateOnly? DateOfBirth { get; set; }
        public string? UnitName { get; set; }
        public string? RoomBuilding { get; set; }
        public string? RoomNumber { get; set; }

        // Анкетні дані (прикомандировані)
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
        public DateOnly? TravelCertificateDate { get; set; }
        public string? FoodCertificate { get; set; }
        public string? IdDocumentNumber { get; set; }
        public string? MedicalBoard { get; set; }
        public string? MedicalBoardConclusion { get; set; }
        public string? OriginUnit { get; set; }
        public string? Vehicle { get; set; }
        public bool IsCourseOfficer { get; set; }

        // Уточнення відмінків
        public Gender? Gender { get; set; }
        public string? RankAccusative { get; set; }
        public string? FullNameAccusative { get; set; }
    }
}
