using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Intakes;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Completeness;

// Знайдено на справжній базі 2026-09-01: у відомості «Допуск Додаток 5» було
// ТРИ рядки з IsCurrent = true (v5, v6, v7). Матриця брала з них довільний,
// натрапляла на найстаріший - зі складом, у якому теперішніх людей нема, - і
// колонка лишалася порожньою, хоча генерація щоразу рапортувала успіх.
//
// Дублі накопичувались тому, що генерація гасила лише ОДИН попередній
// «поточний» (FirstOrDefault). Два боки однієї біди, обидва закріплені тут.
public class OneCurrentGroupDocumentTests
{
    private sealed record Seeded(int IntakeId, int PackageId, int SheetId, int OldPersonId, List<int> NowPersonIds);

    private static Seeded Seed(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });

        var intake = new Intake { Number = 5, DisplayNumber = "Набір №5" };
        ctx.Intakes.Add(intake);

        var root = new OrgNode { Name = "Корінь", Depth = 0, SortOrder = 0, Path = "/" };
        ctx.OrgNodes.Add(root);

        var older = TemplateFixtures.Person(1, "СТАРЕНКО", "Старий");
        var one = TemplateFixtures.Person(2, "ШЕВЧЕНКО", "Тарас");
        var two = TemplateFixtures.Person(3, "ФРАНКО", "Іван");
        ctx.Recipients.AddRange(older, one, two);

        var sheet = new ExportTemplate
        {
            Name = "Допуск Додаток 5",
            OriginalFileName = "dopusk.xlsx",
            Content = TemplateFixtures.Bytes(TemplateFixtures.DopuskXlsx),
            UploadedAt = DateTime.Now,
            UsesPlaceholders = true
        };
        ctx.ExportTemplates.Add(sheet);
        ctx.SaveChanges();

        // «СТАРЕНКО» був у наборі колись і вже не з ним - як і в справжній базі.
        one.IntakeId = intake.Id;
        two.IntakeId = intake.Id;

        var package = new GenerationPackage { Name = "Пакет" };
        package.ExportTemplates.Add(new GenerationPackageExportTemplate
        {
            ExportTemplateId = sheet.Id, SortOrder = 0, FitnessFilter = FitnessFilter.All
        });
        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();

        return new Seeded(intake.Id, package.Id, sheet.Id, older.Id, new List<int> { one.Id, two.Id });
    }

    private static void AddCurrentDocument(TestDb db, int sheetId, int version, IEnumerable<int> participants)
    {
        using var ctx = db.Factory.CreateDbContext();
        var doc = new GeneratedGroupDocument
        {
            ExportTemplateId = sheetId,
            IntakeId = null,
            GeneratedAt = new DateTime(2026, 8, 24).AddDays(version),
            GeneratedByUserId = 1,
            FileName = $"dopusk-v{version}.xlsx",
            Version = version,
            IsCurrent = true,
            HasContent = true
        };
        foreach (var id in participants)
            doc.Recipients.Add(new GeneratedGroupDocumentRecipient { RecipientId = id });
        doc.RecipientCount = doc.Recipients.Count;

        ctx.GeneratedGroupDocuments.Add(doc);
        ctx.SaveChanges();
    }

    // ─── Матриця не має губитись серед кількох «поточних» ────────────────────

    [Fact]
    public async Task WithSeveralCurrentDocumentsTheMatrixUsesTheNewest()
    {
        using var db = new TestDb();
        var s = Seed(db);

        // Старий «поточний» зі складом, у якому теперішніх людей немає…
        AddCurrentDocument(db, s.SheetId, version: 5, new[] { s.OldPersonId });
        // …і свіжий, що охоплює обох.
        AddCurrentDocument(db, s.SheetId, version: 7, s.NowPersonIds);

        var data = await TestServices.Completeness(db).BuildAsync(s.IntakeId, s.PackageId);

        foreach (var personId in s.NowPersonIds)
        {
            Assert.True(data.Docs.ContainsKey((personId, s.SheetId, true)),
                "матриця взяла не найсвіжіший документ - людина лишилась без відмітки");
        }
    }

    [Fact]
    public async Task TheNewestIsChosenEvenWhenItComesFirstInTheTable()
    {
        using var db = new TestDb();
        var s = Seed(db);

        // Порядок вставки зворотний - результат не має від нього залежати.
        AddCurrentDocument(db, s.SheetId, version: 9, s.NowPersonIds);
        AddCurrentDocument(db, s.SheetId, version: 4, new[] { s.OldPersonId });

        var data = await TestServices.Completeness(db).BuildAsync(s.IntakeId, s.PackageId);

        Assert.True(data.Docs.TryGetValue((s.NowPersonIds[0], s.SheetId, true), out var cell));
        Assert.Equal(9, cell!.Version);
    }

    // ─── Генерація не має лишати другого «поточного» ─────────────────────────

    [Fact]
    public void GeneratingClearsEveryPreviousCurrentDocument()
    {
        using var db = new TestDb();
        var s = Seed(db);

        // Три «поточних» одразу - саме те, що знайшлося в справжній базі.
        AddCurrentDocument(db, s.SheetId, version: 5, new[] { s.OldPersonId });
        AddCurrentDocument(db, s.SheetId, version: 6, s.NowPersonIds);
        AddCurrentDocument(db, s.SheetId, version: 7, s.NowPersonIds);

        var folder = Path.Combine(Path.GetTempPath(), $"gendoc-onecur-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            TestServices.Generation(db).GenerateTemplatesForRecipients(
                Array.Empty<int>(), new[] { s.SheetId }, s.NowPersonIds,
                folder, new Dictionary<string, string>(), new Progress<string>());

            using var ctx = db.Factory.CreateDbContext();
            var current = ctx.GeneratedGroupDocuments
                .Where(g => g.ExportTemplateId == s.SheetId && g.IsCurrent)
                .ToList();

            Assert.Single(current);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
