using GenDoc.Models.Enums;
using GenDoc.Services.Completeness;
using GenDoc.ViewModels.Completeness;

namespace GenDoc.Tests;

// MatrixTemplateInfo.LinkId - не-nullable int, тож щойно доданий у діалозі шаблон
// приходить із сентинелом 0. Якщо він не перетвориться на null, збереження бере
// гілку «оновити наявний зв'язок» і падає з "Sequence contains no matching element"
// на пошуку зв'язку з Id = 0.
public class RequirementTemplateRowViewModelTests
{
    [Fact]
    public void LinkId_NewlyAddedTemplateWithSentinelZero_BecomesNull()
    {
        var row = new RequirementTemplateRowViewModel(
            new MatrixTemplateInfo(0, TemplateId: 42, Name: "Рапорт", ShortName: null, SortOrder: 0,
                TemplateRequirement.Required, TemplateRequirement.Required),
            hasDocuments: false);

        Assert.Null(row.LinkId);
    }

    [Fact]
    public void LinkId_ExistingLink_IsPreserved()
    {
        var row = new RequirementTemplateRowViewModel(
            new MatrixTemplateInfo(17, TemplateId: 42, Name: "Рапорт", ShortName: null, SortOrder: 3,
                TemplateRequirement.Optional, TemplateRequirement.NotApplicable),
            hasDocuments: true);

        Assert.Equal(17, row.LinkId);
        Assert.Equal(42, row.TemplateId);
        Assert.Equal(3, row.SortOrder);
        Assert.True(row.HasDocuments);
        Assert.Equal(TemplateRequirement.Optional, row.Regular);
        Assert.Equal(TemplateRequirement.NotApplicable, row.Limited);
    }
}
