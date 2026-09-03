namespace GenDoc.Services.OrgTree
{
    public interface ICountService
    {
        Task<Dictionary<int, int>> GetTreeCountsAsync();
    }
}
