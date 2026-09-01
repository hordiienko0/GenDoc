using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Generation;

// Вибіркова генерація мусить уміти й ГРУПОВІ документи, а не лише персональні
// та відомості. Без цього «Комплектність» могла попросити відсутню відомість,
// але не відсутній груповий наказ - хоча обидва це один документ на весь склад
// (вимога користувача 2026-09-01).
public class SelectiveGroupGenerationTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-selgroup-{Guid.NewGuid():N}");

    public SelectiveGroupGenerationTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static readonly IProgress<string> NoProgress = new Progress<string>();

    private sealed record Seeded(int GroupTemplateId, int PersonalTemplateId, List<int> RecipientIds);

    private static Seeded Seed(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });

        var intake = new Intake { Number = 7, DisplayNumber = "Набір №7" };
        ctx.Intakes.Add(intake);

        var one = TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас");
        var two = TemplateFixtures.Person(2, "ФРАНКО", "Іван");
        ctx.Recipients.AddRange(one, two);

        // Груповий шаблон із повторюваним блоком - по рядку на людину.
        var group = new Template
        {
            Name = "Список групи",
            OriginalFileName = "group.docx",
            Content = TemplateFixtures.Bytes(TemplateFixtures.RaportGroupDocx),
            UploadedAt = DateTime.Now,
            Kind = TemplateKind.Group
        };
        var personal = new Template
        {
            Name = "Рапорт",
            OriginalFileName = "personal.docx",
            Content = TemplateFixtures.Bytes(TemplateFixtures.RaportIndividualDocx),
            UploadedAt = DateTime.Now,
            Kind = TemplateKind.PerRecipient
        };
        ctx.Templates.AddRange(group, personal);
        ctx.SaveChanges();

        one.IntakeId = intake.Id;
        two.IntakeId = intake.Id;
        ctx.SaveChanges();

        return new Seeded(group.Id, personal.Id, new List<int> { one.Id, two.Id });
    }

    [Fact]
    public void AGroupTemplateProducesOneDocumentForTheWholeRoster()
    {
        using var db = new TestDb();
        var s = Seed(db);

        var result = TestServices.Generation(db).GenerateTemplatesForRecipients(
            new[] { s.GroupTemplateId }, Array.Empty<int>(), s.RecipientIds,
            _folder, new Dictionary<string, string>(), NoProgress);

        Assert.Equal(0, result.DocxGroupErrors);
        Assert.Equal(1, result.DocxGroupGenerated);

        using var ctx = db.Factory.CreateDbContext();
        // Один документ, а не по одному на людину.
        var doc = Assert.Single(ctx.GeneratedGroupDocuments.ToList());
        Assert.Equal(s.GroupTemplateId, doc.TemplateId);
        // Персональних документів груповий шаблон не створює.
        Assert.Empty(ctx.GeneratedDocuments.ToList());
    }

    // Саме цей запис і робить клітинку зеленою в матриці.
    [Fact]
    public void TheGroupDocumentRecordsWhoIsInIt()
    {
        using var db = new TestDb();
        var s = Seed(db);

        TestServices.Generation(db).GenerateTemplatesForRecipients(
            new[] { s.GroupTemplateId }, Array.Empty<int>(), s.RecipientIds,
            _folder, new Dictionary<string, string>(), NoProgress);

        using var ctx = db.Factory.CreateDbContext();
        var doc = ctx.GeneratedGroupDocuments.Single();
        ctx.Entry(doc).Collection(d => d.Recipients).Load();

        Assert.Equal(s.RecipientIds.OrderBy(x => x), doc.Recipients.Select(r => r.RecipientId).OrderBy(x => x));
    }

    // Звужений склад дає документ саме на цих людей: так «Комплектність»
    // передає тих, кого охоплює фільтр придатності відомості.
    [Fact]
    public void ANarrowedRosterGivesADocumentForExactlyThosePeople()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var onlyFirst = s.RecipientIds.Take(1).ToList();

        TestServices.Generation(db).GenerateTemplatesForRecipients(
            new[] { s.GroupTemplateId }, Array.Empty<int>(), onlyFirst,
            _folder, new Dictionary<string, string>(), NoProgress);

        using var ctx = db.Factory.CreateDbContext();
        var doc = ctx.GeneratedGroupDocuments.Single();
        ctx.Entry(doc).Collection(d => d.Recipients).Load();

        Assert.Equal(onlyFirst, doc.Recipients.Select(r => r.RecipientId).ToList());
    }

    // Персональні шаблони поруч не постраждали.
    [Fact]
    public void PersonalTemplatesStillGenerateOnePerPerson()
    {
        using var db = new TestDb();
        var s = Seed(db);

        var result = TestServices.Generation(db).GenerateTemplatesForRecipients(
            new[] { s.PersonalTemplateId }, Array.Empty<int>(), s.RecipientIds,
            _folder, new Dictionary<string, string>(), NoProgress);

        Assert.Equal(2, result.Generated);
        Assert.Equal(0, result.DocxGroupGenerated);

        using var ctx = db.Factory.CreateDbContext();
        Assert.Equal(2, ctx.GeneratedDocuments.Count());
        Assert.Empty(ctx.GeneratedGroupDocuments.ToList());
    }

    // Обидва види разом - один прогін, обидва результати.
    [Fact]
    public void APersonalAndAGroupTemplateInOneCallProduceBoth()
    {
        using var db = new TestDb();
        var s = Seed(db);

        var result = TestServices.Generation(db).GenerateTemplatesForRecipients(
            new[] { s.PersonalTemplateId, s.GroupTemplateId }, Array.Empty<int>(), s.RecipientIds,
            _folder, new Dictionary<string, string>(), NoProgress);

        Assert.Equal(2, result.Generated);
        Assert.Equal(1, result.DocxGroupGenerated);

        using var ctx = db.Factory.CreateDbContext();
        Assert.Equal(2, ctx.GeneratedDocuments.Count());
        Assert.Single(ctx.GeneratedGroupDocuments.ToList());
    }
}
