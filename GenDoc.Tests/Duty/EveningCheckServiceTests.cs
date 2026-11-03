using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Duty;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Duty;

public class EveningCheckServiceTests
{
    internal sealed record Seeded(int RootId, int SecondedId, List<Recipient> Course, List<Recipient> Seconded);

    /// <summary>Набір №30 з вкладеною папкою «Прикомандировані» - як у дереві користувача.</summary>
    internal static Seeded Seed(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        var root = new OrgNode { Name = "Набір №30", Depth = 0 };
        ctx.OrgNodes.Add(root);
        ctx.SaveChanges();
        root.Path = $"/{root.Id}/";
        var seconded = new OrgNode { Name = "Прикомандировані", ParentId = root.Id, Depth = 1 };
        ctx.OrgNodes.Add(seconded);
        ctx.SaveChanges();
        seconded.Path = $"/{root.Id}/{seconded.Id}/";

        var intake = new Intake
        {
            Number = 30, DisplayNumber = "Набір №30", Status = IntakeStatus.Active, RootOrgNodeId = root.Id,
            DateStart = new DateOnly(2026, 9, 15), DateEnd = new DateOnly(2026, 11, 14)
        };
        ctx.Intakes.Add(intake);
        ctx.SaveChanges();
        root.IntakeId = intake.Id;
        seconded.IntakeId = intake.Id;

        var course = new List<Recipient>
        {
            new() { LastName = "Соломенцев", FirstName = "Дмитро", MiddleName = "Олександрович", Rank = "молодший лейтенант", IntakeId = intake.Id, OrgNodeId = root.Id },
            new() { LastName = "Усс", FirstName = "Олег", MiddleName = "Геннадійович", Rank = "старший лейтенант", IntakeId = intake.Id, OrgNodeId = root.Id },
            new() { LastName = "Гордієнко", FirstName = "Микола", MiddleName = "Геннадійович", Rank = "молодший лейтенант", IntakeId = intake.Id }
        };
        var secondedPeople = new List<Recipient>
        {
            new() { LastName = "Бурла", FirstName = "Володимир", MiddleName = "Миколайович", Rank = "капітан", IntakeId = intake.Id, OrgNodeId = seconded.Id },
            new() { LastName = "Борисюк", FirstName = "Андрій", MiddleName = "Володимирович", Rank = "старший лейтенант", IntakeId = intake.Id, OrgNodeId = seconded.Id },
            new() { LastName = "Окоманюк", FirstName = "Володимир", MiddleName = "Валентинович", Rank = "капітан", IntakeId = intake.Id, OrgNodeId = seconded.Id }
        };
        ctx.Recipients.AddRange(course);
        ctx.Recipients.AddRange(secondedPeople);
        ctx.Recipients.Add(new Recipient { LastName = "Курсовий", FirstName = "Олег", Rank = "капітан" });
        ctx.Recipients.Add(new Recipient { LastName = "Видалений", FirstName = "Хтось", Rank = "майор", IntakeId = intake.Id, DeletedAt = DateTime.Now });
        ctx.OrganizationSettings.Add(new OrganizationSettings { CommanderRank = "підполковник", CommanderFullName = "Дмитро ВЕРЕМЕЄНКО" });
        ctx.SaveChanges();
        return new Seeded(root.Id, seconded.Id, course, secondedPeople);
    }

    internal static EveningCheckService Service(TestDb db, FakeAuditLog? audit = null)
    {
        audit ??= new FakeAuditLog();
        var rosters = new DutyRosterService(db.Factory, audit, new FakeCurrentUser(), TestServices.UserSettings(db, null));
        return new EveningCheckService(db.Factory, audit, rosters);
    }

    [Fact]
    public async Task FoldersAreTheDutyRosterTreeWithPeoplePerFolder()
    {
        using var db = new TestDb();
        var seeded = Seed(db);

        var folders = await Service(db).GetFoldersAsync();

        var root = folders.Single(f => f.Id == seeded.RootId);
        var seconded = folders.Single(f => f.Id == seeded.SecondedId);
        Assert.True(root.IsIntakeRoot);
        Assert.Equal(seeded.RootId, seconded.ParentId);
        Assert.Equal(seeded.Course.Concat(seeded.Seconded).Select(p => p.Id).Order(), root.PeopleIds);
        Assert.Equal(seeded.Seconded.Select(p => p.Id).Order(), seconded.PeopleIds);
    }

    [Fact]
    public async Task PeopleComeByRankSeniorityThenAlphabetAndOnlyTheAskedOnes()
    {
        using var db = new TestDb();
        var seeded = Seed(db);

        var people = await Service(db).GetPeopleAsync(seeded.Seconded.Select(p => p.Id).Append(999_999).ToList());

        Assert.Equal(new[] { "БУРЛА Володимир Миколайович", "ОКОМАНЮК Володимир Валентинович", "БОРИСЮК Андрій Володимирович" },
            people.Select(p => p.FullName));
        Assert.Equal("капітан", people[0].Rank);
        Assert.All(people, p => Assert.False(p.IsLimited));
        Assert.Empty(await Service(db).GetPeopleAsync(Array.Empty<int>()));

        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Recipients.Single(r => r.LastName == "Бурла").FitnessCategory = "обмежено придатний";
            ctx.SaveChanges();
        }
        var again = await Service(db).GetPeopleAsync(seeded.Seconded.Select(p => p.Id).ToList());
        Assert.True(again.Single(p => p.FullName.StartsWith("БУРЛА")).IsLimited);
        Assert.False(again.Single(p => p.FullName.StartsWith("ОКОМАНЮК")).IsLimited);
    }

    [Fact]
    public async Task SignatureAndExportLogUseTheOrganizationAndPeriod()
    {
        using var db = new TestDb();
        Seed(db);
        var audit = new FakeAuditLog();
        var service = Service(db, audit);

        Assert.Equal("Начальник курсів П та ПК:  підполковник                     Д. ВЕРЕМЕЄНКО", await service.GetDefaultSignatureAsync());

        var data = new EveningCheckData { DateFrom = new DateOnly(2026, 9, 15), DateTo = new DateOnly(2026, 11, 14) };
        var group = new EveningCheckGroup { Title = EveningCheckText.SecondedTitle };
        group.People.Add(new EveningCheckPerson("капітан", "БУРЛА Володимир Миколайович"));
        data.Groups.Add(group);
        service.LogExport(data, @"C:\tmp\Список вечірньої перевірки.xlsx");

        Assert.Equal("export:EveningCheck:1", audit.Entries.Single());
        Assert.Equal("Список вечірньої перевірки 15.09 - 14.11.2026, 1 особа → Список вечірньої перевірки.xlsx", audit.Details.Single());
    }
}
