using GenDoc.ViewModels.Generation;

namespace GenDoc.Services.Generation
{
    public interface IManualTagFormBuilder
    {
        Task<ManualTagFormViewModel> BuildAsync(
            IReadOnlyList<string> tags, string contextKey, bool needsCourseOfficer = false, int? intakeId = null);

        Task SaveAsync(string contextKey, ManualTagFormViewModel form);
    }
}
