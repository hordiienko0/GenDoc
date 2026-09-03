using GenDoc.ViewModels.Generation;

namespace GenDoc.Services.Generation
{
    public interface IManualTagFormBuilder
    {
        Task<ManualTagFormViewModel> BuildAsync(
            IReadOnlyList<string> tags, string contextKey, bool needsCourseOfficer = false);

        Task SaveAsync(string contextKey, ManualTagFormViewModel form);
    }
}
