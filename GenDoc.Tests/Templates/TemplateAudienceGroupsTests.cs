using System.Collections.ObjectModel;
using GenDoc.Models.Enums;
using GenDoc.ViewModels.Templates;

namespace GenDoc.Tests.Templates;

public class TemplateAudienceGroupsTests
{
    private static DocxTemplateListItemViewModel Row(int id, string name, TemplateAudience audience) =>
        new(id, name, shortName: null, originalFileName: $"{name}.docx",
            uploadedAt: new DateTime(2026, 8, 17), tagCount: 3, isFromBuilder: false, audience: audience);

    private static ObservableCollection<DocxTemplateListItemViewModel> Sample() => new()
    {
        Row(1, "Рапорт котлове", TemplateAudience.Intake),
        Row(2, "Атестація офіцера", TemplateAudience.PermanentStaff),
        Row(3, "Довідка про склад сім'ї", TemplateAudience.Intake)
    };

    [Fact]
    public void Intake_ShowsOnlyTemplatesForIntakes()
    {
        var view = TemplateAudienceGroups.Intake(Sample());

        var names = view.Cast<DocxTemplateListItemViewModel>().Select(t => t.Name).ToList();

        Assert.Equal(new[] { "Рапорт котлове", "Довідка про склад сім'ї" }, names);
    }

    [Fact]
    public void PermanentStaff_ShowsOnlyTemplatesForPermanentStaff()
    {
        var view = TemplateAudienceGroups.PermanentStaff(Sample());

        var row = Assert.Single(view.Cast<DocxTemplateListItemViewModel>());

        Assert.Equal("Атестація офіцера", row.Name);
    }

    [Fact]
    public void SwitchingAudience_MovesTheSameRowBetweenGroups()
    {
        var source = Sample();
        var intake = TemplateAudienceGroups.Intake(source);
        var permanent = TemplateAudienceGroups.PermanentStaff(source);

        var moved = source.First(t => t.Name == "Рапорт котлове");
        moved.Audience = TemplateAudience.PermanentStaff;

        intake.Refresh();
        permanent.Refresh();

        Assert.DoesNotContain(intake.Cast<DocxTemplateListItemViewModel>(), t => ReferenceEquals(t, moved));
        Assert.Contains(permanent.Cast<DocxTemplateListItemViewModel>(), t => ReferenceEquals(t, moved));
    }

    [Fact]
    public void Groups_TogetherCoverEveryRowExactlyOnce()
    {
        var source = Sample();
        var intake = TemplateAudienceGroups.Intake(source).Cast<DocxTemplateListItemViewModel>().ToList();
        var permanent = TemplateAudienceGroups.PermanentStaff(source).Cast<DocxTemplateListItemViewModel>().ToList();

        Assert.Equal(source.Count, intake.Count + permanent.Count);
        Assert.Empty(intake.Intersect(permanent));
    }
}
