using System.Collections.ObjectModel;
using GenDoc.Models.Enums;
using GenDoc.ViewModels.Templates;

namespace GenDoc.Tests.Templates;

// Поділ за аудиторією працював у ДАНИХ (Template.Audience, схема v23) - «Видати
// документ» показує лише шаблони постійного складу, - але на екрані обидві
// аудиторії лежали одним списком, і зрозуміти, який шаблон куди піде, було ніяк.
//
// Групи - це ВИДИ над однією колекцією, тому рядок при переході між ними
// лишається тим самим об'єктом: вибір і завантажений мапінг не губляться.
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

    // Головне, заради чого групи зроблені видами: перемикання аудиторії
    // переносить рядок з однієї групи в іншу, і це ТОЙ САМИЙ об'єкт - тобто
    // вибір і завантажений мапінг переїжджають разом із ним.
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
