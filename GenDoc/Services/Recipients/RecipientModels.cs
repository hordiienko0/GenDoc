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
    }
}
