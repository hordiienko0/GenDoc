using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Services.Templates;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Generation;

public class ManualValuesInSkipDecisionTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-manual-skip-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static readonly IProgress<string> NoProgress = new Progress<string>(_ => { });

    private const string ReportDateTag = "{{дата_рапорту}}";
    private const string SheetNumberTag = "{{номер_відомості}}";

    private static readonly string[] RaportTags =
    {
        "{{дата_зарахування}}", "{{дата_посвідчення}}", "{{дата_прибуття}}", "{{дата_рапорту}}",
        "{{звання_зв}}", "{{звання_підписанта}}", "{{номер_посвідчення}}", "{{прибув}}",
        "{{прод_атестат}}", "{{піб_зв}}", "{{піб_підписанта}}", "{{таким}}"
    };

    private sealed record Seeded(int PackageId, int IntakeId, int RecipientId, int TemplateId);

    private static Seeded SeedDocxPackage(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now });
        ctx.AppSettings.Add(new AppSettings { DetectStaleDocuments = true });

        var intake = new Intake { Number = 7, DisplayNumber = "Набір №7", Status = IntakeStatus.Active };
        ctx.Intakes.Add(intake);
        ctx.SaveChanges();

        var person = TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас");
        person.IntakeId = intake.Id;
        ctx.Recipients.Add(person);

        var template = new Template
        {
            Name = "Рапорт котлове",
            OriginalFileName = "raport.docx",
            Content = TemplateFixtures.Bytes(TemplateFixtures.RaportIndividualDocx),
            UploadedAt = DateTime.Now,
            Kind = TemplateKind.PerRecipient
        };
        ctx.Templates.Add(template);
        ctx.SaveChanges();

        foreach (var tag in RaportTags)
        {
            var (sourceType, fieldName) = PlaceholderTagMaps.Classify(tag);
            ctx.TemplateFieldMappings.Add(new TemplateFieldMapping
            {
                TemplateId = template.Id, PlaceholderTag = tag, SourceType = sourceType, FieldName = fieldName
            });
        }

        var package = new GenerationPackage { Name = "Котлове" };
        package.Templates.Add(new GenerationPackageTemplate { TemplateId = template.Id, SortOrder = 0 });
        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();

        return new Seeded(package.Id, intake.Id, person.Id, template.Id);
    }

    private static void AddSheet(TestDb db, int packageId)
    {
        using var ctx = db.Factory.CreateDbContext();
        var (row, mappings) = XlsxTemplateScan.ForGeneration(TemplateFixtures.RozdavalnaXlsx);
        var sheet = new ExportTemplate
        {
            Name = "Роздавальна відомість",
            OriginalFileName = "rozdavalna.xlsx",
            Content = TemplateFixtures.Bytes(TemplateFixtures.RozdavalnaXlsx),
            UploadedAt = DateTime.Now,
            UsesPlaceholders = true,
            TemplateRowIndex = row
        };
        foreach (var mapping in mappings) sheet.ColumnMappings.Add(mapping);
        ctx.ExportTemplates.Add(sheet);
        ctx.SaveChanges();

        ctx.GenerationPackageExportTemplates.Add(new GenerationPackageExportTemplate
        {
            GenerationPackageId = packageId, ExportTemplateId = sheet.Id, SortOrder = 0, FitnessFilter = FitnessFilter.All
        });
        ctx.SaveChanges();
    }

    private RunResult Run(TestDb db, int packageId, Dictionary<string, string> manualValues,
        bool regenerate = false, int? courseOfficerId = null)
        => TestServices.Generation(db).RunPackage(
            packageId, _folder, manualValues, regenerate, RosterSelection.Everyone, NoProgress, courseOfficerId);

    private static Dictionary<string, string> ReportDate(string date) => new() { [ReportDateTag] = date };

    private static List<GeneratedDocument> Versions(TestDb db, int recipientId)
    {
        using var ctx = db.Factory.CreateDbContext();
        return ctx.GeneratedDocuments.Where(g => g.RecipientId == recipientId).OrderBy(g => g.Version).ToList();
    }

    [Fact]
    public void Docx_ChangedManualValue_SecondRunGeneratesANewVersion()
    {
        using var db = new TestDb();
        var s = SeedDocxPackage(db);

        Run(db, s.PackageId, ReportDate("01.09.2026"));
        var second = Run(db, s.PackageId, ReportDate("02.09.2026"));

        Assert.Equal(1, second.Generated);
        Assert.Equal(0, second.Skipped);
        var versions = Versions(db, s.RecipientId);
        Assert.Equal(new[] { 1, 2 }, versions.Select(v => v.Version).ToArray());
        Assert.True(versions.Single(v => v.IsCurrent).Version == 2);
    }

    [Fact]
    public void Docx_SameManualValue_SecondRunSkipsAndSaysWhy()
    {
        using var db = new TestDb();
        var s = SeedDocxPackage(db);

        Run(db, s.PackageId, ReportDate("01.09.2026"));
        var second = Run(db, s.PackageId, ReportDate("01.09.2026"));

        Assert.Equal(0, second.Generated);
        Assert.Equal(1, second.Skipped);
        var issue = Assert.Single(second.Issues!, i => i.Message.StartsWith("пропущено", StringComparison.Ordinal));
        Assert.False(issue.IsError);
        Assert.Equal(RunIssue.PhaseDocx, issue.Phase);
        Assert.Equal("ШЕВЧЕНКО Тарас", issue.Person);
        Assert.Equal(GenerationService.SkippedUpToDate, issue.Message);
        Assert.Single(Versions(db, s.RecipientId));
    }

    [Fact]
    public void Docx_ManualUploadIsCurrent_RunWithoutRegenerateLeavesItCurrent()
    {
        using var db = new TestDb();
        var s = SeedDocxPackage(db);
        Run(db, s.PackageId, ReportDate("01.09.2026"));
        AddManualUploadVersion(db, s.RecipientId, s.TemplateId);

        var second = Run(db, s.PackageId, ReportDate("02.09.2026"));

        Assert.Equal(0, second.Generated);
        Assert.Equal(1, second.Skipped);
        var issue = Assert.Single(second.Issues!, i => i.Message.StartsWith("пропущено", StringComparison.Ordinal));
        Assert.Equal(GenerationService.SkippedManualUpload, issue.Message);
        Assert.False(issue.IsError);

        var current = Assert.Single(Versions(db, s.RecipientId), v => v.IsCurrent);
        Assert.Equal(DocumentSourceType.ManualUpload, current.SourceType);
        Assert.Equal(2, current.Version);
    }

    [Fact]
    public void Docx_ManualUploadIsCurrent_RegenerateExistingStillMakesANewVersion()
    {
        using var db = new TestDb();
        var s = SeedDocxPackage(db);
        Run(db, s.PackageId, ReportDate("01.09.2026"));
        AddManualUploadVersion(db, s.RecipientId, s.TemplateId);

        var second = Run(db, s.PackageId, ReportDate("01.09.2026"), regenerate: true);

        Assert.Equal(1, second.Generated);
        var current = Assert.Single(Versions(db, s.RecipientId), v => v.IsCurrent);
        Assert.Equal(3, current.Version);
        Assert.Equal(DocumentSourceType.Generated, current.SourceType);
    }

    private static void AddManualUploadVersion(TestDb db, int recipientId, int templateId)
    {
        using var ctx = db.Factory.CreateDbContext();
        foreach (var doc in ctx.GeneratedDocuments.Where(g => g.RecipientId == recipientId && g.IsCurrent))
            doc.IsCurrent = false;
        ctx.GeneratedDocuments.Add(new GeneratedDocument
        {
            RecipientId = recipientId, TemplateId = templateId, GeneratedAt = DateTime.Now, GeneratedByUserId = 1,
            FileName = "підписаний рапорт.pdf", SizeBytes = 3, Version = 2, IsCurrent = true,
            SourceType = DocumentSourceType.ManualUpload, HasContent = true,
            Content = new GeneratedDocumentContent { Content = new byte[] { 1, 2, 3 } }
        });
        ctx.SaveChanges();
    }

    [Fact]
    public void Docx_ChangedCourseOfficer_SecondRunGeneratesANewVersion()
    {
        using var db = new TestDb();
        var (packageId, cadetId, firstOfficerId, secondOfficerId) = SeedSignedDocxPackage(db);
        var cadetOnly = new RosterSelection(false, new[] { cadetId }, FitnessFilter.All, false, Array.Empty<RankCategory>(), Array.Empty<string>());
        RunResult RunFor(int officerId) => TestServices.Generation(db).RunPackage(
            packageId, _folder, new Dictionary<string, string>(), false, cadetOnly, NoProgress, officerId);

        RunFor(firstOfficerId);
        var sameOfficer = RunFor(firstOfficerId);
        var otherOfficer = RunFor(secondOfficerId);

        Assert.Equal(1, sameOfficer.Skipped);
        Assert.Equal(1, otherOfficer.Generated);
        Assert.Equal(2, Versions(db, cadetId).Count);
    }

    private static (int PackageId, int CadetId, int FirstOfficerId, int SecondOfficerId) SeedSignedDocxPackage(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now });

        var template = new Template
        {
            Name = "Картка", OriginalFileName = "kartka.docx",
            Content = BuildSignedDocx(), UploadedAt = DateTime.Now, Kind = TemplateKind.PerRecipient
        };
        ctx.Templates.Add(template);

        var intake = new Intake { Number = 7, DisplayNumber = "Набір №7", Status = IntakeStatus.Active };
        ctx.Intakes.Add(intake);
        ctx.SaveChanges();

        var first = new Recipient { LastName = "Ковальчук", FirstName = "Василь", Rank = "капітан", IsCourseOfficer = true };
        var second = new Recipient { LastName = "Мельник", FirstName = "Петро", Rank = "майор", IsCourseOfficer = true };
        var cadet = new Recipient { LastName = "ШЕВЧЕНКО", FirstName = "Тарас", Rank = "солдат", IntakeId = intake.Id };
        ctx.Recipients.AddRange(first, second, cadet);
        ctx.SaveChanges();

        foreach (var tag in new[] { "{{піб}}", "{{курсовий_офіцер}}" })
        {
            var (sourceType, fieldName) = PlaceholderTagMaps.Classify(tag);
            ctx.TemplateFieldMappings.Add(new TemplateFieldMapping
            {
                TemplateId = template.Id, PlaceholderTag = tag, SourceType = sourceType, FieldName = fieldName
            });
        }

        var package = new GenerationPackage { Name = "Картки" };
        package.Templates.Add(new GenerationPackageTemplate { TemplateId = template.Id, SortOrder = 0 });
        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();

        return (package.Id, cadet.Id, first.Id, second.Id);
    }

    private static byte[] BuildSignedDocx()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            doc.AddMainDocumentPart().Document = new Document(new Body(
                new Paragraph(new Run(new Text("{{піб}}"))),
                new Paragraph(new Run(new Text("{{курсовий_офіцер}}")))));
            doc.MainDocumentPart!.Document!.Save();
        }
        return stream.ToArray();
    }

    private static Dictionary<string, string> SheetValues(string number) => new()
    {
        ["{{дата_аркуша}}"] = "07.08.2026",
        ["{{калібр}}"] = "5,45",
        ["{{кількість_патронів}}"] = "30",
        [SheetNumberTag] = number,
        [ReportDateTag] = "01.09.2026"
    };

    [Fact]
    public void Sheet_ChangedManualValue_SecondRunGeneratesANewVersion()
    {
        using var db = new TestDb();
        var s = SeedDocxPackage(db);
        AddSheet(db, s.PackageId);

        var first = Run(db, s.PackageId, SheetValues("12"));
        var second = Run(db, s.PackageId, SheetValues("13"));

        Assert.Equal(1, first.GroupGenerated);
        Assert.Equal(1, second.GroupGenerated);
        Assert.Equal(0, second.GroupSkipped);

        using var ctx = db.Factory.CreateDbContext();
        var current = Assert.Single(ctx.GeneratedGroupDocuments.Where(g => g.IsCurrent).ToList());
        Assert.Equal(2, current.Version);
    }

    [Fact]
    public void Sheet_SameManualValues_SecondRunSkipsAndSaysWhy()
    {
        using var db = new TestDb();
        var s = SeedDocxPackage(db);
        AddSheet(db, s.PackageId);

        Run(db, s.PackageId, SheetValues("12"));
        var second = Run(db, s.PackageId, SheetValues("12"));

        Assert.Equal(0, second.GroupGenerated);
        Assert.Equal(1, second.GroupSkipped);
        var issue = Assert.Single(second.Issues!, i => i.Phase == RunIssue.PhaseXlsx);
        Assert.Equal(GenerationService.SkippedUpToDate, issue.Message);
        Assert.False(issue.IsError);
    }

    [Fact]
    public async Task Matrix_DocumentGeneratedWithManualValues_IsNotStale()
    {
        using var db = new TestDb();
        var s = SeedDocxPackage(db);

        Run(db, s.PackageId, ReportDate("01.09.2026"));

        var data = await TestServices.Completeness(db).BuildAsync(s.IntakeId, s.PackageId);
        var cell = data.Docs[(s.RecipientId, s.TemplateId, false)];

        Assert.False(cell.IsStale);
    }

    [Fact]
    public async Task Matrix_PersonDataChangedAfterGenerationWithManualValues_IsStale()
    {
        using var db = new TestDb();
        var s = SeedDocxPackage(db);
        Run(db, s.PackageId, ReportDate("01.09.2026"));

        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Recipients.First(r => r.Id == s.RecipientId).Rank = "сержант";
            ctx.SaveChanges();
        }

        var data = await TestServices.Completeness(db).BuildAsync(s.IntakeId, s.PackageId);

        Assert.True(data.Docs[(s.RecipientId, s.TemplateId, false)].IsStale);
    }
}
