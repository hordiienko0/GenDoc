using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Documents;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Generation;

// Розкладка документів по папках — на СПРАВЖНЬОМУ диску, справжнім шаблоном.
//
// DocumentFolderLayout покритий власними тестами, але вони перевіряють саму
// функцію. Тут перевіряється те, чого вони не бачать: що генерація справді
// створює ці теки й кладе файли саме туди, і що ім'я, записане в архів,
// збігається зі шляхом на диску — інакше дерево «Архіву» показувало б не те,
// що лежить у папках.
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

        // Рівень прогону — одна тека на весь запуск, і назва її рахується
        // ОДИН раз, інакше документи розповзлися б по двох теках.
        var runFolder = Assert.Single(Directory.GetDirectories(typeFolder));

        var files = Directory.GetFiles(runFolder, "*.docx");
        Assert.Equal(2, files.Length);
        Assert.All(files, f => Assert.EndsWith(".docx", f, StringComparison.Ordinal));
    }

    // Головна умова, заради якої розкладку рахує ОДНА спільна функція: те, що
    // записано в архів, мусить збігатися зі шляхом на диску. Інакше дерево
    // «Архіву» показувало б не те, що лежить у папках.
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

    // Дерево «Архіву» будується розбором цього ж шляху — перевіряємо, що на
    // справжніх даних воно дає саме ті гілки, які є на диску.
    [Fact]
    public void ArchiveTree_BuiltFromStoredPaths_MatchesTheFoldersOnDisk()
    {
        using var db = new TestDb();
        var packageId = Seed(db);

        TestServices.Generation(db).RunPackage(
            packageId, _folder, new Dictionary<string, string>(),
            regenerateExisting: false, RosterSelection.Everyone, NoProgress);

        using var ctx = db.Factory.CreateDbContext();
        var tree = ArchiveFolderTree.Build(ctx.GeneratedDocuments.Select(g => g.FileName).ToList());

        var intake = Assert.Single(tree);
        Assert.Equal("Набір №15", intake.Name);
        Assert.Equal(2, intake.DocumentCount);

        var type = Assert.Single(intake.Children);
        Assert.Equal(TemplateName, type.Name);

        var run = Assert.Single(type.Children);
        Assert.True(
            Directory.Exists(Path.Combine(_folder, intake.Name, type.Name, run.Name)),
            $"Гілка дерева не відповідає теці на диску: {run.Name}");
    }
}
