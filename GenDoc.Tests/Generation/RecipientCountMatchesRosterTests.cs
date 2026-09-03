using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Generation;

public class RecipientCountMatchesRosterTests
{
    private static void Seed(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        var intake = new Intake { Number = 1, DisplayNumber = "Набір №1" };
        ctx.Intakes.Add(intake);
        ctx.SaveChanges();

        ctx.Recipients.AddRange(
            new Recipient { LastName = "А", FirstName = "А", Rank = "майор", IntakeId = intake.Id },
            new Recipient { LastName = "Б", FirstName = "Б", Rank = "капітан", IntakeId = intake.Id },
            new Recipient { LastName = "В", FirstName = "В", Rank = "солдат", IntakeId = intake.Id },
            new Recipient { LastName = "Г", FirstName = "Г", Rank = "солдат", IntakeId = intake.Id },
            new Recipient { LastName = "Д", FirstName = "Д", Rank = "солдат", IntakeId = intake.Id },
            new Recipient { LastName = "Е", FirstName = "Е", Rank = "підполковник", IntakeId = null });
        ctx.SaveChanges();
    }

    [Fact]
    public void Count_WithoutFilters_IsEveryone()
    {
        using var db = new TestDb();
        Seed(db);

        Assert.Equal(6, TestServices.Generation(db).GetRecipientCount(RosterSelection.Everyone));
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
