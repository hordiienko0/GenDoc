using System.Text.Json;
using ClosedXML.Excel;
using GenDoc.Models;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Generation;
using GenDoc.ViewModels.Templates;

namespace GenDoc.Tests.Generation;

public class PeriodInManualFormTests : IDisposable
{
    private const int UserId = 1;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-period-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static readonly IProgress<string> NoProgress = new Progress<string>(_ => { });

    private static int SeedRozdavalnaPackage(TestDb db, bool repeatSheetPerDate)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now });
        ctx.OrganizationSettings.Add(new OrganizationSettings { UnitNumber = "А1234", City = "Львів" });
        ctx.Recipients.AddRange(TemplateFixtures.Roster(3));

        var (row, mappings) = XlsxTemplateScan.ForGeneration(TemplateFixtures.RozdavalnaXlsx);
        var template = new ExportTemplate
        {
            Name = "Роздавальна відомість",
            OriginalFileName = "rozdavalna.xlsx",
            Content = TemplateFixtures.Bytes(TemplateFixtures.RozdavalnaXlsx),
            UsesPlaceholders = true,
            TemplateRowIndex = row,
            RepeatSheetPerDate = repeatSheetPerDate,
            UploadedAt = DateTime.Now
        };
        foreach (var mapping in mappings) template.ColumnMappings.Add(mapping);
        ctx.ExportTemplates.Add(template);
        ctx.SaveChanges();

        var package = new GenerationPackage { Name = "Пакет зі зброєю" };
        package.ExportTemplates.Add(new GenerationPackageExportTemplate
        {
            ExportTemplateId = template.Id, SortOrder = 0, FitnessFilter = Models.Enums.FitnessFilter.All
        });
        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();
        return package.Id;
    }

    [Fact]
    public void GetManualTags_RepeatSheetPerDate_OffersPeriodTagAfterTemplateTags()
    {
        using var db = new TestDb();
        var packageId = SeedRozdavalnaPackage(db, repeatSheetPerDate: true);

        var tags = TestServices.Generation(db).GetManualTags(packageId);

        Assert.Equal(XlsxGenerationService.PeriodTag, tags.Last());
        Assert.Contains("{{калібр}}", tags);
        Assert.Single(tags, t => t == XlsxGenerationService.PeriodTag);
    }

    [Fact]
    public void GetManualTags_WithoutRepeat_DoesNotOfferPeriodTag()
    {
        using var db = new TestDb();
        var packageId = SeedRozdavalnaPackage(db, repeatSheetPerDate: false);

        var tags = TestServices.Generation(db).GetManualTags(packageId);

        Assert.DoesNotContain(XlsxGenerationService.PeriodTag, tags);
    }

    [Fact]
    public void GetManualTagsForTemplates_RepeatSheetPerDate_OffersPeriodTag()
    {
        using var db = new TestDb();
        SeedRozdavalnaPackage(db, repeatSheetPerDate: true);
        int exportId;
        using (var ctx = db.Factory.CreateDbContext()) exportId = ctx.ExportTemplates.Select(t => t.Id).Single();

        var tags = TestServices.Generation(db).GetManualTagsForTemplates(Array.Empty<int>(), new[] { exportId });

        Assert.Contains(XlsxGenerationService.PeriodTag, tags);
    }

    [Fact]
    public async Task RunPackage_PeriodFromForm_ProducesOneSheetPerDate()
    {
        using var db = new TestDb();
        var packageId = SeedRozdavalnaPackage(db, repeatSheetPerDate: true);
        var service = TestServices.Generation(db);
        var builder = TestServices.ManualTagForm(
            db, TestServices.Staff(db, UserId), new FakeCurrentUser(), new FakeIntakeAccessor(), UserId);

        var form = await builder.BuildAsync(service.GetManualTags(packageId), $"pkg:{packageId}");
        var period = form.Rows.Single(r => r.Tag == XlsxGenerationService.PeriodTag);
        Assert.Equal(ManualTagKind.Period, period.Kind);
        period.PeriodFrom = new DateTime(2026, 8, 3);
        period.PeriodTo = new DateTime(2026, 8, 5);
        foreach (var row in form.Rows.Where(r => r.Kind == ManualTagKind.Text)) row.Value = "1";

        var result = service.RunPackage(
            packageId, _folder, form.GetValues(), regenerateExisting: false, RosterSelection.Everyone, NoProgress);

        Assert.Equal(0, result.GroupErrors);
        Assert.Equal(1, result.GroupGenerated);
        var file = Directory.GetFiles(_folder, "*.xlsx", SearchOption.AllDirectories).Single();
        using var produced = new XLWorkbook(file);
        Assert.Equal(new[] { "03.08.2026", "04.08.2026", "05.08.2026" }, produced.Worksheets.Select(s => s.Name).ToArray());
    }

    [Fact]
    public void Classify_PeriodTag_IsPeriod()
    {
        Assert.Equal(ManualTagKind.Period, ManualTagClassifier.Classify(XlsxGenerationService.PeriodTag));
        Assert.Equal(ManualTagKind.Period, ManualTagClassifier.Classify("період"));
    }

    [Fact]
    public void FormatPeriod_RangeAndSingleDay_RoundTripThroughParser()
    {
        Assert.Equal("03.08.2026-05.08.2026", XlsxGenerationService.FormatPeriod(new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 5)));
        Assert.Equal("03.08.2026", XlsxGenerationService.FormatPeriod(new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 3)));

        Assert.Equal(3, XlsxGenerationService.ParsePeriodDates("03.08.2026-05.08.2026").Count);
        Assert.True(XlsxGenerationService.TryParsePeriodBounds("03.08.2026-05.08.2026", out var from, out var to));
        Assert.Equal(new DateOnly(2026, 8, 3), from);
        Assert.Equal(new DateOnly(2026, 8, 5), to);
        Assert.True(XlsxGenerationService.TryParsePeriodBounds("03.08.2026", out from, out to));
        Assert.Equal(from, to);
        Assert.False(XlsxGenerationService.TryParsePeriodBounds("03.08.2026, 05.08.2026", out _, out _));
        Assert.False(XlsxGenerationService.TryParsePeriodBounds("вересень", out _, out _));
        Assert.False(XlsxGenerationService.TryParsePeriodBounds(null, out _, out _));
    }

    [Fact]
    public void PeriodRow_PickersWriteParserFormat()
    {
        var row = ManualTagRowViewModel.Period(XlsxGenerationService.PeriodTag, null);

        Assert.Equal(ManualTagKind.Period, row.Kind);
        Assert.Equal(string.Empty, row.Value);
        Assert.Null(row.PeriodFrom);

        row.PeriodFrom = new DateTime(2026, 8, 3);
        Assert.Equal("03.08.2026", row.Value);

        row.PeriodTo = new DateTime(2026, 8, 5);
        Assert.Equal("03.08.2026-05.08.2026", row.Value);
    }

    [Fact]
    public void PeriodRow_StoredRange_FillsPickers()
    {
        var row = ManualTagRowViewModel.Period(XlsxGenerationService.PeriodTag, "03.08.2026-05.08.2026");

        Assert.Equal(new DateTime(2026, 8, 3), row.PeriodFrom);
        Assert.Equal(new DateTime(2026, 8, 5), row.PeriodTo);
        Assert.Equal("03.08.2026-05.08.2026", row.Value);
    }

    [Fact]
    public void PeriodRow_StoredTextThatIsNotARange_IsKeptAsText()
    {
        var row = ManualTagRowViewModel.Period(XlsxGenerationService.PeriodTag, "03.08.2026, 05.08.2026");

        Assert.Null(row.PeriodFrom);
        Assert.Null(row.PeriodTo);
        Assert.Equal("03.08.2026, 05.08.2026", row.Value);
    }

    [Fact]
    public void PeriodRow_TypedRange_SyncsPickers()
    {
        var row = ManualTagRowViewModel.Period(XlsxGenerationService.PeriodTag, null);

        row.Value = "10.09.2026-12.09.2026";

        Assert.Equal(new DateTime(2026, 9, 10), row.PeriodFrom);
        Assert.Equal(new DateTime(2026, 9, 12), row.PeriodTo);
        Assert.Equal("10.09.2026-12.09.2026", row.Value);
    }

    [Fact]
    public async Task Form_PeriodValue_IsRememberedForNextTime()
    {
        using var db = new TestDb();
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Users.Add(new UserProfile { FullName = "Курсовий", PasswordHash = "x", CreatedAt = DateTime.Now });
            ctx.SaveChanges();
        }
        var builder = TestServices.ManualTagForm(
            db, TestServices.Staff(db, UserId), new FakeCurrentUser(), new FakeIntakeAccessor(), UserId);

        var form = await builder.BuildAsync(new[] { XlsxGenerationService.PeriodTag }, "pkg:1");
        var row = Assert.Single(form.Rows);
        row.PeriodFrom = new DateTime(2026, 8, 3);
        row.PeriodTo = new DateTime(2026, 8, 4);
        await builder.SaveAsync("pkg:1", form);

        var next = await builder.BuildAsync(new[] { XlsxGenerationService.PeriodTag }, "pkg:1");
        var stored = (await TestServices.UserSettings(db, UserId).GetForCurrentUserAsync()).LastManualValuesJson;

        Assert.Equal("03.08.2026-04.08.2026", Assert.Single(next.Rows).Value);
        Assert.Equal("03.08.2026-04.08.2026",
            JsonSerializer.Deserialize<Dictionary<string, string>>(stored!)![XlsxGenerationService.PeriodTag]);
    }

    [Fact]
    public void PeriodLabel_ReadsAsFromTo()
    {
        Assert.Equal("Період (з – по)", ManualTagRowViewModel.Period(XlsxGenerationService.PeriodTag, null).Label);
    }

    [Fact]
    public void MappingSavedMessage_MentionsPeriodOnlyWhenSheetRepeatsPerDate()
    {
        Assert.Equal("Мапінг колонок збережено.", TemplatesViewModel.MappingSavedMessage(repeatSheetPerDate: false));
        Assert.Equal(
            "Мапінг колонок збережено.\n\nАркуш повторюватиметься на кожну дату періоду: період «з – по» буде запитано під час генерації (рядок «Період» у значеннях для генерації).",
            TemplatesViewModel.MappingSavedMessage(repeatSheetPerDate: true));
    }
}
