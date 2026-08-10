using ClosedXML.Excel;
using GenDoc.Models;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Generation;

// Тести ParsePeriodDates (розбір рядка «03.08.2026-05.08.2026, 09.08.2026» на дати) і
// repeatSheetPerDate (клонування аркуша шаблону на кожну дату періоду) в
// XlsxGenerationService. XlsxTemplateScan.ForGeneration бере (Row, Mappings) для
// справжньої Роздавальної відомості — той самий шлях, що й у Task 7/8.
public class PeriodSheetsTests
{
    // ── ParsePeriodDates ────────────────────────────────────────────

    [Fact]
    public void ParsePeriodDates_ExpandsRangeInclusively()
    {
        var dates = XlsxGenerationService.ParsePeriodDates("03.08.2026-05.08.2026");

        Assert.Equal(
            new[] { new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 4), new DateOnly(2026, 8, 5) },
            dates.ToArray());
    }

    [Fact]
    public void ParsePeriodDates_MixesRangesAndSingleDates_SortedAndDeduplicated()
    {
        var dates = XlsxGenerationService.ParsePeriodDates("05.08.2026, 03.08.2026-04.08.2026, 03.08.2026");

        Assert.Equal(
            new[] { new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 4), new DateOnly(2026, 8, 5) },
            dates.ToArray());
    }

    [Fact]
    public void ParsePeriodDates_ReversedRange_ThrowsWithReadableMessage()
    {
        var ex = Assert.Throws<FormatException>(
            () => XlsxGenerationService.ParsePeriodDates("05.08.2026-03.08.2026"));

        Assert.Contains("кінцева дата раніша за початкову", ex.Message);
    }

    [Fact]
    public void ParsePeriodDates_UnknownFormat_ThrowsWithReadableMessage()
    {
        var ex = Assert.Throws<FormatException>(
            () => XlsxGenerationService.ParsePeriodDates("2026-08-03"));

        Assert.Contains("дд.мм.рррр", ex.Message);
    }

    [Fact]
    public void ParsePeriodDates_Empty_ThrowsWithReadableMessage()
    {
        var ex = Assert.Throws<FormatException>(() => XlsxGenerationService.ParsePeriodDates("   "));
        Assert.Contains("порожній", ex.Message);
    }

    // ── repeatSheetPerDate ──────────────────────────────────────────

    private static Dictionary<string, string> RozdavalnaManualValues(string? period) =>
        new()
        {
            ["{{калібр}}"] = "5,45",
            ["{{кількість_патронів}}"] = "30",
            ["{{номер_відомості}}"] = "12",
            [XlsxGenerationService.PeriodTag] = period ?? string.Empty
        };

    [Fact]
    public void RepeatSheetPerDate_CreatesOneSheetPerDate_NamedByDate()
    {
        var (row, mappings) = XlsxTemplateScan.ForGeneration(TemplateFixtures.RozdavalnaXlsx);

        var result = new XlsxGenerationService().Generate(
            TemplateFixtures.Bytes(TemplateFixtures.RozdavalnaXlsx), row, usesPlaceholders: true,
            mappings, TemplateFixtures.Roster(3),
            org: new OrganizationSettings { UnitNumber = "А1234" },
            manualValues: RozdavalnaManualValues("03.08.2026-05.08.2026"),
            repeatSheetPerDate: true);

        Assert.True(result.Success, result.ErrorMessage);

        using var produced = new XLWorkbook(new MemoryStream(result.Content!));
        var names = produced.Worksheets.Select(s => s.Name).ToList();

        Assert.Contains("03.08.2026", names);
        Assert.Contains("04.08.2026", names);
        Assert.Contains("05.08.2026", names);
    }

    [Fact]
    public void RepeatSheetPerDate_FillsSheetDateTagPerSheet()
    {
        var (row, mappings) = XlsxTemplateScan.ForGeneration(TemplateFixtures.RozdavalnaXlsx);

        var result = new XlsxGenerationService().Generate(
            TemplateFixtures.Bytes(TemplateFixtures.RozdavalnaXlsx), row, usesPlaceholders: true,
            mappings, TemplateFixtures.Roster(2),
            org: new OrganizationSettings { UnitNumber = "А1234" },
            manualValues: RozdavalnaManualValues("03.08.2026-04.08.2026"),
            repeatSheetPerDate: true);

        Assert.True(result.Success, result.ErrorMessage);

        using var produced = new XLWorkbook(new MemoryStream(result.Content!));
        foreach (var sheetName in new[] { "03.08.2026", "04.08.2026" })
        {
            var text = produced.Worksheet(sheetName).RangeUsed()!.CellsUsed()
                .Select(c => c.GetString())
                .Aggregate(string.Empty, (a, b) => a + "\n" + b);

            Assert.Contains(sheetName, text);
            Assert.DoesNotContain(XlsxGenerationService.DateSheetTag, text);
        }
    }

    [Fact]
    public void RepeatSheetPerDate_PutsFullRosterOnEverySheet()
    {
        var roster = TemplateFixtures.Roster(3);
        var (row, mappings) = XlsxTemplateScan.ForGeneration(TemplateFixtures.RozdavalnaXlsx);

        var result = new XlsxGenerationService().Generate(
            TemplateFixtures.Bytes(TemplateFixtures.RozdavalnaXlsx), row, usesPlaceholders: true,
            mappings, roster,
            org: new OrganizationSettings { UnitNumber = "А1234" },
            manualValues: RozdavalnaManualValues("03.08.2026-04.08.2026"),
            repeatSheetPerDate: true);

        Assert.True(result.Success, result.ErrorMessage);

        using var produced = new XLWorkbook(new MemoryStream(result.Content!));
        foreach (var sheetName in new[] { "03.08.2026", "04.08.2026" })
        {
            var cells = produced.Worksheet(sheetName).RangeUsed()!.CellsUsed()
                .Select(c => c.GetString()).ToList();

            foreach (var person in roster)
                Assert.Contains(cells, t => t.Contains(person.LastName, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void RepeatSheetPerDate_WithoutPeriodTag_FailsWithReadableMessage()
    {
        var (row, mappings) = XlsxTemplateScan.ForGeneration(TemplateFixtures.RozdavalnaXlsx);

        var result = new XlsxGenerationService().Generate(
            TemplateFixtures.Bytes(TemplateFixtures.RozdavalnaXlsx), row, usesPlaceholders: true,
            mappings, TemplateFixtures.Roster(2),
            org: new OrganizationSettings { UnitNumber = "А1234" },
            manualValues: RozdavalnaManualValues(period: null),
            repeatSheetPerDate: true);

        Assert.False(result.Success);
        Assert.Contains(XlsxGenerationService.PeriodTag, result.ErrorMessage);
    }

    // Дефект C1: CollectResidualUnfilledTags проходить аркуш ще раз і додає
    // будь-який залишковий тег поверх уже порахованих — у зведенні з'являються
    // теги, які насправді підставились.
    [Fact(Skip = "Червоний до Task 15 — unfilledTags містить теги, які були заповнені")]
    public void UnfilledTags_DoNotRepeatTagsThatWereActuallyFilled()
    {
        var (row, mappings) = XlsxTemplateScan.ForGeneration(TemplateFixtures.RozdavalnaXlsx);

        var manual = RozdavalnaManualValues(period: null);
        manual.Remove(XlsxGenerationService.PeriodTag);
        manual["{{дата_аркуша}}"] = "07.08.2026";

        var result = new XlsxGenerationService().Generate(
            TemplateFixtures.Bytes(TemplateFixtures.RozdavalnaXlsx), row, usesPlaceholders: true,
            mappings, TemplateFixtures.Roster(2),
            org: new OrganizationSettings { UnitNumber = "А1234" },
            manualValues: manual,
            repeatSheetPerDate: false);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.DoesNotContain("{{калібр}}", result.UnfilledTags);
        Assert.DoesNotContain("{{дата_аркуша}}", result.UnfilledTags);
        Assert.DoesNotContain("{{номер_відомості}}", result.UnfilledTags);
    }
}
