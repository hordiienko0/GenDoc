using GenDoc.Services.Completeness;

namespace GenDoc.ViewModels.Personnel
{
    // Рядок підвалу «Групові відомості пакета» у картці особи.
    public class GroupDocumentRowViewModel
    {
        public GroupDocumentRowViewModel(PackageGroupDocumentStatus status)
        {
            TemplateName = status.TemplateName;
            GroupDocumentId = status.GroupDocumentId;
            StateText = status.GroupDocumentId is null ? "ще не сформовано" : $"є, в.{status.Version}";
        }

        public string TemplateName { get; }
        public int? GroupDocumentId { get; }
        public string StateText { get; }
        public bool CanOpen => GroupDocumentId is not null;
    }
}
