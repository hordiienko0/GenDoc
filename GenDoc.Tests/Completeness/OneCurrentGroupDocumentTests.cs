using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Intakes;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Completeness;

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

    private static int AddIntake(TestDb db, int number)
    {
        using var ctx = db.Factory.CreateDbContext();
        var intake = new Intake { Number = number, DisplayNumber = $"Набір №{number}" };
        ctx.Intakes.Add(intake);
        ctx.SaveChanges();
        return intake.Id;
    }

    private static void AddCurrentDocument(
        TestDb db, int sheetId, int version, IEnumerable<int> participants, int? intakeId = null)
    {
        using var ctx = db.Factory.CreateDbContext();
        var doc = new GeneratedGroupDocument
        {
            ExportTemplateId = sheetId,
            IntakeId = intakeId,
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

    [Fact]
    public async Task WithSeveralCurrentDocumentsTheMatrixUsesTheNewest()
    {
        using var db = new TestDb();
        var s = Seed(db);

        AddCurrentDocument(db, s.SheetId, version: 5, new[] { s.OldPersonId });
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

        AddCurrentDocument(db, s.SheetId, version: 9, s.NowPersonIds);
        AddCurrentDocument(db, s.SheetId, version: 4, new[] { s.OldPersonId });

        var data = await TestServices.Completeness(db).BuildAsync(s.IntakeId, s.PackageId);

        Assert.True(data.Docs.TryGetValue((s.NowPersonIds[0], s.SheetId, true), out var cell));
        Assert.Equal(9, cell!.Version);
    }

    [Fact]
    public void GeneratingClearsEveryPreviousCurrentDocument()
    {
        using var db = new TestDb();
        var s = Seed(db);

        AddCurrentDocument(db, s.SheetId, version: 5, new[] { s.OldPersonId }, s.IntakeId);
        AddCurrentDocument(db, s.SheetId, version: 6, s.NowPersonIds, s.IntakeId);
        AddCurrentDocument(db, s.SheetId, version: 7, s.NowPersonIds, s.IntakeId);

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

    [Fact]
    public void GeneratingStoresTheIntakeOfTheRoster()
    {
        using var db = new TestDb();
        var s = Seed(db);

        var folder = Path.Combine(Path.GetTempPath(), $"gendoc-onecur-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            TestServices.Generation(db).GenerateTemplatesForRecipients(
                Array.Empty<int>(), new[] { s.SheetId }, s.NowPersonIds,
                folder, new Dictionary<string, string>(), new Progress<string>());

            using var ctx = db.Factory.CreateDbContext();
            var doc = Assert.Single(ctx.GeneratedGroupDocuments.Where(g => g.ExportTemplateId == s.SheetId).ToList());
            Assert.Equal(s.IntakeId, doc.IntakeId);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void GeneratingForOneIntakeKeepsTheOtherIntakesSheetCurrent()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var otherIntakeId = AddIntake(db, 28);
        AddCurrentDocument(db, s.SheetId, version: 7, new[] { s.OldPersonId }, otherIntakeId);

        var folder = Path.Combine(Path.GetTempPath(), $"gendoc-onecur-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            TestServices.Generation(db).GenerateTemplatesForRecipients(
                Array.Empty<int>(), new[] { s.SheetId }, s.NowPersonIds,
                folder, new Dictionary<string, string>(), new Progress<string>());

            using var ctx = db.Factory.CreateDbContext();
            var other = ctx.GeneratedGroupDocuments.Single(g => g.IntakeId == otherIntakeId);
            var mine = ctx.GeneratedGroupDocuments.Single(g => g.IntakeId == s.IntakeId);

            Assert.True(other.IsCurrent, "відомість чужого набору втратила статус поточної");
            Assert.True(mine.IsCurrent);
            Assert.Equal(1, mine.Version);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task TheMatrixIgnoresSheetsOfAnotherIntake()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var otherIntakeId = AddIntake(db, 28);

        AddCurrentDocument(db, s.SheetId, version: 3, s.NowPersonIds, otherIntakeId);

        var data = await TestServices.Completeness(db).BuildAsync(s.IntakeId, s.PackageId);

        foreach (var personId in s.NowPersonIds)
            Assert.False(data.Docs.ContainsKey((personId, s.SheetId, true)),
                "матриця показала відомість чужого набору");
    }
}
