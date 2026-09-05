using GenDoc.Models;

namespace GenDoc.Services.Intakes
{
    public record IntakeCreateRequest(
        string DisplayNumber,
        int BaseNodeId,
        DateOnly DateStart,
        DateOnly DateEnd);

    public interface IIntakeService
    {
        Task<Intake?> GetActiveAsync();
        Task<Intake?> GetByIdAsync(int id);
        Task<int> GetNextNumberAsync();
        Task<string> GetNumberTemplateAsync();
        Task<Intake> CreateAsync(IntakeCreateRequest request);

        Task<IReadOnlyList<IntakeOverview>> GetOverviewsAsync();
        Task<IReadOnlyList<int>> GetYearsAsync();
        Task<IntakeCloseInfo> GetCloseInfoAsync(int intakeId);
        Task CloseAsync(IntakeCloseRequest request);
        Task<string?> GetReopenConflictAsync(int intakeId);
        Task ReopenAsync(int intakeId);
    }
}
