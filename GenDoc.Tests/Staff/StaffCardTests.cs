using GenDoc.Models;
using GenDoc.Services.Recipients;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Staff;

namespace GenDoc.Tests.Staff;

// Постійний склад не редагувався ВЗАГАЛІ: розділ мав лише «+ Додати», і той
// відкривав анкету прикомандированого на три десятки полів, з яких офіцерові
// підходили чотири. Профіль, заведений при вході, лишався з самим прізвищем -
// дописати звання не було де (вимога користувача 2026-08-31).
public class StaffCardTests
{
    private static StaffCardViewModel NewCard(TestDb db)
        => new(new RecipientService(db.Factory, new FakeAuditLog(), new FakeCurrentUser()));

    private static int SeedStaff(TestDb db, string lastName = "ПЕТРЕНКО", bool courseOfficer = false)
    {
        using var ctx = db.Factory.CreateDbContext();
        var person = new Recipient
        {
            LastName = lastName,
            FirstName = "Іван",
            MiddleName = "Іванович",
            Rank = "майор",
            Position = "курсовий офіцер",
            ServiceNumber = "АА-123456",
            Phone = "0501234567",
            IsCourseOfficer = courseOfficer
        };
        ctx.Recipients.Add(person);
        ctx.SaveChanges();
        return person.Id;
    }

    private static Recipient Reload(TestDb db, int id)
    {
        using var ctx = db.Factory.CreateDbContext();
        return ctx.Recipients.Single(r => r.Id == id);
    }

    // ─── Створення ───────────────────────────────────────────────────────────

    [Fact]
    public void ANewCardOpensInEditModeAndIsClean()
    {
        using var db = new TestDb();
        var card = NewCard(db);

        card.StartNew();

        Assert.True(card.IsNew);
        Assert.True(card.IsEditing);
        Assert.False(card.IsDirty);
    }

    // Розділ веде курсових офіцерів, тож ознака стоїть одразу: зняти її - один
    // клік, а забути поставити легко.
    [Fact]
    public void ANewCardIsACourseOfficerByDefault()
    {
        using var db = new TestDb();
        var card = NewCard(db);

        card.StartNew();

        Assert.True(card.IsCourseOfficer);
    }

    [Fact]
    public void SavingANewCardCreatesAPermanentStaffMember()
    {
        using var db = new TestDb();
        var card = NewCard(db);
        card.StartNew();
        card.LastName = "ШЕВЧЕНКО";
        card.FirstMiddle = "Тарас Григорович";
        card.Rank = "підполковник";
        card.Position = "курсовий офіцер";
        card.ServiceNumber = "АА-999";
        card.Phone = "0670000000";
        card.UnitName = "1 рота";

        Assert.True(card.TrySave());

        var person = Reload(db, card.Id);
        Assert.Equal("ШЕВЧЕНКО", person.LastName);
        Assert.Equal("Тарас", person.FirstName);
        Assert.Equal("Григорович", person.MiddleName);
        Assert.Equal("підполковник", person.Rank);
        Assert.Equal("0670000000", person.Phone);
        Assert.True(person.IsCourseOfficer);
        // Постійний склад - поза наборами.
        Assert.Null(person.IntakeId);
    }

    [Fact]
    public void SavingLeavesTheCardInReadModeAndClean()
    {
        using var db = new TestDb();
        var card = NewCard(db);
        card.StartNew();
        card.LastName = "ШЕВЧЕНКО";

        card.TrySave();

        Assert.False(card.IsEditing);
        Assert.False(card.IsDirty);
        Assert.False(card.IsNew);
    }

    [Fact]
    public void ACardWithoutASurnameIsNotSaved()
    {
        using var db = new TestDb();
        var card = NewCard(db);
        card.StartNew();
        card.Rank = "майор";

        Assert.False(card.TrySave());
        Assert.NotNull(card.LastNameError);

        using var ctx = db.Factory.CreateDbContext();
        Assert.Empty(ctx.Recipients.ToList());
    }

    // ─── Редагування наявної людини ──────────────────────────────────────────

    [Fact]
    public void LoadingFillsTheFieldsAndOpensInReadMode()
    {
        using var db = new TestDb();
        var id = SeedStaff(db, courseOfficer: true);
        var card = NewCard(db);

        Assert.True(card.Load(id));

        Assert.Equal("ПЕТРЕНКО", card.LastName);
        Assert.Equal("Іван Іванович", card.FirstMiddle);
        Assert.Equal("майор", card.Rank);
        Assert.Equal("0501234567", card.Phone);
        Assert.True(card.IsCourseOfficer);
        Assert.False(card.IsEditing);
        Assert.False(card.IsDirty);
    }

    // Головний випадок з вимоги: профіль лишав саму лише назву, і дописати
    // звання не було де.
    [Fact]
    public void ARankCanBeAddedToACardThatHadOnlyAName()
    {
        using var db = new TestDb();
        int id;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var bare = new Recipient
            {
                LastName = "ПЕТРЕНКО", FirstName = "Іван", MiddleName = "Іванович",
                Rank = string.Empty, Position = string.Empty, ServiceNumber = string.Empty,
                IsCourseOfficer = true
            };
            ctx.Recipients.Add(bare);
            ctx.SaveChanges();
            id = bare.Id;
        }

        var card = NewCard(db);
        card.Load(id);
        card.EditCommand.Execute(null);
        card.Rank = "майор";
        card.Position = "курсовий офіцер";

        Assert.True(card.TrySave());

        var person = Reload(db, id);
        Assert.Equal("майор", person.Rank);
        Assert.Equal("курсовий офіцер", person.Position);
    }

    [Fact]
    public void TheCourseOfficerFlagCanBeRemoved()
    {
        using var db = new TestDb();
        var id = SeedStaff(db, courseOfficer: true);
        var card = NewCard(db);
        card.Load(id);

        card.IsCourseOfficer = false;
        Assert.True(card.TrySave());

        Assert.False(Reload(db, id).IsCourseOfficer);
    }

    [Fact]
    public void EditingMakesTheCardDirty()
    {
        using var db = new TestDb();
        var card = NewCard(db);
        card.Load(SeedStaff(db));

        card.Rank = "підполковник";

        Assert.True(card.IsDirty);
    }

    // Повернення значення руками - знову «чисто»: guard не має питати про
    // зміни, яких уже немає.
    [Fact]
    public void TypingTheValueBackMakesTheCardCleanAgain()
    {
        using var db = new TestDb();
        var card = NewCard(db);
        card.Load(SeedStaff(db));

        card.Rank = "підполковник";
        card.Rank = "майор";

        Assert.False(card.IsDirty);
    }

    [Fact]
    public void CancelRestoresTheStoredValues()
    {
        using var db = new TestDb();
        var card = NewCard(db);
        card.Load(SeedStaff(db));
        card.EditCommand.Execute(null);
        card.Rank = "підполковник";

        card.CancelCommand.Execute(null);

        Assert.Equal("майор", card.Rank);
        Assert.False(card.IsEditing);
        Assert.False(card.IsDirty);
    }

    [Fact]
    public void LoadingAPersonWhoIsGoneReportsFailure()
    {
        using var db = new TestDb();
        Assert.False(NewCard(db).Load(4242));
    }

    // Заголовок картки - те, що бачить курсовий над полями.
    [Fact]
    public void TheHeaderShowsTheNameAndTheRankWithPosition()
    {
        using var db = new TestDb();
        var card = NewCard(db);
        card.Load(SeedStaff(db));

        Assert.Contains("ПЕТРЕНКО", card.HeaderName);
        Assert.Equal("майор · курсовий офіцер", card.HeaderSub);
    }

    [Fact]
    public void AnEmptyNewCardSaysSoInTheHeader()
    {
        using var db = new TestDb();
        var card = NewCard(db);
        card.StartNew();

        Assert.Equal("Нова людина", card.HeaderName);
    }
}
