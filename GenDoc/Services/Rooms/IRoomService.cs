namespace GenDoc.Services.Rooms
{
    public record RoomOccupantSummary(int RecipientId, string ShortName);

    public record RoomOverview(int Id, string Building, string Number, int Capacity, string? Note, List<RoomOccupantSummary> Occupants);

    public record RoomOccupantDto(int RecipientId, string FullName, string ShortName, string Rank, string Position, string Unit, string PersonalNumber);

    public record UnassignedRecipientDto(int Id, string FullName, string Rank, string Unit);

    public interface IRoomService
    {
        List<RoomOverview> GetAll();
        (bool Success, string? ErrorMessage) Create(string building, string number, int capacity, string? note);
        (bool Success, string? ErrorMessage) Update(int id, string building, string number, int capacity, string? note);
        void Delete(int id);

        List<RoomOccupantDto> GetOccupants(int roomId);
        List<UnassignedRecipientDto> SearchUnassigned(string? searchText, int take = 8);
        void AssignOccupant(int recipientId, int roomId);
        void UnassignOccupant(int recipientId);
    }
}
