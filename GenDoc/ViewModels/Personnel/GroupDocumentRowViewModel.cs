using GenDoc.Services.Completeness;

namespace GenDoc.ViewModels.Personnel
{
    // Рядок підвалу «Групові відомості пакета» у картці особи. v25: стан - про
    // УЧАСТЬ саме цієї людини, а не про сам факт існування документа.
    public class GroupDocumentRowViewModel
    {
        public GroupDocumentRowViewModel(PackageGroupDocumentStatus status)
        {
            TemplateName = status.TemplateName;
            GroupDocumentId = status.GroupDocumentId;
            StateText = status.GroupDocumentId is null
                ? "ще не сформовано"
                : status.RosterUnknown
                    ? "склад не записано (згенеровано до оновлення) - перегенеруйте"
                    : status.IsParticipant
                        ? $"у складі, в.{status.Version}"
                        : $"не входить до чинного складу (в.{status.Version})";
        }

        public string TemplateName { get; }
        public int? GroupDocumentId { get; }
        public string StateText { get; }
        public bool CanOpen => GroupDocumentId is not null;
    }
}
