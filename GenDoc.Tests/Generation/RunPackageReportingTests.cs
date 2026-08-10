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

    // Дефект B1: run.ErrorCount пишеться лише з docx-фази, тож помилка
    // XLSX-фази на екрані «Запуски» не видно взагалі.
    [Fact(Skip = "Червоний до Task 14 — лічильники запуску враховують лише docx-фазу")]
    public void RunPackage_PersistsCountersSummedAcrossAllThreePhases()
    {
        using var db = new TestDb();
        var packageId = SeedPackageWithFailingXlsx(db);

        var result = TestServices.Generation(db).RunPackage(
            packageId, _folder, new Dictionary<string, string>(),
            regenerateExisting: false, RosterSelection.Everyone, NoProgress);

        Assert.Equal(1, result.GroupErrors);

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
    [Fact(Skip = "Червоний до Task 14 — помилки запуску відновлюються розбором рядка")]
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
}
