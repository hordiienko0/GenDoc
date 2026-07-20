namespace GenDoc.Services.Recipients
{
    public interface IRecipientService
    {
        List<RecipientListItem> Search(string? searchText);
        RecipientEditModel? GetForEdit(int id);
        List<string> GetUnitNames();
        List<string> GetRoomBuildings();
        void Save(RecipientEditModel model);
        void Delete(int id);
    }
}
