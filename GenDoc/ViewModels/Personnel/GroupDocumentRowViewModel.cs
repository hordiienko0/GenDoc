using GenDoc.Models.Enums;
using GenDoc.Services.Completeness;

namespace GenDoc.ViewModels.Personnel
{
    public class GroupDocumentRowViewModel
    {
        public GroupDocumentRowViewModel(PackageGroupDocumentStatus status)
        {
            TemplateName = status.TemplateName;
            GroupDocumentId = status.GroupDocumentId;
            Requirement = status.Requirement;
            IsGap = status.Requirement == TemplateRequirement.Required && !status.IsParticipant;

            var label = RequirementLabels.Of(status.Requirement);
            const string notApplicable = "не застосовується для цієї категорії";
            StateText = status.GroupDocumentId is null
                ? status.Requirement == TemplateRequirement.NotApplicable ? notApplicable : $"ще не сформовано · {label}"
                : status.RosterUnknown
                    ? "склад не записано (згенеровано до оновлення) - перегенеруйте"
                    : status.IsParticipant
                        ? $"у складі, в.{status.Version}"
                        : status.Requirement == TemplateRequirement.NotApplicable
                            ? notApplicable
                            : $"не входить до чинного складу (в.{status.Version}) · {label}";
        }

        public string TemplateName { get; }
        public int? GroupDocumentId { get; }
        public TemplateRequirement Requirement { get; }
        public bool IsGap { get; }
        public string StateText { get; }
        public bool CanOpen => GroupDocumentId is not null;
    }
}
