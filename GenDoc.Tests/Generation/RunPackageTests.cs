using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Tests.Generation;

// RunPackage - оркестратор, що досі не мав жодного тесту. Тут покриваємо лише
// фазу персональних DOCX (по одному файлу на людину): завантаження ростера з
// фільтрами (усі комбінуються через AND), правило пропуску і формування імені
// файлу. Звітна (xlsx/груповий docx) поведінка - Task 11.
public class RunPackageTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-run-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static readonly IProgress<string> NoProgress = new Progress<string>(_ => { });

    // Пакет з одним персональним DOCX-шаблоном (справжній «індивідуальний рапорт»)
    // і заданим складом людей.
    private static (int PackageId, List<int> RecipientIds) SeedPackage(TestDb db, List<Recipient> people)
    {
        using var ctx = db.Factory.CreateDbContext();

        // GeneratedDocument.GeneratedByUserId - обов'язковий FK на UserProfile.
        // Заводимо користувача без явного Id, щоб SQLite сам призначив 1
        // (перевірений підхід з DocumentVersionChainTests - явний Id=1 при
        // автоінкременті поводиться інакше).
        ctx.Users.Add(new UserProfile { FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now });
        ctx.OrganizationSettings.Add(new OrganizationSettings
        {
            UnitNumber = "А1234", City = "Львів",
            CommanderRank = "полковник", CommanderFullName = "І. ПЕТРЕНКО",
            CommanderPosition = "начальник", HrOfficerFullName = "К. КАДРОВ",
            UnitFullName = "Військовий коледж"
        });

        var template = new Template
        {
            Name = "Шаблон_Рапорт_котлове_ІНДИВІДУАЛЬНИЙ",
            OriginalFileName = "rapport.docx",
            Content = TemplateFixtures.Bytes(TemplateFixtures.RaportIndividualDocx),
            UploadedAt = DateTime.Now,
            Kind = TemplateKind.PerRecipient
        };
        ctx.Templates.Add(template);
        ctx.Recipients.AddRange(people);
        ctx.SaveChanges();

        // Мапінги: беремо реальні теги шаблону, класифікуючи їх як у продакшні.
        // Шаблон містить 12 тегів - тут навмисно мапимо лише 7; решта 5 (ручні
        // поля) підуть у UnfilledTags, на що ці тести не зважають.
        foreach (var tag in new[]
                 {
                     "{{звання_зв}}", "{{піб_зв}}", "{{прибув}}", "{{таким}}",
                     "{{номер_посвідчення}}", "{{прод_атестат}}", "{{дата_посвідчення}}"
                 })
        {
            var (sourceType, fieldName) = Services.Templates.PlaceholderTagMaps.Classify(tag);
            ctx.TemplateFieldMappings.Add(new TemplateFieldMapping
            {
                TemplateId = template.Id,
                PlaceholderTag = tag,
                SourceType = sourceType,
                FieldName = fieldName,
                IsInsideRepeatingBlock = false
            });
        }

        var package = new GenerationPackage { Name = "Тестовий пакет" };
        package.Templates.Add(new GenerationPackageTemplate { TemplateId = template.Id, SortOrder = 0 });
        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();

        return (package.Id, people.Select(p => p.Id).ToList());
    }

    private RunResult Run(TestDb db, int packageId, bool regenerate = false, RosterSelection? selection = null)
        => TestServices.Generation(db).RunPackage(
            packageId, _folder, new Dictionary<string, string>(), regenerate,
            selection ?? RosterSelection.Everyone, NoProgress);

    [Fact]
    public void RunPackage_GeneratesOneFilePerPerson()
    {
        using var db = new TestDb();
        var (packageId, _) = SeedPackage(db, TemplateFixtures.Roster(3));

        var result = Run(db, packageId);

        Assert.Equal(3, result.Generated);
        Assert.Equal(0, result.Errors);
        Assert.Equal(3, Directory.GetFiles(_folder, "*.docx", SearchOption.AllDirectories).Length);
    }

    // Ім'я файлу: «ПРІЗВИЩЕ Ім'я Назва шаблону.docx», без технічного префікса «Шаблон_».
    [Fact]
    public void RunPackage_FileNameDropsTemplatePrefixAndUnderscores()
    {
        using var db = new TestDb();
        var (packageId, _) = SeedPackage(db, new List<Recipient>
        {
            TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас")
        });

        Run(db, packageId);

        // Назва шаблону тепер у ПАПЦІ, а не в імені файлу - розкладка по папках
        // (DocumentFolderLayout). Намір тесту той самий: технічний префікс
        // «Шаблон_» і підкреслення до назви не доходять.
        var file = Directory.GetFiles(_folder, "*.docx", SearchOption.AllDirectories).Single();
        // Через рівень вище: безпосередня тека файлу - це позначка прогону.
        var templateFolder = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(file)));

        Assert.Equal("Рапорт котлове ІНДИВІДУАЛЬНИЙ", templateFolder);
        Assert.StartsWith("ШЕВЧЕНКО Тарас", Path.GetFileName(file));
    }

    // Наскрізна перевірка розкладки: до цієї зміни всі документи лягали в корінь
    // обраної теки пласким списком.
    [Fact]
    public void RunPackage_PutsDocumentsIntoIntakeAndTemplateFolders()
    {
        using var db = new TestDb();
        var (packageId, _) = SeedPackage(db, new List<Recipient>
        {
            TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас")
        });

        Run(db, packageId);

        var file = Directory.GetFiles(_folder, "*.docx", SearchOption.AllDirectories).Single();
        var relative = Path.GetRelativePath(_folder, file);
        var parts = relative.Split(Path.DirectorySeparatorChar);

        // Чотири рівні: набір (для людини поза набором - «Постійний склад»),
        // тип документа, позначка прогону, файл на особу. Рівень прогону тут
        // ключовий - без нього повторна генерація затирала б попередню.
        Assert.Equal(4, parts.Length);
        Assert.Equal("Рапорт котлове ІНДИВІДУАЛЬНИЙ", parts[1]);
        Assert.StartsWith(DateTime.Now.ToString("yyyy-MM-dd"), parts[2]);
        Assert.EndsWith(".docx", parts[3]);
    }

    [Fact]
    public void RunPackage_TwoPeopleWithIdenticalNames_GetDistinctFileNames()
    {
        using var db = new TestDb();
        var (packageId, _) = SeedPackage(db, new List<Recipient>
        {
            TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас"),
            TemplateFixtures.Person(2, "ШЕВЧЕНКО", "Тарас")
        });

        var result = Run(db, packageId);

        Assert.Equal(2, result.Generated);
        Assert.Equal(2, Directory.GetFiles(_folder, "*.docx", SearchOption.AllDirectories).Length);
    }

    // Другий прогін без regenerateExisting нічого не робить, бо файли на місці.
    [Fact]
    public void RunPackage_SecondRun_SkipsWhenFilesStillPresent()
    {
        using var db = new TestDb();
        var (packageId, _) = SeedPackage(db, TemplateFixtures.Roster(2));

        Run(db, packageId);
        var second = Run(db, packageId);

        Assert.Equal(0, second.Generated);
        Assert.Equal(2, second.Skipped);
    }

    // А якщо файли з теки прибрали - має сформувати наново, інакше тека лишиться порожньою.
    // Це і є та причина, чому пропуск зважає на наявність запису в архіві ТА файлу
    // в теці одночасно, а не лише на запис.
    [Fact]
    public void RunPackage_SecondRun_RegeneratesWhenFilesWereRemovedFromFolder()
    {
        using var db = new TestDb();
        var (packageId, _) = SeedPackage(db, TemplateFixtures.Roster(2));

        Run(db, packageId);
        foreach (var file in Directory.GetFiles(_folder, "*.docx", SearchOption.AllDirectories)) File.Delete(file);

        var second = Run(db, packageId);

        Assert.Equal(2, second.Generated);
        Assert.Equal(0, second.Skipped);
    }

    [Fact]
    public void RunPackage_RegenerateExisting_BumpsVersionAndKeepsOneCurrent()
    {
        using var db = new TestDb();
        var (packageId, recipientIds) = SeedPackage(db, TemplateFixtures.Roster(1));

        Run(db, packageId);
        Run(db, packageId, regenerate: true);

        using var ctx = db.Factory.CreateDbContext();
        var docs = ctx.GeneratedDocuments
            .Where(g => g.RecipientId == recipientIds[0])
            .OrderBy(g => g.Version).ToList();

        Assert.Equal(new[] { 1, 2 }, docs.Select(d => d.Version).ToArray());
        Assert.Single(docs.Where(d => d.IsCurrent));
        Assert.Equal(2, docs.Single(d => d.IsCurrent).Version);
    }

    [Fact]
    public void RunPackage_SelectedRecipientsOnly_IgnoresTheRest()
    {
        using var db = new TestDb();
        var (packageId, recipientIds) = SeedPackage(db, TemplateFixtures.Roster(4));

        var result = Run(db, packageId, selection: new RosterSelection(
            AllRecipients: false,
            RecipientIds: new[] { recipientIds[0], recipientIds[2] },
            FitnessFilter: FitnessFilter.All,
            PermanentStaffOnly: false,
            RankCategories: Array.Empty<RankCategory>(),
            Ranks: Array.Empty<string>()));

        Assert.Equal(2, result.Generated);
    }

    [Fact]
    public void RunPackage_RankFilter_NarrowsRosterByExactRank()
    {
        using var db = new TestDb();
        var (packageId, _) = SeedPackage(db, new List<Recipient>
        {
            TemplateFixtures.Person(1, "ПЕРШИЙ", "Іван", "майор"),
            TemplateFixtures.Person(2, "ДРУГИЙ", "Петро", "капітан"),
            TemplateFixtures.Person(3, "ТРЕТІЙ", "Сидір", "майор")
        });

        var result = Run(db, packageId, selection: new RosterSelection(
            AllRecipients: true,
            RecipientIds: Array.Empty<int>(),
            FitnessFilter: FitnessFilter.All,
            PermanentStaffOnly: false,
            RankCategories: Array.Empty<RankCategory>(),
            Ranks: new[] { "майор" }));

        Assert.Equal(2, result.Generated);
    }

    // PermanentStaffOnly = лише люди без набору (IntakeId == null).
    [Fact]
    public void RunPackage_PermanentStaffOnly_ExcludesIntakeMembers()
    {
        using var db = new TestDb();
        var people = TemplateFixtures.Roster(3);
        var (packageId, recipientIds) = SeedPackage(db, people);

        using (var ctx = db.Factory.CreateDbContext())
        {
            var intake = new Intake { Number = 29, Status = IntakeStatus.Active };
            ctx.Intakes.Add(intake);
            ctx.SaveChanges();

            var member = ctx.Recipients.First(r => r.Id == recipientIds[0]);
            member.IntakeId = intake.Id;
            ctx.SaveChanges();
        }

        var result = Run(db, packageId, selection: new RosterSelection(
            AllRecipients: true,
            RecipientIds: Array.Empty<int>(),
            FitnessFilter: FitnessFilter.All,
            PermanentStaffOnly: true,
            RankCategories: Array.Empty<RankCategory>(),
            Ranks: Array.Empty<string>()));

        Assert.Equal(2, result.Generated);
    }

    [Fact]
    public void RunPackage_RecordsRunRowWithPackageLink()
    {
        using var db = new TestDb();
        var (packageId, _) = SeedPackage(db, TemplateFixtures.Roster(2));

        Run(db, packageId);

        using var ctx = db.Factory.CreateDbContext();
        var run = Assert.Single(ctx.GenerationPackageRuns.ToList());
        Assert.Equal(packageId, run.GenerationPackageId);
    }
}
