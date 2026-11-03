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

    private static string[] Texts(IXLWorksheet sheet, DateOnly day, params int[] members)
        => members.Select(m => sheet.Cell(HeaderRow + 1 + m, DayColumn(day)).GetString()).ToArray();

    /// <summary>Номери змін, які рахує планувальник (по черзі змін), - аркуш має друкувати саме їх.</summary>
    private static string[] PlannedShifts(DutyRosterData roster, DateOnly day, params int[] members)
        => members.Select(m => DutyRosterEngine.RomanShift(DutyRosterPlanner.ShiftsByDay(roster)[day][m])).ToArray();

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

        var day = new DateOnly(2026, 9, 24);
        Assert.Equal(PlannedShifts(roster, day, 1, 2, 3), Texts(sheet, day, 1, 2, 3));
        Assert.Equal(new[] { "І", "ІІ", "ІІІ" }, Texts(sheet, day, 1, 2, 3).OrderBy(t => t.Length));
        Assert.True(sheet.Cell(HeaderRow + 5, DayColumn(day)).IsEmpty());
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
    public void WeekendColumnsAreGreenAndKeepTheirDuties()
    {
        var roster = Roster();
        var saturday = new DateOnly(2026, 9, 26);
        roster.Members[2].Duties[saturday] = DutyCellSource.Auto;
        var green = XLColor.FromHtml(DutyRosterXlsxExporter.ExemptFillHex);

        using (var workbook = DutyRosterXlsxExporter.Build(roster))
        {
            var sheet = workbook.Worksheet(DutyRosterXlsxExporter.SheetName);
            Assert.Equal("І", sheet.Cell(HeaderRow + 3, DayColumn(saturday)).GetString());
            Assert.Equal(green, sheet.Cell(HeaderRow + 3, DayColumn(saturday)).Style.Fill.BackgroundColor);
            Assert.Equal(green, sheet.Cell(HeaderRow + 2, DayColumn(saturday)).Style.Fill.BackgroundColor);
        }

        roster.WeekendsGreen = false;
        using (var workbook = DutyRosterXlsxExporter.Build(roster))
        {
            var sheet = workbook.Worksheet(DutyRosterXlsxExporter.SheetName);
            Assert.NotEqual(green, sheet.Cell(HeaderRow + 2, DayColumn(saturday)).Style.Fill.BackgroundColor);
        }
    }

    [Fact]
    public void CanteenIsAYellowKOutsideTheShiftNumbering()
    {
        var roster = Roster();
        var day = new DateOnly(2026, 9, 24);
        roster.Members[5].Canteen.Add(day);
        using var workbook = DutyRosterXlsxExporter.Build(roster);
        var sheet = workbook.Worksheet(DutyRosterXlsxExporter.SheetName);

        var cell = sheet.Cell(HeaderRow + 6, DayColumn(day));
        Assert.Equal("к", cell.GetString());
        Assert.True(cell.Style.Font.Bold);
        Assert.Equal(XLColor.FromHtml(DutyRosterXlsxExporter.CanteenFillHex), cell.Style.Fill.BackgroundColor);
        Assert.Equal(new[] { "І", "ІІ", "ІІІ" }, Texts(sheet, day, 1, 2, 3).OrderBy(t => t.Length));
    }

    [Fact]
    public void ManualShiftIsPrintedAndOthersTakeTheFreeShifts()
    {
        var roster = Roster();
        var day = new DateOnly(2026, 9, 24);
        roster.Members[3].Duties[day] = DutyCellSource.Manual;
        roster.Members[3].Shifts[day] = 1;
        using var workbook = DutyRosterXlsxExporter.Build(roster);
        var sheet = workbook.Worksheet(DutyRosterXlsxExporter.SheetName);

        Assert.Equal("І", Texts(sheet, day, 3).Single());
        Assert.Equal(new[] { "ІІ", "ІІІ" }, Texts(sheet, day, 1, 2).OrderBy(t => t.Length));
        Assert.Equal(PlannedShifts(roster, day, 1, 2), Texts(sheet, day, 1, 2));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(7)]
    public void ShortPeriodSheetFitsTheHeadingAndEveryShiftBlock(int days)
    {
        var roster = Roster();
        roster.DateTo = From.AddDays(days - 1);
        using var workbook = DutyRosterXlsxExporter.Build(roster);
        var sheet = workbook.Worksheet(DutyRosterXlsxExporter.SheetName);
        var printArea = sheet.PageSetup.PrintAreas.Single();
        double Left(int column) => Enumerable.Range(1, column - 1).Sum(c => DutyRosterXlsxExporter.Pixels(sheet.Column(c).Width));

        // На друці (Excel → PDF) знак шапки ~7.5 px, «20:00 - 00:00» ~86 px.
        var tableWidth = Left(3 + days);
        Assert.True(tableWidth >= "курсів перепідготовки та підвищення кваліфікації ХНУПС".Length * 7.5, $"{tableWidth}");

        var blocks = sheet.CellsUsed(c => c.GetString().EndsWith(" Зміна")).ToList();
        Assert.Equal(new[] { "І Зміна", "ІІ Зміна", "ІІІ Зміна" }, blocks.Select(c => c.GetString()));
        var lefts = blocks.Select(c => Left(c.Address.ColumnNumber)).ToList();
        Assert.All(lefts.Zip(lefts.Skip(1)), pair => Assert.True(pair.Second - pair.First >= 90, $"{pair.First} → {pair.Second}"));
        Assert.All(blocks, c => Assert.True(c.Address.ColumnNumber <= printArea.LastColumn().ColumnNumber()));
        Assert.True(lefts[^1] + 86 <= Left(printArea.LastColumn().ColumnNumber() + 1));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(31)]
    public void EverySheetSpansTheA4WidthAndSitsInTheMiddle(int days)
    {
        var roster = Roster();
        roster.DateTo = From.AddDays(days - 1);
        using var workbook = DutyRosterXlsxExporter.Build(roster);
        var sheet = workbook.Worksheet(DutyRosterXlsxExporter.SheetName);

        var tablePoints = Enumerable.Range(1, 2 + days).Sum(c => DutyRosterXlsxExporter.Pixels(sheet.Column(c).Width))
                          * DutyRosterXlsxExporter.PointsPerPixel;
        Assert.InRange(tablePoints, 0.97 * DutyRosterXlsxExporter.PrintableWidthPoints, DutyRosterXlsxExporter.PrintableWidthPoints);
        Assert.Equal(2 + days, sheet.PageSetup.PrintAreas.Single().LastColumn().ColumnNumber());
        Assert.True(sheet.PageSetup.CenterHorizontally);
        Assert.True(sheet.PageSetup.CenterVertically);
    }

    [Fact]
    public void LongPeriodKeepsNarrowDayColumns()
    {
        using var workbook = DutyRosterXlsxExporter.Build(Roster());
        var sheet = workbook.Worksheet(DutyRosterXlsxExporter.SheetName);

        Assert.Equal(2.9, sheet.Column(3).Width);
        Assert.Equal(2.9, sheet.Column(36).Width);
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
        double Scale(int days, int people) => DutyRosterXlsxExporter.PrintScale(Sized(days, people));

        Assert.Equal(1.0, Scale(31, 24));
        Assert.True(Scale(35, 24) >= 0.9);
        Assert.True(Scale(43, 26) >= DutyRosterXlsxExporter.ReadableScale - 0.05);
        Assert.True(Scale(62, 30) < DutyRosterXlsxExporter.ReadableScale);
        Assert.True(Scale(35, 40) < Scale(35, 24));
    }

    /// <summary>Як на фото: 43 дні й 55 осіб - аркуш зменшено під висоту, а таблиця все одно на всю ширину.</summary>
    [Theory]
    [InlineData(43, 55)]
    [InlineData(31, 40)]
    [InlineData(62, 20)]
    [InlineData(31, 24)]
    public void SheetFillsTheWholePageNotJustOneSide(int days, int people)
    {
        using var workbook = DutyRosterXlsxExporter.Build(Sized(days, people));
        var sheet = workbook.Worksheet(DutyRosterXlsxExporter.SheetName);
        var area = sheet.PageSetup.PrintAreas.Single();

        var width = Enumerable.Range(1, area.LastColumn().ColumnNumber())
            .Sum(c => DutyRosterXlsxExporter.Pixels(sheet.Column(c).Width)) * DutyRosterXlsxExporter.PointsPerPixel;
        var height = Enumerable.Range(1, area.LastRow().RowNumber()).Sum(r => sheet.Row(r).Height);
        var scale = Math.Min(1.0, Math.Min(DutyRosterXlsxExporter.PrintableWidthPoints / width, DutyRosterXlsxExporter.PrintableHeightPoints / height));

        Assert.InRange(width * scale, 0.97 * DutyRosterXlsxExporter.PrintableWidthPoints, DutyRosterXlsxExporter.PrintableWidthPoints + 0.5);
        Assert.InRange(height * scale, 0.97 * DutyRosterXlsxExporter.PrintableHeightPoints, DutyRosterXlsxExporter.PrintableHeightPoints + 0.5);
        Assert.Equal(DutyRosterXlsxExporter.PrintScale(Sized(days, people)), scale, 2);
    }

    [Fact]
    public void FewPeopleGetTallerRowsButNotStripes()
    {
        using var workbook = DutyRosterXlsxExporter.Build(Sized(31, 3));
        var sheet = workbook.Worksheet(DutyRosterXlsxExporter.SheetName);

        Assert.Equal(20, sheet.Row(HeaderRow).Height);
        Assert.Equal(2 * 14.4, sheet.Row(HeaderRow + 1).Height, 2);
        Assert.Equal(14.4, sheet.Row(HeaderRow + 5).Height, 2);
    }

    private static DutyRosterData Sized(int days, int people)
    {
        var roster = new DutyRosterData { DateFrom = From, DateTo = From.AddDays(days - 1), Seed = 7 };
        for (var p = 1; p <= people; p++)
            roster.Members.Add(new DutyRosterMemberData { DisplayName = $"ПРІЗВИЩЕ{p} А.Б." });
        return roster;
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
