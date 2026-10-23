using ClosedXML.Excel;
using GenDoc.Models.Enums;
using GenDoc.Services.Duty;

namespace GenDoc.Tests.Duty;

public class DutyRosterXlsxExporterTests
{
    private static readonly DateOnly From = new(2026, 9, 19);
    private static readonly DateOnly To = new(2026, 10, 22);
    private const int HeaderRow = 5;

    private static DutyRosterData Roster()
    {
        var roster = new DutyRosterData { DateFrom = From, DateTo = To, Seed = 7 };
        foreach (var name in new[] { "БУРЛА В.М.", "ОКОМАНЮК В.В.", "БОРИСЮК А.В.", "ВАСЬКО Є.І.", "ЛІМОНОВ Д.Є.", "ПОПЕНКО В.В." })
            roster.Members.Add(new DutyRosterMemberData { DisplayName = name });

        roster.Members[0].ExemptWholePeriod = true;
        roster.Members[4].Exemptions[new DateOnly(2026, 9, 24)] = DutyCellSource.Manual;
        roster.ExemptDays.Add(new DateOnly(2026, 9, 30));
        roster.Members[1].Duties[new DateOnly(2026, 9, 24)] = DutyCellSource.Manual;
        roster.Members[2].Duties[new DateOnly(2026, 9, 24)] = DutyCellSource.Auto;
        roster.Members[3].Duties[new DateOnly(2026, 9, 24)] = DutyCellSource.Auto;
        return roster;
    }

    private static int DayColumn(DateOnly day) => 3 + day.DayNumber - From.DayNumber;

    [Fact]
    public void TitleIsHeadingPlusPeriodLine()
    {
        using var workbook = DutyRosterXlsxExporter.Build(Roster());
        var sheet = workbook.Worksheet(DutyRosterXlsxExporter.SheetName);

        Assert.Equal("Графік чергувань особового складу", sheet.Cell(1, 1).GetString());
        Assert.Equal("курсів перепідготовки та підвищення кваліфікації ХНУПС", sheet.Cell(2, 1).GetString());
        Assert.Equal("на вересень-жовтень 2026 р.", sheet.Cell(3, 1).GetString());
        Assert.True(sheet.Cell(1, 1).IsMerged());
        Assert.True(sheet.Cell(1, 1).Style.Font.Bold);
    }

    [Fact]
    public void HeaderFollowsTheRealCalendarWithoutSeptember31()
    {
        using var workbook = DutyRosterXlsxExporter.Build(Roster());
        var sheet = workbook.Worksheet(DutyRosterXlsxExporter.SheetName);

        Assert.Equal("№ з/п", sheet.Cell(HeaderRow, 1).GetString());
        Assert.Equal("П.І.Б.", sheet.Cell(HeaderRow, 2).GetString());

        var days = Enumerable.Range(3, 34).Select(c => (int)sheet.Cell(HeaderRow, c).GetDouble()).ToList();
        var expected = Enumerable.Range(19, 12).Concat(Enumerable.Range(1, 22)).ToList();
        Assert.Equal(expected, days);
        Assert.True(sheet.Cell(HeaderRow, 37).IsEmpty());
    }

    [Fact]
    public void RowsAreNumberedWithoutRepeatsAndCarryDutyMarks()
    {
        var roster = Roster();
        using var workbook = DutyRosterXlsxExporter.Build(roster);
        var sheet = workbook.Worksheet(DutyRosterXlsxExporter.SheetName);

        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 },
            Enumerable.Range(HeaderRow + 1, 6).Select(r => (int)sheet.Cell(r, 1).GetDouble()));
        Assert.Equal("ПОПЕНКО В.В.", sheet.Cell(HeaderRow + 6, 2).GetString());

        var column = DayColumn(new DateOnly(2026, 9, 24));
        Assert.Equal("І", sheet.Cell(HeaderRow + 2, column).GetString());
        Assert.Equal("ІІ", sheet.Cell(HeaderRow + 3, column).GetString());
        Assert.Equal("ІІІ", sheet.Cell(HeaderRow + 4, column).GetString());
        Assert.True(sheet.Cell(HeaderRow + 5, column).IsEmpty());
    }

    [Fact]
    public void ExemptCellsWholeRowsAndWholeDaysAreGreen()
    {
        using var workbook = DutyRosterXlsxExporter.Build(Roster());
        var sheet = workbook.Worksheet(DutyRosterXlsxExporter.SheetName);
        var green = XLColor.FromHtml(DutyRosterXlsxExporter.ExemptFillHex);

        Assert.Equal(green, sheet.Cell(HeaderRow + 1, 2).Style.Fill.BackgroundColor);
        Assert.Equal(green, sheet.Cell(HeaderRow + 1, DayColumn(new DateOnly(2026, 10, 5))).Style.Fill.BackgroundColor);
        Assert.Equal(green, sheet.Cell(HeaderRow + 5, DayColumn(new DateOnly(2026, 9, 24))).Style.Fill.BackgroundColor);
        Assert.Equal(green, sheet.Cell(HeaderRow + 3, DayColumn(new DateOnly(2026, 9, 30))).Style.Fill.BackgroundColor);
        Assert.NotEqual(green, sheet.Cell(HeaderRow + 3, DayColumn(new DateOnly(2026, 9, 29))).Style.Fill.BackgroundColor);
        Assert.NotEqual(green, sheet.Cell(HeaderRow, DayColumn(new DateOnly(2026, 9, 30))).Style.Fill.BackgroundColor);
    }

    [Fact]
    public void FooterHasChangeoverCleaningAndShiftLegend()
    {
        using var workbook = DutyRosterXlsxExporter.Build(Roster());
        var sheet = workbook.Worksheet(DutyRosterXlsxExporter.SheetName);
        var texts = sheet.CellsUsed().Select(c => c.GetString()).ToList();

        Assert.Contains("Перезмінка: 08:00 год.", texts);
        Assert.Contains("Прибирання: 22:30 - 23:30 год.", texts);
        Assert.Contains("І Зміна", texts);
        Assert.Contains("ІІ Зміна", texts);
        Assert.Contains("ІІІ Зміна", texts);
        Assert.Contains("0:00 - 04:00", texts);

        var firstShift = sheet.CellsUsed(c => c.GetString() == "І Зміна").Single();
        Assert.Equal(2, firstShift.Address.ColumnNumber);
        Assert.Equal("8:00 - 12:00", firstShift.CellBelow().GetString());
        Assert.Equal("20:00 - 00:00", firstShift.CellBelow().CellBelow().GetString());
    }

    [Fact]
    public void PrintsOnOneLandscapeA4Page()
    {
        using var workbook = DutyRosterXlsxExporter.Build(Roster());
        var page = workbook.Worksheet(DutyRosterXlsxExporter.SheetName).PageSetup;

        Assert.Equal(XLPageOrientation.Landscape, page.PageOrientation);
        Assert.Equal(XLPaperSize.A4Paper, page.PaperSize);
        Assert.Equal(1, page.PagesWide);
        Assert.Equal(1, page.PagesTall);
        Assert.Single(page.PrintAreas);
    }

    [Fact]
    public void DutyMarkOnlyPrintsJustNInOneSheet()
    {
        var roster = Roster();
        roster.DutyMarkOnly = true;
        using var workbook = DutyRosterXlsxExporter.Build(roster);
        var sheet = workbook.Worksheet(DutyRosterXlsxExporter.SheetName);

        Assert.Single(workbook.Worksheets);
        Assert.Equal(3, sheet.CellsUsed(c => c.GetString() == "н").Count());
        Assert.DoesNotContain(sheet.CellsUsed(), c => c.GetString() is "І" or "ІІ" or "ІІІ");
    }

    [Fact]
    public void MonthFitsA4AlmostFullSizeAndLongPeriodsAreFlagged()
    {
        Assert.Equal(1.0, DutyRosterXlsxExporter.PrintScale(31, 24));
        Assert.True(DutyRosterXlsxExporter.PrintScale(35, 24) >= 0.9);
        Assert.True(DutyRosterXlsxExporter.PrintScale(43, 26) >= DutyRosterXlsxExporter.ReadableScale - 0.05);
        Assert.True(DutyRosterXlsxExporter.PrintScale(62, 30) < DutyRosterXlsxExporter.ReadableScale);
        Assert.True(DutyRosterXlsxExporter.PrintScale(35, 40) < DutyRosterXlsxExporter.PrintScale(35, 24));
    }

    [Fact]
    public void SavedFileOpensAgain()
    {
        var path = Path.Combine(Path.GetTempPath(), $"duty-{Guid.NewGuid():N}.xlsx");
        try
        {
            DutyRosterXlsxExporter.Save(Roster(), path);
            using var workbook = new XLWorkbook(path);
            Assert.Equal("на вересень-жовтень 2026 р.", workbook.Worksheet(1).Cell(3, 1).GetString());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
