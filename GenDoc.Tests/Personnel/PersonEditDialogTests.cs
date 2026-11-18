using GenDoc.Models;
using GenDoc.Services.Personnel;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Personnel;

namespace GenDoc.Tests.Personnel;

public class PersonEditDialogTests
{
    private static int SeedNode(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        var root = new OrgNode { Name = "Курс", Depth = 0, SortOrder = 0, Path = "/" };
        ctx.OrgNodes.Add(root);
        ctx.SaveChanges();
        root.Path = $"/{root.Id}/";
        ctx.SaveChanges();
        return root.Id;
    }

    private static int SeedPerson(TestDb db, int nodeId)
    {
        using var ctx = db.Factory.CreateDbContext();
        var person = new Recipient
        {
            LastName = "Тестенко", FirstName = "Петро", MiddleName = "Іванович",
            Rank = "солдат", Position = "стрілець", OrgNodeId = nodeId, Phone = "0000000001"
        };
        ctx.Recipients.Add(person);
        ctx.SaveChanges();
        return person.Id;
    }

    private static PersonnelService Personnel(TestDb db)
        => new(db.Factory, new FakeAuditLog(), new FakeCurrentUser());

    private static PersonCardViewModel Card(PersonnelService personnel, PersonEditModel model, List<int>? saved = null)
    {
        var card = new PersonCardViewModel(personnel, null!, null!, null!, null!, null!, null!, null!, model, "Курс");
        if (saved is not null) card.Saved += saved.Add;
        return card;
    }

    private static PersonEditDialogViewModel NewDialog(TestDb db, int nodeId, List<int>? saved = null)
    {
        var personnel = Personnel(db);
        return PersonEditDialogViewModel.ForNew(
            () => Card(personnel, new PersonEditModel { OrgNodeId = nodeId }, saved), "Курс");
    }

    private static async Task<PersonEditDialogViewModel> EditDialogAsync(TestDb db, int personId, List<int>? saved = null)
    {
        var personnel = Personnel(db);
        var model = await personnel.GetForEditAsync(personId);
        return PersonEditDialogViewModel.ForExisting(Card(personnel, model!, saved), "Курс");
    }

    private static List<string> LastNames(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        return ctx.Recipients.OrderBy(r => r.Id).Select(r => r.LastName).ToList();
    }

    private static Recipient Load(TestDb db, int id)
    {
        using var ctx = db.Factory.CreateDbContext();
        return ctx.Recipients.Single(r => r.Id == id);
    }

    [Fact]
    public async Task Save_StoresPersonAndClosesWithResult()
    {
        using var db = new TestDb();
        var saved = new List<int>();
        var dialog = NewDialog(db, SeedNode(db), saved);
        var closed = false;
        dialog.RequestClose += (_, _) => closed = true;

        dialog.Card.LastName = "Тестенко";
        dialog.Card.FirstMiddle = "Петро Іванович";
        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.True(closed);
        Assert.True(dialog.DialogResultValue);
        Assert.Single(saved);
        Assert.Equal(new[] { "Тестенко" }, LastNames(db));
    }

    [Fact]
    public async Task SaveAndNext_KeepsWindowOpenWithCleanCard()
    {
        using var db = new TestDb();
        var dialog = NewDialog(db, SeedNode(db));
        var closed = false;
        var replaced = 0;
        dialog.RequestClose += (_, _) => closed = true;
        dialog.CardReplaced += () => replaced++;

        var first = dialog.Card;
        first.LastName = "Перший";
        await dialog.SaveAndNextCommand.ExecuteAsync(null);

        Assert.False(closed);
        Assert.Equal(1, replaced);
        Assert.NotSame(first, dialog.Card);
        Assert.True(dialog.Card.IsNew);
        Assert.True(string.IsNullOrEmpty(dialog.Card.LastName));
        Assert.False(dialog.NeedsCloseConfirmation);
        Assert.Equal(1, dialog.SavedCount);
        Assert.Contains("ПЕРШИЙ", dialog.AddedNote);

        dialog.Card.LastName = "Другий";
        await dialog.SaveAndNextCommand.ExecuteAsync(null);

        Assert.Equal(2, dialog.SavedCount);
        Assert.Equal(new[] { "Перший", "Другий" }, LastNames(db));
    }

    [Fact]
    public async Task Save_WithoutLastName_StaysOpenAndReportsError()
    {
        using var db = new TestDb();
        var dialog = NewDialog(db, SeedNode(db));
        var closed = false;
        var failed = 0;
        dialog.RequestClose += (_, _) => closed = true;
        dialog.SaveFailed += () => failed++;

        dialog.Card.FirstMiddle = "Петро";
        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.False(closed);
        Assert.Equal(1, failed);
        Assert.NotNull(dialog.Card.LastNameError);
        Assert.True(dialog.NeedsCloseConfirmation);
        Assert.Empty(LastNames(db));
    }

    [Fact]
    public void Cancel_WithNothingAdded_ClosesWithoutResult()
    {
        using var db = new TestDb();
        var dialog = NewDialog(db, SeedNode(db));

        dialog.CancelCommand.Execute(null);

        Assert.False(dialog.DialogResultValue);
        Assert.Null(dialog.AddedNote);
    }

    [Fact]
    public async Task Edit_ShowsPersonAndHasNoAddMore()
    {
        using var db = new TestDb();
        var dialog = await EditDialogAsync(db, SeedPerson(db, SeedNode(db)));

        Assert.False(dialog.IsAddMode);
        Assert.False(dialog.SaveAndNextCommand.CanExecute(null));
        Assert.Equal("Редагування особи", dialog.Heading);
        Assert.Equal("ТЕСТЕНКО П.І. · солдат · стрілець", dialog.PersonLine);
        Assert.Equal("Тестенко", dialog.Card.LastName);
        Assert.Equal("Петро Іванович", dialog.Card.FirstMiddle);
        Assert.Equal("0000000001", dialog.Card.QuestionnaireFields.Single(f => f.Key == "Phone").Value);
        Assert.False(dialog.NeedsCloseConfirmation);
    }

    [Fact]
    public async Task Edit_Save_UpdatesPersonAndCloses()
    {
        using var db = new TestDb();
        var id = SeedPerson(db, SeedNode(db));
        var saved = new List<int>();
        var dialog = await EditDialogAsync(db, id, saved);
        var closed = false;
        dialog.RequestClose += (_, _) => closed = true;

        dialog.Card.Rank = "старший солдат";
        dialog.Card.QuestionnaireFields.Single(f => f.Key == "Phone").Value = "0000000002";
        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.True(closed);
        Assert.True(dialog.DialogResultValue);
        Assert.Equal(new[] { id }, saved);
        var person = Load(db, id);
        Assert.Equal("старший солдат", person.Rank);
        Assert.Equal("0000000002", person.Phone);
        Assert.Null(dialog.AddedNote);
    }

    [Fact]
    public async Task Edit_SaveWithoutChanges_ClosesWithoutSaving()
    {
        using var db = new TestDb();
        var saved = new List<int>();
        var dialog = await EditDialogAsync(db, SeedPerson(db, SeedNode(db)), saved);
        var closed = false;
        dialog.RequestClose += (_, _) => closed = true;

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.True(closed);
        Assert.False(dialog.DialogResultValue);
        Assert.Empty(saved);
    }

    [Fact]
    public async Task Edit_InvalidDate_StaysOpenAndKeepsPersonUnchanged()
    {
        using var db = new TestDb();
        var id = SeedPerson(db, SeedNode(db));
        var dialog = await EditDialogAsync(db, id);
        var failed = 0;
        dialog.SaveFailed += () => failed++;

        dialog.Card.Rank = "сержант";
        dialog.Card.QuestionnaireFields.Single(f => f.Key == "DateOfBirth").Value = "31.02";
        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.Equal(1, failed);
        Assert.Equal(PersonQuestionnaire.DateError, dialog.Card.QuestionnaireFields.Single(f => f.Key == "DateOfBirth").Error);
        Assert.Equal("солдат", Load(db, id).Rank);
        Assert.True(dialog.NeedsCloseConfirmation);
    }
}
