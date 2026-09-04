using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Services.Templates;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Generation;

public class DocxCourseOfficerSignatureRunTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-docx-sign-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static readonly IProgress<string> NoProgress = new Progress<string>(_ => { });

    private static readonly string[] Tags = { "{{піб}}", "{{курсовий_офіцер}}", "{{оцінка_1}}", "{{оцінка_загальна}}", "{{номер}}" };

    private static (int PackageId, int ChosenOfficerId) Seed(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now });

        var template = new Template
        {
            Name = "Картка", OriginalFileName = "kartka.docx",
            Content = BuildDocx(), UploadedAt = DateTime.Now, Kind = TemplateKind.PerRecipient
        };
        ctx.Templates.Add(template);

        var first = new Recipient { LastName = "Ковальчук", FirstName = "Василь", MiddleName = "Петрович", Rank = "капітан", IsCourseOfficer = true, IntakeId = null };
        var chosen = new Recipient { LastName = "Мельник", FirstName = "Петро", MiddleName = "Іванович", Rank = "майор", IsCourseOfficer = true, IntakeId = null };
        var cadet = new Recipient { LastName = "ШЕВЧЕНКО", FirstName = "Тарас", MiddleName = "Григорович", Rank = "солдат" };
        ctx.Recipients.AddRange(first, chosen, cadet);
        ctx.SaveChanges();

        foreach (var tag in Tags)
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

        return (package.Id, chosen.Id);
    }

    private static byte[] BuildDocx()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            doc.AddMainDocumentPart().Document = new Document(new Body(
                new Paragraph(new Run(new Text("{{номер}}. {{піб}}"))),
                new Paragraph(new Run(new Text("Оцінка: {{оцінка_1}} / {{оцінка_загальна}}"))),
                new Paragraph(new Run(new Text("{{курсовий_офіцер}}")))));
            doc.MainDocumentPart!.Document!.Save();
        }
        return stream.ToArray();
    }

    private static RosterSelection CadetOnly(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        var cadetId = ctx.Recipients.Single(r => r.LastName == "ШЕВЧЕНКО").Id;
        return new RosterSelection(false, new[] { cadetId }, FitnessFilter.All, false, Array.Empty<RankCategory>(), Array.Empty<string>());
    }

    private static string GeneratedText(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        var content = ctx.GeneratedDocumentContents.Single().Content;
        using var doc = WordprocessingDocument.Open(new MemoryStream(content), false);
        return string.Concat(doc.MainDocumentPart!.Document!.Body!.Descendants<Text>().Select(t => t.Text));
    }

    [Fact]
    public void RunPackage_PersonalDocx_SignsWithChosenOfficerAndFillsGradesAndRowNumber()
    {
        using var db = new TestDb();
        var (packageId, chosenId) = Seed(db);

        var result = TestServices.Generation(db).RunPackage(
            packageId, _folder, new Dictionary<string, string> { ["{{номер}}"] = "5" },
            regenerateExisting: false, CadetOnly(db), NoProgress, courseOfficerId: chosenId);

        Assert.Equal(0, result.Errors);
        Assert.Empty(result.Issues!.Where(i => i.Message.Contains("{{курсовий_офіцер}}") || i.Message.Contains("{{оцінка")));

        var text = GeneratedText(db);
        Assert.Contains("Курсовий офіцер майор Мельник П. І.", text);
        Assert.DoesNotContain("Ковальчук", text);
        Assert.DoesNotContain("{{", text);
        Assert.Contains("5. ШЕВЧЕНКО Тарас Григорович", text);
    }

    [Fact]
    public void RunPackage_PersonalDocx_WithoutChosenOfficer_FallsBackToTheFirstCourseOfficer()
    {
        using var db = new TestDb();
        var (packageId, _) = Seed(db);

        TestServices.Generation(db).RunPackage(
            packageId, _folder, new Dictionary<string, string>(),
            regenerateExisting: false, CadetOnly(db), NoProgress);

        Assert.Contains("Курсовий офіцер капітан Ковальчук В. П.", GeneratedText(db));
    }

    [Fact]
    public void CourseOfficerSignatureFor_OnlyQueriesWhenSomeMappingAsksForIt()
    {
        using var db = new TestDb();
        Seed(db);
        using var ctx = db.Factory.CreateDbContext();
        var mappings = ctx.TemplateFieldMappings.ToList();

        Assert.Equal("Курсовий офіцер капітан Ковальчук В. П.", GenerationService.CourseOfficerSignatureFor(ctx, mappings));
        Assert.Null(GenerationService.CourseOfficerSignatureFor(ctx, mappings.Where(m => m.PlaceholderTag == "{{піб}}")));
    }

    [Fact]
    public void PackageNeedsCourseOfficer_IsTrueForPersonalDocxAskingForTheSignature()
    {
        using var db = new TestDb();
        var (packageId, _) = Seed(db);

        Assert.True(TestServices.Generation(db).PackageNeedsCourseOfficer(packageId));
    }
}
