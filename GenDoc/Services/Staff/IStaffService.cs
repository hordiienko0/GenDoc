using GenDoc.Models.Enums;

namespace GenDoc.Services.Staff
{
    public record StaffRowOverview(
        int Id, string FullName, string Rank, string Position, string? UnitName,
        StaffEventKind? CurrentState, int DocsCount, bool IsCourseOfficer);

    public record StaffPickerPerson(int Id, string LastName, string FirstName, string Rank);

    public interface IStaffService
    {
        Task<IReadOnlyList<StaffRowOverview>> GetOverviewAsync();
        Task<IReadOnlyList<string>> GetUnitsAsync();
        Task<IReadOnlyList<StaffPickerPerson>> GetPermanentStaffForPickerAsync();

        /// <summary>Вужчий список - лише курсові офіцери. Дропліст
        /// {{курсовий_офіцер}} не має права показувати весь постійний склад:
        /// це повернуло б випадковий вибір, від якого й ішли.</summary>
        Task<IReadOnlyList<StaffPickerPerson>> GetCourseOfficersForPickerAsync();
        Task IssueDocumentsAsync(
            StaffEventKind kind, IReadOnlyList<int> recipientIds, IReadOnlyList<int> templateIds,
            DateOnly dateStart, DateOnly dateEnd, string? note, Dictionary<string, string> manualValues);
        Task<int> GenerateDocumentsAsync(
            IReadOnlyList<int> recipientIds, IReadOnlyList<int> templateIds, Dictionary<string, string> manualValues);
        Task<Dictionary<string, string>> GetLastManualValuesAsync();
        Task DeleteManyAsync(IReadOnlyList<int> recipientIds);
    }
}
