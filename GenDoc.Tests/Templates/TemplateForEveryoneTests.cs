using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Templates;

namespace GenDoc.Tests.Templates;

/// <summary>
/// Шаблон «для всіх»: захворіти може і слухач БЗВП, і курсовий офіцер, тому рапорти про
/// хворобу мають бути доступні й у наборі, і в постійному складі — без другої копії файлу.
/// </summary>
public class TemplateForEveryoneTests
{
    private static int SeedTemplate(TestDb db, TemplateAudience audience, TemplateKind kind = TemplateKind.PerRecipient)
    {
        using var ctx = db.Factory.CreateDbContext();
        var template = new Template
        {
            Name = "Шаблон_Рапорт про хворобу",
            OriginalFileName = "sick.docx",
            Content = Array.Empty<byte>(),
            UploadedAt = DateTime.Now,
            Kind = kind,
            Audience = audience
        };
        ctx.Templates.Add(template);
        ctx.SaveChanges();
        return template.Id;
    }

    [Theory]
    [InlineData(TemplateAudience.Intake)]
    [InlineData(TemplateAudience.PermanentStaff)]
    public void TemplateForEveryone_IsOfferedToBothAudiences(TemplateAudience asked)
    {
        using var db = new TestDb();
        var id = SeedTemplate(db, TemplateAudience.Everyone);

        var offered = TestServices.Generation(db).GetPerRecipientTemplates(asked);

        Assert.Contains(offered, t => t.Id == id);
    }

    [Theory]
    [InlineData(TemplateAudience.Intake)]
    [InlineData(TemplateAudience.PermanentStaff)]
    public void TemplateForEveryone_IsAlsoInTheFullTemplateList(TemplateAudience asked)
    {
        using var db = new TestDb();
        var id = SeedTemplate(db, TemplateAudience.Everyone, TemplateKind.Group);

        var offered = TestServices.Generation(db).GetAllTemplates(asked);

        Assert.Contains(offered, t => t.Id == id);
    }

    [Fact]
    public void IntakeOnlyTemplate_StaysOutOfThePermanentStaffList()
    {
        using var db = new TestDb();
        var id = SeedTemplate(db, TemplateAudience.Intake);

        var offered = TestServices.Generation(db).GetPerRecipientTemplates(TemplateAudience.PermanentStaff);

        Assert.DoesNotContain(offered, t => t.Id == id);
    }

    [Fact]
    public void TemplateForEveryone_ShowsInBothGroupsOfTheTemplatesScreen()
    {
        var item = new DocxTemplateListItemViewModel(
            1, "Шаблон_Рапорт про хворобу", null, "sick.docx", DateTime.Now, 0, false,
            TemplateAudience.Everyone, TemplateKind.PerRecipient);

        Assert.True(TemplateAudienceGroups.Intake(new[] { item }).Filter!(item));
        Assert.True(TemplateAudienceGroups.PermanentStaff(new[] { item }).Filter!(item));
    }

    [Fact]
    public void AudienceCaption_NamesTheEveryoneOption()
    {
        var item = new DocxTemplateListItemViewModel(
            1, "Шаблон_Рапорт про хворобу", null, "sick.docx", DateTime.Now, 0, false,
            TemplateAudience.Everyone, TemplateKind.PerRecipient);

        Assert.Equal("для всіх", item.AudienceCaption);
    }
}
