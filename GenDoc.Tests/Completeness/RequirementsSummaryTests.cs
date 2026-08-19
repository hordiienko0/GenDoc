using GenDoc.Models.Enums;
using GenDoc.Services.Completeness;
using GenDoc.ViewModels.Completeness;

namespace GenDoc.Tests.Completeness;

public class RequirementsSummaryTests
{
    [Fact]
    public void BuildPreviewText_ReadsAsSentence()
    {
        var text = PackageRequirementsViewModel.BuildPreviewText(4, requiredRegular: 1, optionalRegular: 1, requiredLimited: 2, optionalLimited: 0);
        Assert.Equal("Для набору №4: звичайні - 1 обов'язковий, 1 опційний · обмежено придатні - 2 обов'язкових, 0 опційних", text);
    }

    [Fact]
    public void GroupRow_HasNoRequirementAndIsFlagged()
    {
        var row = new RequirementTemplateRowViewModel(
            new MatrixTemplateInfo(3, 42, "Рапорт ГРУПОВИЙ", null, 0, TemplateRequirement.Required, TemplateRequirement.Required, IsGroup: true),
            hasDocuments: false);

        Assert.True(row.IsGroup);
        Assert.Equal(TemplateRequirement.NotApplicable, row.Regular);
        Assert.Equal(TemplateRequirement.NotApplicable, row.Limited);
    }
}
