using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Generation;

// Екран «Запуски» бере лічильники з рядка GenerationPackageRun, а перелік
// помилок відновлює з поля Summary. Обидва джерела зараз брешуть.
public class RunPackageReportingTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-report-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static readonly IProgress<string> NoProgress = new Progress<string>(_ => { });

    // Пакет з однією XLSX-відомістю зі свідомо пошкодженим вмістом — тобто
    // XLSX-фаза дасть рівно одну помилку.
    private static int SeedPackageWithFailingXlsx(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();

        ctx.Users.Add(new UserProfile { FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now });
        ctx.OrganizationSettings.Add(new OrganizationSettings
        {
            UnitNumber = "А1234", City = "Львів",
            CommanderRank = "полковник", CommanderFullName = "І. ПЕТРЕНКО",
            CommanderPosition = "начальник", HrOfficerFullName = "К. КАДРОВ",
            UnitFullName = "Військовий коледж"
        });
        ctx.Recipients.AddRange(TemplateFixtures.Roster(2));

        var broken = new ExportTemplate
        {
            Name = "Зламана відомість",
            OriginalFileName = "broken.xlsx",
            Content = new byte[] { 0x00, 0x01, 0x02, 0x03 }, // не є zip/xlsx
            UsesPlaceholders = true,
            TemplateRowIndex = 2,
            UploadedAt = DateTime.Now
        };
        broken.ColumnMappings.Add(new ExportTemplateColumnMapping
        {
            ColumnIndex = 1, HeaderText = string.Empty,
            FieldKey = nameof(ExportFieldKey.FullNameFormatted),
            PlaceholderTag = "{{піб}}", SourceType = MappingSourceType.Recipient
        });
        ctx.ExportTemplates.Add(broken);
        ctx.SaveChanges();

        var package = new GenerationPackage { Name = "Пакет зі зламаною відомістю" };
        package.ExportTemplates.Add(new GenerationPackageExportTemplate
        {
            ExportTemplateId = broken.Id, SortOrder = 0, FitnessFilter = FitnessFilter.All
        });
        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();

        return package.Id;
    }

    // Той самий пакет, але додатково зі зламаним ГРУПОВИМ DOCX-шаблоном
    // (Template.Kind = Group). Навіщо: у пакеті з самою лише XLSX-відомістю
    // фаза групового DOCX узагалі не запускається (нема жодного Template
    // з Kind = Group), тож docxGroup.Generated/Skipped/Errors лишаються 0 —
    // і "фікс", який підсумовує тільки docx + xlsx (забувши третю фазу),
    // усе одно пройшов би тест. Зламаний груповий DOCX-шаблон гарантує
    // docxGroup.Errors == 1, тож пропуск третьої фази стає видимим
    // розбіжністю в ErrorCount.
    private static int SeedPackageWithFailingXlsxAndGroupDocx(TestDb db)
    {
        var packageId = SeedPackageWithFailingXlsx(db);

        using var ctx = db.Factory.CreateDbContext();

        var brokenGroupDocx = new Template
        {
            Name = "Зламаний груповий рапорт",
            OriginalFileName = "broken-group.docx",
            Content = new byte[] { 0x00, 0x01, 0x02, 0x03 }, // не є zip/docx
            Kind = TemplateKind.Group,
            UploadedAt = DateTime.Now
        };
        ctx.Templates.Add(brokenGroupDocx);
        ctx.SaveChanges();

        var package = ctx.GenerationPackages.First(p => p.Id == packageId);
        package.Templates.Add(new GenerationPackageTemplate { TemplateId = brokenGroupDocx.Id, SortOrder = 0 });
        ctx.SaveChanges();

        return packageId;
    }

    // Дефект B1: run.ErrorCount пишеться лише з docx-фази, тож помилка
    // XLSX-фази і фази групового DOCX на екрані «Запуски» не видно взагалі.
    [Fact]
    public void RunPackage_PersistsCountersSummedAcrossAllThreePhases()
    {
        using var db = new TestDb();
        var packageId = SeedPackageWithFailingXlsxAndGroupDocx(db);

        var result = TestServices.Generation(db).RunPackage(
            packageId, _folder, new Dictionary<string, string>(),
            regenerateExisting: false, RosterSelection.Everyone, NoProgress);

        // Три фази дають: docx — 0 (нема жодного PerRecipient-шаблону в пакеті),
        // xlsx — 1 помилка (зламана відомість), груповий docx — 1 помилка
        // (зламаний груповий шаблон). Це і є "внесок" кожної фази в підсумок.
        Assert.Equal(0, result.Errors);
        Assert.Equal(1, result.GroupErrors);
        Assert.Equal(1, result.DocxGroupErrors);

        using var ctx = db.Factory.CreateDbContext();
        var run = Assert.Single(ctx.GenerationPackageRuns.ToList());

        Assert.Equal(
            result.Errors + result.GroupErrors + result.DocxGroupErrors,
            run.ErrorCount);
        Assert.Equal(
            result.Generated + result.GroupGenerated + result.DocxGroupGenerated,
            run.GeneratedCount);
        Assert.Equal(
            result.Skipped + result.GroupSkipped + result.DocxGroupSkipped,
            run.SkippedCount);
    }

    // Дефект B2: рядок «ГРУПА: Назва: текст» розбирається як ПІБ = «ГРУПА»,
    // шаблон = «—», а текст обрізається на першій двокрапці.
    [Fact]
    public async Task GetRunItemsAsync_ReportsGroupPhaseErrorWithTemplateNameIntact()
    {
        using var db = new TestDb();
        var packageId = SeedPackageWithFailingXlsx(db);

        TestServices.Generation(db).RunPackage(
            packageId, _folder, new Dictionary<string, string>(),
            regenerateExisting: false, RosterSelection.Everyone, NoProgress);

        int runId;
        using (var ctx = db.Factory.CreateDbContext())
            runId = ctx.GenerationPackageRuns.Select(r => r.Id).Single();

        var items = await TestServices.Archive(db).GetRunItemsAsync(runId);

        var errorItem = Assert.Single(items.Where(i => i.IsError));
        Assert.Equal("Зламана відомість", errorItem.TemplateName);
        Assert.DoesNotContain("ГРУПА", errorItem.Person, StringComparison.Ordinal);
    }

    // Зворотна сумісність: у робочих базах уже є запуски, чий Summary — звичайний
    // текст у старому форматі (до переходу на JSON). RunIssue.TryDeserialize має
    // відхилити такий рядок (він не починається з '['), а GetRunItemsAsync —
    // впасти на запасний текстовий парсер, а не мовчки загубити історію помилок.
    [Fact]
    public async Task GetRunItemsAsync_FallsBackToLegacyTextFormat_ForRunsPredatingJsonSummary()
    {
        using var db = new TestDb();

        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Users.Add(new UserProfile { FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now });
            var package = new GenerationPackage { Name = "Старий пакет" };
            ctx.GenerationPackages.Add(package);
            ctx.SaveChanges();

            ctx.GenerationPackageRuns.Add(new GenerationPackageRun
            {
                GenerationPackageId = package.Id,
                RunAt = DateTime.Now,
                RunByUserId = 1,
                GeneratedCount = 0,
                SkippedCount = 0,
                ErrorCount = 1,
                // Старий текстовий формат: "ПІБ / Шаблон: помилка".
                Summary = "ІВАНЕНКО Іван / Наказ про відрядження: файл шаблону пошкоджено"
            });
            ctx.SaveChanges();
        }

        int runId;
        using (var ctx = db.Factory.CreateDbContext())
            runId = ctx.GenerationPackageRuns.Select(r => r.Id).Single();

        var items = await TestServices.Archive(db).GetRunItemsAsync(runId);

        var errorItem = Assert.Single(items.Where(i => i.IsError));
        Assert.Equal("ІВАНЕНКО Іван", errorItem.Person);
        Assert.Equal("Наказ про відрядження", errorItem.TemplateName);
        Assert.Equal("помилка: файл шаблону пошкоджено", errorItem.Status);
    }
}
