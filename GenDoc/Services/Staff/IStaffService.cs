using GenDoc.Models.Enums;

namespace GenDoc.Services.Staff
{
    public record StaffRowOverview(
        int Id, string FullName, string Rank, string Position, string? UnitName,
        StaffEventKind? CurrentState, int DocsCount);

    public interface IStaffService
    {
        Task<IReadOnlyList<StaffRowOverview>> GetOverviewAsync();
        Task<IReadOnlyList<string>> GetUnitsAsync();
        Task IssueDocumentsAsync(
            StaffEventKind kind, IReadOnlyList<int> recipientIds, IReadOnlyList<int> templateIds,
            DateOnly dateStart, DateOnly dateEnd, string? note);
        Task DeleteManyAsync(IReadOnlyList<int> recipientIds);
    }
}
