using ClosedXML.Excel;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Generation;

public class CourseOfficerMissingGuardTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-officer-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static readonly IProgress<string> NoProgress = new Progress<string>(_ => { });

    private static byte[] BuildSheetWithSignature()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Відомість");
        sheet.Cell(1, 1).Value = "ПІБ";
        sheet.Cell(2, 1).Value = "{{піб}}";
        sheet.Cell(4, 1).Value = "{{курсовий_офіцер}}";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static int SeedPackage(TestDb db, bool withCourseOfficer)
    {
        using var ctx = db.Factory.CreateDbContext();

        ctx.Users.Add(new UserProfile { FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now });
        ctx.Recipients.AddRange(TemplateFixtures.Roster(2));

        if (withCourseOfficer)
            ctx.Recipients.Add(new Recipient
            {
                Id = 100,
                LastName = "Ковальчук", FirstName = "Василь", MiddleName = "Богданович",
                Rank = "капітан", IsCourseOfficer = true, IntakeId = null
            });

        var template = new ExportTemplate
        {
            Name = "Відомість допуску",
            OriginalFileName = "dopusk.xlsx",
            Content = BuildSheetWithSignature(),
            UsesPlaceholders = true,
            TemplateRowIndex = 2,
            UploadedAt = DateTime.Now
        };
        template.ColumnMappings.Add(new ExportTemplateColumnMapping
        {
            ColumnIndex = 1, HeaderText = string.Empty,
            FieldKey = nameof(ExportFieldKey.FullNameFormatted),
            PlaceholderTag = "{{піб}}", SourceType = MappingSourceType.Recipient
        });
        template.ColumnMappings.Add(new ExportTemplateColumnMapping
        {
            ColumnIndex = 0, HeaderText = string.Empty,
            FieldKey = nameof(ExportFieldKey.CourseOfficerSignature),
            PlaceholderTag = "{{курсовий_офіцер}}", SourceType = MappingSourceType.Recipient
        });
        ctx.ExportTemplates.Add(template);
        ctx.SaveChanges();

        var package = new GenerationPackage { Name = "Пакет допуску" };
        package.ExportTemplates.Add(new GenerationPackageExportTemplate
        {
            ExportTemplateId = template.Id, SortOrder = 0, FitnessFilter = FitnessFilter.All
        });
        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();

        return package.Id;
    }

    [Fact]
    public void RunPackage_NoCourseOfficerInPermanentStaff_SkipsDocumentAndReportsError()
    {
        using var db = new TestDb();
        var packageId = SeedPackage(db, withCourseOfficer: false);

        var result = TestServices.Generation(db).RunPackage(
            packageId, _folder, new Dictionary<string, string>(),
            regenerateExisting: false, RosterSelection.Everyone, NoProgress);

        Assert.Equal(0, result.GroupGenerated);
        Assert.Equal(1, result.GroupErrors);

        using var ctx = db.Factory.CreateDbContext();
        Assert.Empty(ctx.GeneratedGroupDocuments.ToList());

        var run = Assert.Single(ctx.GenerationPackageRuns.ToList());
        Assert.True(RunIssue.TryDeserialize(run.Summary, out var issues));
        var issue = Assert.Single(issues.Where(i => i.IsError));
        Assert.Equal("Відомість допуску", issue.TemplateName);
        Assert.Contains("курсов", issue.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunPackage_CourseOfficerPresent_GeneratesDocument()
    {
        using var db = new TestDb();
        var packageId = SeedPackage(db, withCourseOfficer: true);

        var result = TestServices.Generation(db).RunPackage(
            packageId, _folder, new Dictionary<string, string>(),
            regenerateExisting: false, RosterSelection.Everyone, NoProgress);

        Assert.Equal(0, result.GroupErrors);
        Assert.Equal(1, result.GroupGenerated);
    }

    [Fact]
    public void RunPackage_WithChosenCourseOfficer_SignsWithThatPerson()
    {
        using var db = new TestDb();
        var packageId = SeedPackage(db, withCourseOfficer: true);

        int chosenId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var second = new Recipient
            {
                Id = 101,
                LastName = "Мельник", FirstName = "Петро", MiddleName = "Іванович",
                Rank = "майор", IsCourseOfficer = true, IntakeId = null
            };
            ctx.Recipients.Add(second);
            ctx.SaveChanges();
            chosenId = second.Id;
        }

        TestServices.Generation(db).RunPackage(
            packageId, _folder, new Dictionary<string, string>(),
            regenerateExisting: false, RosterSelection.Everyone, NoProgress,
            courseOfficerId: chosenId);

        using var read = db.Factory.CreateDbContext();
        var content = read.GeneratedGroupDocumentContents.Single().Content;

        using var workbook = new XLWorkbook(new MemoryStream(content));

        var signature = string.Join(
            " ", workbook.Worksheets.First().CellsUsed().Select(c => c.GetString()));

        Assert.Contains("Мельник", signature, StringComparison.Ordinal);
        Assert.DoesNotContain("Ковальчук", signature, StringComparison.Ordinal);
    }

    [Fact]
    public void PackageNeedsCourseOfficer_TrueOnlyWhenSomeTemplateAsksForTheSignature()
    {
        using var db = new TestDb();
        var withSignature = SeedPackage(db, withCourseOfficer: false);

        int withoutSignature;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var plain = new GenerationPackage { Name = "Пакет без підпису" };
            ctx.GenerationPackages.Add(plain);
            ctx.SaveChanges();
            withoutSignature = plain.Id;
        }

        var service = TestServices.Generation(db);

        Assert.True(service.PackageNeedsCourseOfficer(withSignature));
        Assert.False(service.PackageNeedsCourseOfficer(withoutSignature));
    }
}
