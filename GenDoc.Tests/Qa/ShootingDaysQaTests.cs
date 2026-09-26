using ClosedXML.Excel;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Generation;
using static GenDoc.Tests.Qa.QaKomplekt;

namespace GenDoc.Tests.Qa;

/// <summary>
/// QA 2026-09-26, блок А: «Дні стрільб» (A3) і відомості «аркуш на кожен день» на 0 і 1 особу (A4).
/// </summary>
public class ShootingDaysQaTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gqd-{Guid.NewGuid():N}"[..14]);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private const string ShootingDatesTag = "{{дати_стрільб}}";

    private static readonly string[] PerDateLedgers =
    {
        "Шаблон_Відомість допуску до стрільб (Додаток 5).xlsx",
        "Шаблон_Відомість складання заліку з вимог безпеки (Додаток 8).xlsx",
        "Шаблон_Роздавально-здавальна відомість.xlsx"
    };

    public static IEnumerable<object[]> Ledgers() => PerDateLedgers.Select(f => new object[] { f });

    // ---------------------------------------------------------------------
    // A3. Розбір переліку днів
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("04.08.2026", "04.08.2026")]
    [InlineData("04.08.2026-06.08.2026", "04.08.2026,05.08.2026,06.08.2026")]
    [InlineData("04.08.2026-06.08.2026, 10.08.2026", "04.08.2026,05.08.2026,06.08.2026,10.08.2026")]
    [InlineData("30.07.2026-02.08.2026", "30.07.2026,31.07.2026,01.08.2026,02.08.2026")]
    [InlineData("30.12.2026-02.01.2027", "30.12.2026,31.12.2026,01.01.2027,02.01.2027")]
    [InlineData("05.08.2026, 04.08.2026, 05.08.2026, 04.08.2026-05.08.2026", "04.08.2026,05.08.2026")]
    [InlineData(" 04.08.2026 - 05.08.2026 ,, 10.08.2026 ", "04.08.2026,05.08.2026,10.08.2026")]
    [InlineData("28.02.2028-01.03.2028", "28.02.2028,29.02.2028,01.03.2028")]
    public void A3_ParsePeriodDates_ChronologicalWithoutDuplicates(string raw, string expected)
    {
        var dates = XlsxGenerationService.ParsePeriodDates(raw).Select(d => d.ToString("dd.MM.yyyy")).ToArray();

        Assert.Equal(expected.Split(','), dates);
    }

    [Theory]
    [InlineData("06.08.2026-04.08.2026", "раніша")]
    [InlineData("вересень", "дд.мм.рррр")]
    [InlineData("32.08.2026", "32.08.2026")]
    [InlineData("4.8.2026", "дд.мм.рррр")]
    [InlineData("04.08.26", "дд.мм.рррр")]
    [InlineData("04.08.2026-", "дд.мм.рррр")]
    [InlineData("04.08.2026; 05.08.2026", "дд.мм.рррр")]
    [InlineData(" , ", "порожній")]
    public void A3_ParsePeriodDates_Garbage_ThrowsReadableMessage(string raw, string mentions)
    {
        var ex = Assert.Throws<FormatException>(() => XlsxGenerationService.ParsePeriodDates(raw));

        Assert.Contains(mentions, ex.Message);
    }

    [Theory]
    [InlineData("04.08.2026–06.08.2026")]   // тире «–», як у підписі поля «з – по»
    [InlineData("04.08.2026 — 06.08.2026")]  // довге тире з Word
    public void A3_RangeTypedWithDash_AsInTheFieldLabel_IsUnderstood(string raw)
    {
        // Було (виправлено 2026-09-26): підпис поля - «Дні стрільб (з – по …)» з тире «–», але розбір знає лише дефіс «-»:
        // введений так діапазон дає помилку у відомостях і «сирий» текст у наказі.
        var dates = XlsxGenerationService.ParsePeriodDates(raw);

        Assert.Equal(3, dates.Count);
        Assert.Equal("04, 05, 06 серпня 2026 року", ManualTagClassifier.FormatPeriodValue(ShootingDatesTag, raw));
    }

    [Theory]
    [InlineData("04.08.2026", "04 серпня 2026 року")]
    [InlineData("04.08.2026, 04.08.2026", "04 серпня 2026 року")]
    [InlineData("10.08.2026, 04.08.2026-05.08.2026", "04, 05, 10 серпня 2026 року")]
    [InlineData("30.12.2026-02.01.2027", "30, 31 грудня 2026 року, 01, 02 січня 2027 року")]
    [InlineData("31.07.2026-01.08.2026", "31 липня, 01 серпня 2026 року")]
    public void A3_ShootingDatesInWords(string raw, string expected)
        => Assert.Equal(expected, ManualTagClassifier.FormatPeriodValue(ShootingDatesTag, raw));

    [Fact]
    public async Task A3_Form_PickersInReverseOrder_GiveForwardRange()
    {
        using var db = new TestDb();
        SeedPeople(db, _folder);
        var form = await FormBuilder(db).BuildAsync(new[] { ShootingDatesTag, XlsxGenerationService.PeriodTag }, "qa");
        var days = Assert.Single(form.Rows, r => r.Kind == ManualTagKind.Period);

        days.PeriodFrom = new DateTime(2026, 8, 6);
        days.PeriodTo = new DateTime(2026, 8, 4);
        var values = form.GetValues();

        Assert.Equal("04.08.2026-06.08.2026", values[XlsxGenerationService.PeriodTag]);
        Assert.Equal("04, 05, 06 серпня 2026 року", values[ShootingDatesTag]);
    }

    [Fact]
    public void A3_SheetsAcrossYearBoundary_WithDuplicates_AreChronologicalAndCarryTheirDate()
    {
        var path = KomplektFile("Стрільби", PerDateLedgers[0]);
        var result = GenerateLedger(path, TemplateFixtures.Roster(2), "02.01.2027, 30.12.2026-31.12.2026, 31.12.2026");

        Assert.True(result.Success, result.ErrorMessage);
        using var workbook = new XLWorkbook(new MemoryStream(result.Content!));
        var names = workbook.Worksheets.Select(w => w.Name).ToArray();
        Assert.Equal(new[] { "30.12.2026", "31.12.2026", "02.01.2027" }, names);

        foreach (var sheet in workbook.Worksheets)
        {
            var cells = SheetCells(sheet);
            Assert.Contains(cells, c => c.Contains(sheet.Name, StringComparison.Ordinal));
            Assert.DoesNotContain(cells, c => c.Contains("{{", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task A3_ReversedRangeInPackage_LedgersFailWithReadableMessage_NoLedgerFiles()
    {
        using var db = new TestDb();
        var seeded = SeedPeople(db, _folder);
        var packageId = ImportSet(db, "Стрільби");
        var generation = TestServices.Generation(db);

        var values = await FormValues(db, generation.GetManualTags(packageId), "06.08.2026-04.08.2026");
        var result = generation.RunPackage(packageId, _folder, values, regenerateExisting: true,
            Intake(seeded.IntakeId), NoProgress, courseOfficerId: CourseOfficerId);

        Assert.Equal(3, result.GroupErrors);
        Assert.All(result.Issues!.Where(i => i.Phase == RunIssue.PhaseXlsx),
            i => Assert.Contains("кінцева дата раніша за початкову", i.Message));
        Assert.Empty(Files(_folder, ".xlsx"));
    }

    // ---------------------------------------------------------------------
    // A4. Відомість «аркуш на день» на 1 і 0 осіб
    // ---------------------------------------------------------------------

    private static XlsxGenerationResult GenerateLedger(string path, IReadOnlyList<Recipient> roster, string period)
    {
        var (row, mappings) = XlsxTemplateScan.ForGeneration(path);
        var manual = mappings.Where(m => m.SourceType == MappingSourceType.Manual)
            .Select(m => m.PlaceholderTag).Distinct()
            .ToDictionary(t => t, t => "[" + t.Trim('{', '}') + "]");
        manual[XlsxGenerationService.PeriodTag] = period;

        return new XlsxGenerationService().Generate(
            TemplateFixtures.Bytes(path), row, usesPlaceholders: true, mappings, roster,
            new OrganizationSettings { UnitNumber = "А0000" }, manual,
            repeatSheetPerDate: true, courseOfficerSignature: "капітан Василь КОВАЛЬЧУК");
    }

    [Theory]
    [MemberData(nameof(Ledgers))]
    public void A4_SheetPerDate_OnePerson_EverySheetHasHimAndNoTags(string file)
    {
        var person = TemplateFixtures.Person(1, "ОДИНАК", "Іван");
        var result = GenerateLedger(KomplektFile("Стрільби", file), new[] { person }, "04.08.2026, 06.08.2026");

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(result.UnfilledTags);
        using var workbook = new XLWorkbook(new MemoryStream(result.Content!));
        Assert.Equal(new[] { "04.08.2026", "06.08.2026" }, workbook.Worksheets.Select(w => w.Name).ToArray());
        foreach (var sheet in workbook.Worksheets)
        {
            var cells = SheetCells(sheet);
            Assert.DoesNotContain(cells, c => c.Contains("{{", StringComparison.Ordinal));
            Assert.Single(cells, c => c.Contains("ОДИНАК", StringComparison.Ordinal));
            Assert.Contains(cells, c => c.Contains(sheet.Name, StringComparison.Ordinal));
        }
    }

    [Theory]
    [MemberData(nameof(Ledgers))]
    public void A4_SheetPerDate_ZeroPeople_DoesNotThrow_AndLeavesNoRowTags(string file)
    {
        var result = GenerateLedger(KomplektFile("Стрільби", file), Array.Empty<Recipient>(), "04.08.2026-05.08.2026");

        Assert.True(result.Success, result.ErrorMessage);
        using var workbook = new XLWorkbook(new MemoryStream(result.Content!));
        Assert.Equal(2, workbook.Worksheets.Count);

        // Було (виправлено 2026-09-26): на порожньому списку рядок-шаблон лишається з {{піб}}/{{звання}}… (FillSheetByPlaceholders
        // пропускає рядок при n == 0 і не прибирає його). Пакет порожню відомість не формує (див. нижче), але
        // «Особовий склад» → експорт за шаблоном із пошуком без збігів передає порожній список без перевірки.
        foreach (var sheet in workbook.Worksheets)
            Assert.DoesNotContain(SheetCells(sheet), c => c.Contains("{{", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A4_SheetPerDate_ZeroPeopleInPackage_IsSkippedWithoutErrorOrFile()
    {
        using var db = new TestDb();
        var seeded = SeedPeople(db, _folder);
        var packageId = ImportSet(db, "Стрільби");
        var generation = TestServices.Generation(db);

        var values = await FormValues(db, generation.GetManualTags(packageId), "04.08.2026");
        var nobody = new RosterSelection(false, new[] { 9999 }, FitnessFilter.All, false,
            Array.Empty<GenDoc.Services.RankCategory>(), Array.Empty<string>(), IntakeId: seeded.IntakeId);
        var result = generation.RunPackage(packageId, _folder, values, regenerateExisting: true,
            nobody, NoProgress, courseOfficerId: CourseOfficerId);

        Assert.Equal(0, result.Errors + result.GroupErrors + result.DocxGroupErrors);
        Assert.Equal(3, result.GroupSkipped);
        Assert.Empty(Files(_folder, ".xlsx"));
    }

    [Fact]
    public async Task A4_SheetPerDate_OnePersonInPackage_OneRowPerSheet()
    {
        using var db = new TestDb();
        var seeded = SeedPeople(db, _folder);
        var packageId = ImportSet(db, "Стрільби");
        var generation = TestServices.Generation(db);

        var values = await FormValues(db, generation.GetManualTags(packageId), "04.08.2026-05.08.2026");
        var result = generation.RunPackage(packageId, _folder, values, regenerateExisting: true,
            Intake(seeded.IntakeId, FitnessFilter.LimitedOnly), NoProgress, courseOfficerId: CourseOfficerId);

        Assert.Equal(0, result.GroupErrors);
        Assert.Equal(3, result.GroupGenerated);
        foreach (var file in Files(_folder, ".xlsx"))
        {
            using var workbook = new XLWorkbook(file);
            Assert.Equal(new[] { "04.08.2026", "05.08.2026" }, workbook.Worksheets.Select(w => w.Name).ToArray());
            foreach (var sheet in workbook.Worksheets)
            {
                var cells = SheetCells(sheet);
                Assert.DoesNotContain(cells, c => c.Contains("{{", StringComparison.Ordinal));
                Assert.Single(cells, c => c.Contains(LimitedSurname, StringComparison.Ordinal));
            }
        }
    }
}
