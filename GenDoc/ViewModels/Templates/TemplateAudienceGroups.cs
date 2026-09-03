using System.Collections;
using System.Windows.Data;
using GenDoc.Models.Enums;

namespace GenDoc.ViewModels.Templates
{
    public static class TemplateAudienceGroups
    {
        public static ListCollectionView Intake(IList source) => new(source)
        {
            Filter = o => AudienceOf(o) != TemplateAudience.PermanentStaff
        };

        public static ListCollectionView PermanentStaff(IList source) => new(source)
        {
            Filter = o => AudienceOf(o) == TemplateAudience.PermanentStaff
        };

        private static TemplateAudience AudienceOf(object? item) =>
            item is DocxTemplateListItemViewModel t ? t.Audience : TemplateAudience.Intake;
    }
}
