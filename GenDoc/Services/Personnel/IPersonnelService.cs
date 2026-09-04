using GenDoc.Models;

namespace GenDoc.Services.Personnel
{
    public record PersonListItem(
        int Id,
        string LastName,
        string FirstName,
        string? MiddleName,
        string Rank,
        string Position,
        string? FitnessCategory,
        string RoomDisplay,
        int OrgNodeId,
        int? IntakeId);

    public class PersonEditModel
    {
        public int Id { get; set; }
        public string LastName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string? MiddleName { get; set; }
        public string Rank { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public string ServiceNumber { get; set; } = string.Empty;
        public string? RoomBuilding { get; set; }
        public string? RoomNumber { get; set; }
        public string? FitnessCategory { get; set; }
        public int OrgNodeId { get; set; }
        public int? IntakeId { get; set; }
    }

    public record PersonSaveResult(bool Success, int Id, Dictionary<string, string> Errors);

    public record DeletedPersonInfo(
        int Id,
        string FullName,
        string Rank,
        string? Folder,
        bool FolderAlive,
        DateTime DeletedAt,
        string? DeletedBy);

    public record PersonRestoreResult(bool Success, string? Message);

    public interface IPersonnelService
    {
        Task<List<PersonListItem>> QueryByNodeAsync(int nodeId, bool includeDescendants);
        Task<PersonEditModel?> GetForEditAsync(int id);
        Task<PersonSaveResult> SaveAsync(PersonEditModel model);
        Task MoveManyAsync(IReadOnlyList<int> recipientIds, int targetNodeId, string sourceBranchName);
        Task DeleteManyAsync(IReadOnlyList<int> recipientIds);
        Task<List<DeletedPersonInfo>> GetDeletedAsync();
        Task<PersonRestoreResult> RestoreAsync(int id);
    }
}
