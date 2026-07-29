using GenDoc.Models;

namespace GenDoc.Services.OrgTree
{
    public record DeletedFolderInfo(int Id, string Name, DateTime DeletedAt, string? DeletedBy, bool ParentAlive);

    public interface IOrgTreeService
    {
        Task<List<OrgNode>> GetAllAsync();
        Task<OrgNode> CreateAsync(int parentId, string name, string? documentName);
        Task RenameAsync(int id, string name, string? documentName);
        Task MoveAsync(int id, int newParentId);
        Task<int> CountPeopleInBranchAsync(int nodeId);
        Task DeleteAsync(int id);
        Task<List<DeletedFolderInfo>> GetDeletedFoldersAsync();
        Task RestoreAsync(int id, int? newParentId);
    }
}
