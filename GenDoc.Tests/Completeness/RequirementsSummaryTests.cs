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
        Assert.Equal("Для набору №4: придатні - 1 обов'язковий, 1 опційний · обмежено придатні - 2 обов'язкових, 0 опційних", text);
    }

    [Fact]
    public void GroupRow_KeepsItsRequirementAndIsFlagged()
    {
        var row = new RequirementTemplateRowViewModel(
            new MatrixTemplateInfo(3, 42, "Рапорт ГРУПОВИЙ", null, 0, TemplateRequirement.Required, TemplateRequirement.Optional, IsGroup: true),
            hasDocuments: false);

        Assert.True(row.IsGroup);
        Assert.Equal(TemplateRequirement.Required, row.Regular);
        Assert.Equal(TemplateRequirement.Optional, row.Limited);
    }
}
