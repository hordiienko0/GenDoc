using GenDoc.Models;

namespace GenDoc.Services.Intakes
{
    public record IntakeCreateRequest(
        string DisplayNumber,
        int BaseNodeId,
        DateOnly DateStart,
        DateOnly DateEnd,
        IReadOnlyList<string> Subfolders);

    public interface IIntakeService
    {
        Task<Intake?> GetActiveAsync();
        Task<Intake?> GetByIdAsync(int id);
        Task<int> GetNextNumberAsync();
        Task<string> GetNumberTemplateAsync();
        Task<Intake> CreateAsync(IntakeCreateRequest request);
    }
}
