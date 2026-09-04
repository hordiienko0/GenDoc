using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Generation;
using GenDoc.Services.Templates;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Archive;

public class RegenerationOutputFolderTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-regen-folder-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static readonly IProgress<string> NoProgress = new Progress<string>(_ => { });

    private const string OrderTag = "{{номер_наказу}}";
    private const string TemplateName = "Наказ";

    private sealed record Seeded(int PackageId, int IntakeId, int PersonId, int TemplateId);

    private Seeded Seed(TestDb db, bool configureOutputFolder = true)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now });
        ctx.AppSettings.Add(new AppSettings { DefaultOutputFolder = configureOutputFolder ? _folder : null });

        var intake = new Intake { Number = 15, DisplayNumber = "Набір №15", Status = IntakeStatus.Active };
        ctx.Intakes.Add(intake);
        ctx.SaveChanges();

        var person = TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас");
        person.IntakeId = intake.Id;
        ctx.Recipients.Add(person);

        var template = new Template
        {
            Name = TemplateName, OriginalFileName = "nakaz.docx", Content = BuildDocx(),
            UploadedAt = DateTime.Now, Kind = TemplateKind.PerRecipient
        };
        ctx.Templates.Add(template);
        ctx.SaveChanges();

        foreach (var tag in new[] { "{{піб}}", OrderTag })
        {
            var (sourceType, fieldName) = PlaceholderTagMaps.Classify(tag);
            ctx.TemplateFieldMappings.Add(new TemplateFieldMapping
            {
                TemplateId = template.Id, PlaceholderTag = tag, SourceType = sourceType, FieldName = fieldName
            });
        }

        var package = new GenerationPackage { Name = "Пакет" };
        package.Templates.Add(new GenerationPackageTemplate { TemplateId = template.Id, SortOrder = 0 });
        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();

        return new Seeded(package.Id, intake.Id, person.Id, template.Id);
    }

    private static byte[] BuildDocx()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            doc.AddMainDocumentPart().Document = new Document(new Body(
                new Paragraph(new Run(new Text("{{піб}} наказ {{номер_наказу}}")))));
            doc.MainDocumentPart!.Document!.Save();
        }
        return stream.ToArray();
    }

    private static Dictionary<string, string> Order(string number) => new() { [OrderTag] = number };

    private RunResult RunPackage(TestDb db, int packageId, string number, bool regenerate = false)
        => TestServices.Generation(db).RunPackage(
            packageId, _folder, Order(number), regenerate, RosterSelection.Everyone, NoProgress);

    private static List<GeneratedDocument> Versions(TestDb db, int personId)
    {
        using var ctx = db.Factory.CreateDbContext();
        return ctx.GeneratedDocuments.Where(g => g.RecipientId == personId).OrderBy(g => g.Version).ToList();
    }

    private static byte[] Content(TestDb db, int documentId)
    {
        using var ctx = db.Factory.CreateDbContext();
        return ctx.GeneratedDocumentContents.Single(c => c.GeneratedDocumentId == documentId).Content;
    }

    private static string DocxText(byte[] content)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(content), false);
        return string.Concat(doc.MainDocumentPart!.Document!.Body!.Descendants<Text>().Select(t => t.Text));
    }

    [Fact]
    public async Task PersonCard_FirstGeneration_WritesTheFileUnderTheRunLayout()
    {
        using var db = new TestDb();
        var s = Seed(db);

        var result = await TestServices.Completeness(db).GenerateForPairAsync(s.PersonId, s.TemplateId, Order("5"));

        Assert.True(result.Success, result.ErrorMessage);
        var doc = Assert.Single(Versions(db, s.PersonId));
        Assert.StartsWith(@"Набір №15\" + TemplateName + @"\", doc.FileName, StringComparison.Ordinal);
        Assert.EndsWith(".docx", doc.FileName, StringComparison.Ordinal);
        var onDisk = Path.Combine(_folder, doc.FileName);
        Assert.True(File.Exists(onDisk), $"Файл не записано: {onDisk}");
        Assert.Contains("наказ 5", DocxText(File.ReadAllBytes(onDisk)));
    }

    [Fact]
    public async Task PersonCard_Regeneration_KeepsThePathAndReplacesTheFileOnDisk()
    {
        using var db = new TestDb();
        var s = Seed(db);
        RunPackage(db, s.PackageId, "5");
        var first = Assert.Single(Versions(db, s.PersonId));

        var result = await TestServices.Completeness(db).GenerateForPairAsync(s.PersonId, s.TemplateId, Order("6"));

        Assert.True(result.Success, result.ErrorMessage);
        var second = Assert.Single(Versions(db, s.PersonId), v => v.IsCurrent);
        Assert.Equal(2, second.Version);
        Assert.Equal(first.FileName, second.FileName);
        var onDisk = File.ReadAllBytes(Path.Combine(_folder, second.FileName));
        Assert.Contains("наказ 6", DocxText(onDisk));
        Assert.Equal(Content(db, second.Id), onDisk);
    }

    [Fact]
    public async Task Archive_Regeneration_KeepsThePathAndReplacesTheFileOnDisk()
    {
        using var db = new TestDb();
        var s = Seed(db);
        RunPackage(db, s.PackageId, "5");
        var first = Assert.Single(Versions(db, s.PersonId));

        var result = await TestServices.Archive(db).RegenerateAsync(first.Id, Order("7"));

        Assert.True(result.Success, result.ErrorMessage);
        var second = Assert.Single(Versions(db, s.PersonId), v => v.IsCurrent);
        Assert.Equal(first.FileName, second.FileName);
        Assert.Contains(@"\", second.FileName);
        var onDisk = File.ReadAllBytes(Path.Combine(_folder, second.FileName));
        Assert.Contains("наказ 7", DocxText(onDisk));
        Assert.Equal(Content(db, second.Id), onDisk);
    }

    [Fact]
    public async Task Archive_Regeneration_AfterMoveToAnotherIntake_PlacesTheFileUnderTheNewIntakeFolder()
    {
        using var db = new TestDb();
        var s = Seed(db);
        RunPackage(db, s.PackageId, "5");
        var first = Assert.Single(Versions(db, s.PersonId));

        using (var ctx = db.Factory.CreateDbContext())
        {
            var other = new Intake { Number = 16, DisplayNumber = "Набір №16", Status = IntakeStatus.Active };
            ctx.Intakes.Add(other);
            ctx.SaveChanges();
            ctx.Recipients.First(r => r.Id == s.PersonId).IntakeId = other.Id;
            ctx.SaveChanges();
        }

        var result = await TestServices.Archive(db).RegenerateAsync(first.Id, Order("8"));

        Assert.True(result.Success, result.ErrorMessage);
        var second = Assert.Single(Versions(db, s.PersonId), v => v.IsCurrent);
        Assert.StartsWith(@"Набір №16\" + TemplateName + @"\", second.FileName, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_folder, second.FileName)));
        Assert.True(File.Exists(Path.Combine(_folder, first.FileName)));
    }

    [Fact]
    public async Task PersonCard_Regeneration_ManualUploadWithBareName_GetsAFreshPlaceInTheLayout()
    {
        using var db = new TestDb();
        var s = Seed(db);
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.GeneratedDocuments.Add(new GeneratedDocument
            {
                RecipientId = s.PersonId, TemplateId = s.TemplateId, IntakeId = s.IntakeId,
                GeneratedAt = DateTime.Now, GeneratedByUserId = 1, FileName = "підписаний.pdf", SizeBytes = 3,
                Version = 1, IsCurrent = true, SourceType = DocumentSourceType.ManualUpload, HasContent = true,
                Content = new GeneratedDocumentContent { Content = new byte[] { 1, 2, 3 } }
            });
            ctx.SaveChanges();
        }

        var result = await TestServices.Completeness(db).GenerateForPairAsync(s.PersonId, s.TemplateId, Order("9"));

        Assert.True(result.Success, result.ErrorMessage);
        var second = Assert.Single(Versions(db, s.PersonId), v => v.IsCurrent);
        Assert.StartsWith(@"Набір №15\" + TemplateName + @"\", second.FileName, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_folder, second.FileName)));
    }

    [Fact]
    public async Task WithoutConfiguredOutputFolder_NothingIsWrittenToDisk()
    {
        using var db = new TestDb();
        var s = Seed(db, configureOutputFolder: false);

        var generated = await TestServices.Completeness(db).GenerateForPairAsync(s.PersonId, s.TemplateId, Order("5"));
        var first = Assert.Single(Versions(db, s.PersonId));
        var regenerated = await TestServices.Archive(db).RegenerateAsync(first.Id, Order("6"));

        Assert.True(generated.Success && regenerated.Success);
        Assert.False(Directory.Exists(_folder));
        var versions = Versions(db, s.PersonId);
        Assert.Equal(2, versions.Count);
        Assert.All(versions, v => Assert.EndsWith(".docx", v.FileName, StringComparison.Ordinal));
        Assert.Equal(first.FileName, versions[1].FileName);
    }

    [Fact]
    public async Task PackageRun_AfterPersonCardRegeneration_SkipsInsteadOfAddingAnotherVersion()
    {
        using var db = new TestDb();
        var s = Seed(db);
        RunPackage(db, s.PackageId, "5");
        await TestServices.Completeness(db).GenerateForPairAsync(s.PersonId, s.TemplateId, Order("5"));

        var third = RunPackage(db, s.PackageId, "5");

        Assert.Equal(0, third.Generated);
        Assert.Equal(1, third.Skipped);
        Assert.Equal(2, Versions(db, s.PersonId).Count);
    }
}
