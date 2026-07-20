namespace GenDoc.Services.Recipients
{
    public interface IRecipientService
    {
        List<RecipientListItem> Search(string? searchText, RecipientSortColumn sortColumn = RecipientSortColumn.FullName, bool sortDescending = false);
        RecipientEditModel? GetForEdit(int id);
        List<string> GetUnitNames();
        List<string> GetRoomBuildings();
        List<string> GetRanks();
        RoomOccupancyInfo? GetRoomOccupancy(string? building, string? number, int excludeRecipientId);
        string? FindDuplicateServiceNumberOwner(string? serviceNumber, int excludeRecipientId);
        void Save(RecipientEditModel model, out string? errorMessage);
        void Delete(int id);
    }
}
