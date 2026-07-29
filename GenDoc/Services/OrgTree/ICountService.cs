namespace GenDoc.Services.OrgTree
{
    public interface ICountService
    {
        // ОДИН запит: людей безпосередньо в кожному вузлі; total рахує споживач по Path-префіксу.
        Task<Dictionary<int, int>> GetTreeCountsAsync();
    }
}
