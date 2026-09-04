using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Generation;

public class RecipientCountMatchesRosterTests
{
    private static (int ActiveIntakeId, int OtherIntakeId) Seed(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        var intake = new Intake { Number = 1, DisplayNumber = "Набір №1" };
        var other = new Intake { Number = 2, DisplayNumber = "Набір №2" };
        ctx.Intakes.AddRange(intake, other);
        ctx.SaveChanges();

        ctx.Recipients.AddRange(
            new Recipient { LastName = "А", FirstName = "А", Rank = "майор", IntakeId = intake.Id },
            new Recipient { LastName = "Б", FirstName = "Б", Rank = "капітан", IntakeId = intake.Id },
            new Recipient { LastName = "В", FirstName = "В", Rank = "солдат", IntakeId = intake.Id },
            new Recipient { LastName = "Г", FirstName = "Г", Rank = "солдат", IntakeId = intake.Id },
            new Recipient { LastName = "Д", FirstName = "Д", Rank = "солдат", IntakeId = intake.Id },
            new Recipient { LastName = "Е", FirstName = "Е", Rank = "підполковник", IntakeId = null },
            new Recipient { LastName = "Є", FirstName = "Є", Rank = "лейтенант", IntakeId = other.Id });
        ctx.SaveChanges();

        return (intake.Id, other.Id);
    }

    [Fact]
    public void Count_WithoutActiveIntake_IsEveryone()
    {
        using var db = new TestDb();
        Seed(db);

        Assert.Equal(7, TestServices.Generation(db).GetRecipientCount(RosterSelection.Everyone));
    }

    [Fact]
    public void Count_WithActiveIntake_ExcludesPermanentStaffAndOtherIntakes()
    {
        using var db = new TestDb();
        var (activeIntakeId, _) = Seed(db);

        var selection = RosterSelection.Everyone with { IntakeId = activeIntakeId };

        Assert.Equal(5, TestServices.Generation(db).GetRecipientCount(selection));
    }

    [Fact]
    public void Count_WithActiveIntakeAndPermanentStaff_AddsStaffButNotOtherIntakes()
    {
        using var db = new TestDb();
        var (activeIntakeId, _) = Seed(db);

        var selection = RosterSelection.Everyone with { IntakeId = activeIntakeId, IncludePermanentStaff = true };

        Assert.Equal(6, TestServices.Generation(db).GetRecipientCount(selection));
    }

    [Fact]
    public void Count_WithActiveIntakeAndRankFilter_CombinesWithAnd()
    {
        using var db = new TestDb();
        var (_, otherIntakeId) = Seed(db);

        var selection = RosterSelection.Everyone with { IntakeId = otherIntakeId, Ranks = new[] { "солдат" } };

        Assert.Equal(0, TestServices.Generation(db).GetRecipientCount(selection));
    }

    [Fact]
    public void Count_WithRankFilter_MatchesTheFilteredRoster()
    {
        using var db = new TestDb();
        Seed(db);

        var selection = RosterSelection.Everyone with { Ranks = new[] { "солдат" } };

        Assert.Equal(3, TestServices.Generation(db).GetRecipientCount(selection));
    }

    [Fact]
    public void Count_WithPermanentStaffOnly_MatchesTheFilteredRoster()
    {
        using var db = new TestDb();
        Seed(db);

        var selection = RosterSelection.Everyone with { PermanentStaffOnly = true };

        Assert.Equal(1, TestServices.Generation(db).GetRecipientCount(selection));
    }

    [Fact]
    public void Count_WithExplicitSelection_CountsOnlyThoseChecked()
    {
        using var db = new TestDb();
        Seed(db);

        int[] ids;
        using (var ctx = db.Factory.CreateDbContext())
            ids = ctx.Recipients.OrderBy(r => r.Id).Take(2).Select(r => r.Id).ToArray();

        var selection = new RosterSelection(
            AllRecipients: false, RecipientIds: ids, FitnessFilter.All,
            PermanentStaffOnly: false, Array.Empty<RankCategory>(), Array.Empty<string>());

        Assert.Equal(2, TestServices.Generation(db).GetRecipientCount(selection));
    }

    [Fact]
    public void Count_WithExplicitSelection_KeepsCheckedPeopleOutsideTheIntake()
    {
        using var db = new TestDb();
        var (activeIntakeId, _) = Seed(db);

        int[] ids;
        using (var ctx = db.Factory.CreateDbContext())
            ids = ctx.Recipients.Where(r => r.IntakeId != activeIntakeId).Select(r => r.Id).ToArray();

        var selection = new RosterSelection(
            AllRecipients: false, RecipientIds: ids, FitnessFilter.All,
            PermanentStaffOnly: false, Array.Empty<RankCategory>(), Array.Empty<string>(),
            IntakeId: activeIntakeId);

        Assert.Equal(2, TestServices.Generation(db).GetRecipientCount(selection));
    }

    [Fact]
    public void Count_CombinesFiltersWithAnd()
    {
        using var db = new TestDb();
        Seed(db);

        var selection = RosterSelection.Everyone with
        {
            PermanentStaffOnly = true,
            Ranks = new[] { "солдат" }
        };

        Assert.Equal(0, TestServices.Generation(db).GetRecipientCount(selection));
    }
}
