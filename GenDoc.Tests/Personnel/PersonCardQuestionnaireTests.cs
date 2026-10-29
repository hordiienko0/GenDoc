using GenDoc.Models;
using GenDoc.Services.Personnel;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Personnel;

namespace GenDoc.Tests.Personnel;

public class PersonCardQuestionnaireTests
{
    private static PersonnelService Personnel(TestDb db)
        => new(db.Factory, new FakeAuditLog(), new FakeCurrentUser());

    private static int SeedPerson(TestDb db, Action<Recipient>? fill = null)
    {
        using var ctx = db.Factory.CreateDbContext();
        var root = new OrgNode { Name = "Курс", Depth = 0, SortOrder = 0, Path = "/" };
        ctx.OrgNodes.Add(root);
        ctx.SaveChanges();
        root.Path = $"/{root.Id}/";
        var person = new Recipient
        {
            LastName = "ТЕСТЕНКО", FirstName = "Петро", MiddleName = "Іванович",
            Rank = "молодший лейтенант", OrgNodeId = root.Id,
            DateOfBirth = new DateOnly(1990, 3, 14), Phone = "0000000001", TaxNumber = "0000000000"
        };
        fill?.Invoke(person);
        ctx.Recipients.Add(person);
        ctx.SaveChanges();
        return person.Id;
    }

    private static async Task<PersonCardViewModel> OpenCardAsync(TestDb db, int id)
    {
        var personnel = Personnel(db);
        var model = await personnel.GetForEditAsync(id);
        return new PersonCardViewModel(personnel, null!, null!, null!, null!, null!, null!, null!, model!, "Курс");
    }

    private static QuestionnaireFieldViewModel Field(PersonCardViewModel card, string key)
        => card.QuestionnaireFields.Single(f => f.Key == key);

    private static Recipient Load(TestDb db, int id)
    {
        using var ctx = db.Factory.CreateDbContext();
        return ctx.Recipients.Single(r => r.Id == id);
    }

    [Fact]
    public async Task Card_ShowsTheFormInDocumentOrder()
    {
        using var db = new TestDb();
        var card = await OpenCardAsync(db, SeedPerson(db));

        var visible = card.QuestionnaireFields.Where(f => f.IsVisible).Select(f => f.Label).ToList();
        Assert.Equal(new[]
        {
            "Дата народження", "Національність", "Освіта цивільна", "Освіта військова (ВОС)", "ВОС",
            "З якого часу в ЗСУ", "Яким РТЦК та СП призваний", "Сімейний стан", "Адреса реєстрації",
            "Адреса фактичного проживання", "Телефон", "Примітка", "ІПН", "Паспорт", "Місце народження",
            "Довідка ВЛК (№, дата)", "Наказ про присвоєння в/звання", "ПІБ на іноземній мові",
            "Служив/не служив", "Примітка (2)"
        }, visible);
        Assert.Equal("14.03.1990", Field(card, "DateOfBirth").DisplayValue);
        Assert.Equal("-", Field(card, "Passport").DisplayValue);
    }

    [Fact]
    public async Task FieldsOfOlderForms_AreShownOnlyWhenFilled()
    {
        using var db = new TestDb();
        var card = await OpenCardAsync(db, SeedPerson(db, r => r.OriginUnit = "А0000"));

        Assert.True(Field(card, "OriginUnit").IsVisible);
        Assert.False(Field(card, "FoodCertificate").IsVisible);
    }

    [Fact]
    public async Task EditingAField_SavesItAndLeavesTheRestAlone()
    {
        using var db = new TestDb();
        var id = SeedPerson(db, r => r.OriginUnit = "А0000");
        var card = await OpenCardAsync(db, id);
        card.EditCommand.Execute(null);
        Assert.True(Field(card, "Passport").IsEditing);

        Field(card, "Passport").Value = "паспорт ТТ 000000";
        Field(card, "ArmedForcesSince").Value = "15.10.2026";
        Assert.True(card.IsDirty);

        Assert.True(await card.SaveAsync());

        var saved = Load(db, id);
        Assert.Equal("паспорт ТТ 000000", saved.Passport);
        Assert.Equal(new DateOnly(2026, 10, 15), saved.ArmedForcesSince);
        Assert.Equal("0000000001", saved.Phone);
        Assert.Equal("0000000000", saved.TaxNumber);
        Assert.Equal("А0000", saved.OriginUnit);
        Assert.Equal(new DateOnly(1990, 3, 14), saved.DateOfBirth);
        Assert.False(card.IsDirty);
    }

    [Fact]
    public async Task UnreadableDate_IsShownOnTheFieldAndNothingIsSaved()
    {
        using var db = new TestDb();
        var id = SeedPerson(db);
        var card = await OpenCardAsync(db, id);
        card.EditCommand.Execute(null);

        Field(card, "Phone").Value = "0000000009";
        Field(card, "ArmedForcesSince").Value = "осінь 2026";

        Assert.False(await card.SaveAsync());
        Assert.Equal(PersonQuestionnaire.DateError, Field(card, "ArmedForcesSince").Error);
        Assert.Equal("0000000001", Load(db, id).Phone);
    }

    [Fact]
    public async Task Cancel_RevertsQuestionnaireEdits()
    {
        using var db = new TestDb();
        var card = await OpenCardAsync(db, SeedPerson(db));
        card.EditCommand.Execute(null);

        Field(card, "Phone").Value = "0000000009";
        card.CancelCommand.Execute(null);

        Assert.Equal("0000000001", Field(card, "Phone").Value);
        Assert.False(card.IsDirty);
        Assert.False(Field(card, "Phone").IsEditing);
    }

    [Fact]
    public async Task SaveWithoutQuestionnaire_DoesNotWipeIt()
    {
        using var db = new TestDb();
        var id = SeedPerson(db);
        var model = await Personnel(db).GetForEditAsync(id);
        model!.Questionnaire = null;
        model.Position = "курсант";

        Assert.True((await Personnel(db).SaveAsync(model)).Success);

        var saved = Load(db, id);
        Assert.Equal("курсант", saved.Position);
        Assert.Equal("0000000001", saved.Phone);
        Assert.Equal(new DateOnly(1990, 3, 14), saved.DateOfBirth);
    }
}
