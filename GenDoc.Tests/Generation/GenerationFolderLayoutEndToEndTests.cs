using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Documents;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Generation;

public class GenerationFolderLayoutEndToEndTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-layout-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static readonly IProgress<string> NoProgress = new Progress<string>(_ => { });

    private const string TemplateName = "Рапорт котлове";

    private static int Seed(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();

        ctx.Users.Add(new UserProfile { Id = 1, FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now });

        var intake = new Intake { Number = 15, DisplayNumber = "Набір №15" };
        ctx.Intakes.Add(intake);
        ctx.SaveChanges();

        foreach (var person in TemplateFixtures.Roster(2))
        {
            person.IntakeId = intake.Id;
            ctx.Recipients.Add(person);
        }

        var template = new Template
        {
            Name = TemplateName,
            OriginalFileName = "raport.docx",
            Content = TemplateFixtures.Bytes(TemplateFixtures.RaportIndividualDocx),
            Kind = TemplateKind.PerRecipient,
            UploadedAt = DateTime.Now
        };
        ctx.Templates.Add(template);
        ctx.SaveChanges();

        var package = new GenerationPackage { Name = "Котлове" };
        package.Templates.Add(new GenerationPackageTemplate { TemplateId = template.Id, SortOrder = 0 });
        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();

        return package.Id;
    }

    [Fact]
    public void RunPackage_PutsEachDocumentUnderIntakeThenTypeThenRunStamp()
    {
        using var db = new TestDb();
        var packageId = Seed(db);

        var result = TestServices.Generation(db).RunPackage(
            packageId, _folder, new Dictionary<string, string>(),
            regenerateExisting: false, RosterSelection.Everyone, NoProgress);

        Assert.Equal(0, result.Errors);
        Assert.Equal(2, result.Generated);

        var intakeFolder = Path.Combine(_folder, "Набір №15");
        Assert.True(Directory.Exists(intakeFolder), $"Немає теки набору: {intakeFolder}");

        var typeFolder = Path.Combine(intakeFolder, TemplateName);
        Assert.True(Directory.Exists(typeFolder), $"Немає теки типу документа: {typeFolder}");

        var runFolder = Assert.Single(Directory.GetDirectories(typeFolder));

        var files = Directory.GetFiles(runFolder, "*.docx");
        Assert.Equal(2, files.Length);
        Assert.All(files, f => Assert.EndsWith(".docx", f, StringComparison.Ordinal));
    }

    [Fact]
    public void RunPackage_StoresTheSameRelativePathInTheArchiveAsOnDisk()
    {
        using var db = new TestDb();
        var packageId = Seed(db);

        TestServices.Generation(db).RunPackage(
            packageId, _folder, new Dictionary<string, string>(),
            regenerateExisting: false, RosterSelection.Everyone, NoProgress);

        using var ctx = db.Factory.CreateDbContext();
        var stored = ctx.GeneratedDocuments.Select(g => g.FileName).ToList();

        Assert.Equal(2, stored.Count);

        foreach (var relative in stored)
        {
            Assert.StartsWith(@"Набір №15\" + TemplateName + @"\", relative, StringComparison.Ordinal);
            Assert.True(
                File.Exists(Path.Combine(_folder, relative)),
                $"Архів посилається на файл, якого немає на диску: {relative}");
        }
    }

    [Fact]
    public void StoredPaths_HaveIntakeThenTypeThenRun_AndTheFoldersExistOnDisk()
    {
        using var db = new TestDb();
        var packageId = Seed(db);

        TestServices.Generation(db).RunPackage(
            packageId, _folder, new Dictionary<string, string>(),
            regenerateExisting: false, RosterSelection.Everyone, NoProgress);

        using var ctx = db.Factory.CreateDbContext();
        var stored = ctx.GeneratedDocuments.Select(g => g.FileName).ToList();

        Assert.Equal(2, stored.Count);

        foreach (var relative in stored)
        {
            var parts = relative.Split('\\');
            Assert.Equal(4, parts.Length);
            Assert.Equal("Набір №15", parts[0]);
            Assert.Equal(TemplateName, parts[1]);

            var runFolder = Path.Combine(_folder, parts[0], parts[1], parts[2]);
            Assert.True(Directory.Exists(runFolder), $"Немає теки прогону: {runFolder}");
        }

        Assert.Single(stored.Select(p => p.Split('\\')[2]).Distinct());
    }
}
