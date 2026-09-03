using ClosedXML.Excel;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Generation;

public class XlsxRealTemplateTests
{
    private static XLWorkbook Generate(
        string path, IReadOnlyList<Recipient> roster, Dictionary<string, string> manualValues,
        out XlsxGenerationResult result, string? courseOfficerSignature = null)
    {
        var (row, mappings) = XlsxTemplateScan.ForGeneration(path);
        result = new XlsxGenerationService().Generate(
            TemplateFixtures.Bytes(path), row, usesPlaceholders: true, mappings, roster,
            org: new OrganizationSettings { UnitNumber = "А1234", City = "Львів" },
            manualValues: manualValues,
            repeatSheetPerDate: false,
            courseOfficerSignature: courseOfficerSignature);

        Assert.True(result.Success, result.ErrorMessage);
        return new XLWorkbook(new MemoryStream(result.Content!));
    }

    private static List<string> AllCellText(XLWorkbook workbook)
        => workbook.Worksheets.First().RangeUsed()!.CellsUsed().Select(c => c.GetString()).ToList();

    [Fact]
    public void Zalik_ProducesOneRowPerPerson_AndKeepsHeaderOnce()
    {
        var roster = TemplateFixtures.Roster(5);
        using var produced = Generate(TemplateFixtures.ZalikXlsx, roster,
            new Dictionary<string, string>
            {
                ["{{дата_аркуша}}"] = "07.08.2026",
                ["{{причина_інструктажу}}"] = "плановий",
                ["{{опис_підрозділу}}"] = "1 курс",
                ["{{звання_начальника}}"] = "полковник",
                ["{{піб_начальника}}"] = "І. ПЕТРЕНКО"
            },
            out _, courseOfficerSignature: "майор В. КОВАЛЕНКО");

        var cells = AllCellText(produced);

        foreach (var person in roster)
            Assert.Contains(cells, t => t.Contains(person.LastName, StringComparison.Ordinal));

        var sheet = produced.Worksheets.First();
        for (var i = 0; i < roster.Count; i++)
            Assert.Contains(roster[i].LastName, sheet.Row(10 + i).CellsUsed().Select(c => c.GetString()).ToList()
                .Aggregate(string.Empty, (a, b) => a + b));

        var headerCount = cells.Count(t =>
            t.Contains("складання заліку зі знань вимог безпеки", StringComparison.Ordinal));
        Assert.Equal(1, headerCount);

        Assert.DoesNotContain(cells, t => t.Contains("{{", StringComparison.Ordinal));
    }

    [Fact]
    public void Rozdavalna_ProducesOneRowPerPerson()
    {
        var roster = TemplateFixtures.Roster(4);
        using var produced = Generate(TemplateFixtures.RozdavalnaXlsx, roster,
            new Dictionary<string, string>
            {
                ["{{дата_аркуша}}"] = "07.08.2026",
                ["{{калібр}}"] = "5,45",
                ["{{кількість_патронів}}"] = "30",
                ["{{номер_відомості}}"] = "12"
            },
            out _);

        var cells = AllCellText(produced);
        foreach (var person in roster)
            Assert.Contains(cells, t => t.Contains(person.LastName, StringComparison.Ordinal));

        Assert.DoesNotContain(cells, t => t.Contains("{{", StringComparison.Ordinal));
    }

    [Fact]
    public void Zalik_SignatureBlockShiftsDownByInsertedRowCount()
    {
        const int templateRow = 10;

        int SignatureRow(XLWorkbook workbook) => workbook.Worksheets.First().RangeUsed()!.CellsUsed()
            .Where(c => c.GetString().Contains("ПЕТРЕНКО", StringComparison.Ordinal))
            .Select(c => c.Address.RowNumber)
            .DefaultIfEmpty(-1)
            .Max();

        var roster = TemplateFixtures.Roster(6);

        using var single = Generate(TemplateFixtures.ZalikXlsx, TemplateFixtures.Roster(1),
            new Dictionary<string, string>
            {
                ["{{дата_аркуша}}"] = "07.08.2026",
                ["{{причина_інструктажу}}"] = "плановий",
                ["{{опис_підрозділу}}"] = "1 курс",
                ["{{звання_начальника}}"] = "полковник",
                ["{{піб_начальника}}"] = "І. ПЕТРЕНКО"
            }, out _, courseOfficerSignature: "майор В. КОВАЛЕНКО");

        using var many = Generate(TemplateFixtures.ZalikXlsx, roster,
            new Dictionary<string, string>
            {
                ["{{дата_аркуша}}"] = "07.08.2026",
                ["{{причина_інструктажу}}"] = "плановий",
                ["{{опис_підрозділу}}"] = "1 курс",
                ["{{звання_начальника}}"] = "полковник",
                ["{{піб_начальника}}"] = "І. ПЕТРЕНКО"
            }, out _, courseOfficerSignature: "майор В. КОВАЛЕНКО");

        var singleRow = SignatureRow(single);
        var manyRow = SignatureRow(many);
        Assert.True(singleRow > templateRow, "Не знайдено блок підписів у згенерованому файлі");
        Assert.Equal(singleRow + (roster.Count - 1), manyRow);
    }

    [Fact]
    public void Zalik_MergeBelowTemplateRow_ShiftsDownAndStaysMerged()
    {
        using var original = new XLWorkbook(new MemoryStream(TemplateFixtures.Bytes(TemplateFixtures.ZalikXlsx)));
        var originalMerges = original.Worksheets.First().MergedRanges
            .Select(m => m.RangeAddress.ToString() ?? string.Empty)
            .ToList();
        Assert.Equal(new[] { "A1:I3", "B4:I4", "B5:I5", "B6:I6", "B11:I11" }, originalMerges);

        using var produced = Generate(TemplateFixtures.ZalikXlsx, TemplateFixtures.Roster(5),
            new Dictionary<string, string>
            {
                ["{{дата_аркуша}}"] = "07.08.2026",
                ["{{причина_інструктажу}}"] = "плановий",
                ["{{опис_підрозділу}}"] = "1 курс",
                ["{{звання_начальника}}"] = "полковник",
                ["{{піб_начальника}}"] = "І. ПЕТРЕНКО"
            }, out _, courseOfficerSignature: "майор В. КОВАЛЕНКО");

        var producedMerges = produced.Worksheets.First().MergedRanges
            .Select(m => m.RangeAddress.ToString() ?? string.Empty)
            .ToList();

        Assert.Equal(new[] { "A1:I3", "B4:I4", "B5:I5", "B6:I6", "B15:I15" }, producedMerges);
    }

    [Fact]
    public void Dopusk_GradesAreStable_AndOverallIsRoundedAverageOfTheRow()
    {
        var roster = TemplateFixtures.Roster(3);
        var manual = new Dictionary<string, string>
        {
            ["{{номери_вправ}}"] = "1, 2",
            ["{{звання_начальника}}"] = "полковник",
            ["{{піб_начальника}}"] = "І. ПЕТРЕНКО",
            ["{{дата_аркуша}}"] = "07.08.2026"
        };

        using var first = Generate(TemplateFixtures.DopuskXlsx, roster, manual, out _);
        using var second = Generate(TemplateFixtures.DopuskXlsx, roster, manual, out _);

        var (row, mappings) = XlsxTemplateScan.ForGeneration(TemplateFixtures.DopuskXlsx);
        var gradeColumns = mappings
            .Where(m => m.FieldKey == nameof(ExportFieldKey.GradeRandom34))
            .Select(m => m.ColumnIndex).Distinct().OrderBy(c => c).ToList();
        var overallColumn = mappings
            .Single(m => m.FieldKey == nameof(ExportFieldKey.GradeOverall34)).ColumnIndex;

        Assert.Equal(4, gradeColumns.Count);

        var firstSheet = first.Worksheets.First();
        var secondSheet = second.Worksheets.First();

        for (var i = 0; i < roster.Count; i++)
        {
            var targetRow = row + i;

            var grades = gradeColumns.Select(c => firstSheet.Cell(targetRow, c).GetDouble()).ToList();
            Assert.All(grades, g => Assert.InRange(g, 3, 4));

            foreach (var c in gradeColumns)
                Assert.Equal(firstSheet.Cell(targetRow, c).GetDouble(), secondSheet.Cell(targetRow, c).GetDouble());

            var expectedOverall = Math.Round(grades.Average(), MidpointRounding.AwayFromZero);
            Assert.Equal(expectedOverall, firstSheet.Cell(targetRow, overallColumn).GetDouble());
        }
    }
}
