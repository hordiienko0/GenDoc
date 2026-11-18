using GenDoc.Models;

namespace GenDoc.Services.Intakes
{
    public record IntakeCreateRequest(
        string DisplayNumber,
        int BaseNodeId,
        DateOnly DateStart,
        DateOnly DateEnd);

    /// <summary>Нові значення всіх полів набору: назва (вона ж - назва кореневої папки), папка-основа, період.</summary>
    public record IntakeUpdateRequest(
        int IntakeId,
        string DisplayNumber,
        int BaseNodeId,
        DateOnly DateStart,
        DateOnly DateEnd);

    /// <summary>Збережений набір і, якщо щось пішло не так із текою документів на диску, попередження для користувача.</summary>
    public record IntakeUpdateResult(Intake Intake, string? Warning = null);

    public interface IIntakeService
    {
        Task<Intake?> GetActiveAsync();
        Task<Intake?> GetByIdAsync(int id);
        Task<int> GetNextNumberAsync();
        Task<string> GetNumberTemplateAsync();
        Task<Intake> CreateAsync(IntakeCreateRequest request);
        Task<IntakeUpdateResult> UpdateAsync(IntakeUpdateRequest request);

        /// <summary>Папка, у якій лежить коренева папка набору.</summary>
        Task<int?> GetBaseNodeIdAsync(int intakeId);

        Task<IReadOnlyList<IntakeOverview>> GetOverviewsAsync();
        Task<IReadOnlyList<int>> GetYearsAsync();
        Task<IntakeCloseInfo> GetCloseInfoAsync(int intakeId);
        Task CloseAsync(IntakeCloseRequest request);
        Task<string?> GetReopenConflictAsync(int intakeId);
        Task ReopenAsync(int intakeId);
    }
}
