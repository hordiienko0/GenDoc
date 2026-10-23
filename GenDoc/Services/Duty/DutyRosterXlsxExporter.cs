using ClosedXML.Excel;

namespace GenDoc.Services.Duty;

/// <summary>
/// Лист «як на паперовому графіку»: шапка, «№ з/п», «П.І.Б.», числа днів за справжнім календарем,
/// у днях наряду номер зміни римськими (або «н», якщо так обрано), зелена заливка звільнених, унизу перезмінка, прибирання і години змін.
/// A4 альбомна, вписано в одну сторінку.
/// </summary>
public static class DutyRosterXlsxExporter
{
    public const string SheetName = "Графік";
    public const string DutyMark = "н";

    /// <summary>Колір документа (не UI): світло-зелений, як на паперовому графіку.</summary>
    public const string ExemptFillHex = "#C6E9D2";

    private const string FontName = "Times New Roman";
    private const int NumberColumn = 1;
    private const int NameColumn = 2;
    private const int FirstDayColumn = 3;

    public static void Save(DutyRosterData roster, string path)
    {
        using var workbook = Build(roster);
        workbook.SaveAs(path);
    }

    public static XLWorkbook Build(DutyRosterData roster)
    {
        var workbook = new XLWorkbook();
        BuildRosterSheet(workbook.Worksheets.Add(SheetName), roster);
        return workbook;
    }

    private static void BuildRosterSheet(IXLWorksheet sheet, DutyRosterData roster)
    {
        var exemptFill = XLColor.FromHtml(ExemptFillHex);
        var days = roster.Days.ToList();
        var lastColumn = FirstDayColumn + days.Count - 1;

        sheet.Style.Font.FontName = FontName;
        sheet.Style.Font.FontSize = 11;
        sheet.ShowGridLines = false;

        var row = 1;
        foreach (var line in DutyRosterText.TitleLines(roster.Heading, roster.DateFrom, roster.DateTo))
        {
            var title = sheet.Range(row, NumberColumn, row, lastColumn).Merge();
            title.FirstCell().Value = line;
            title.Style.Font.Bold = true;
            title.Style.Font.FontSize = 12;
            title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            row++;
        }

        var headerRow = row + 1;
        var number = sheet.Cell(headerRow, NumberColumn);
        number.Value = "№ з/п";
        number.Style.Font.FontSize = 7;
        number.Style.Font.Bold = true;
        number.Style.Alignment.WrapText = true;

        var name = sheet.Cell(headerRow, NameColumn);
        name.Value = "П.І.Б.";
        name.Style.Font.Bold = true;

        for (var d = 0; d < days.Count; d++)
        {
            var cell = sheet.Cell(headerRow, FirstDayColumn + d);
            cell.Value = days[d].Day;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontSize = 9;
        }

        sheet.Range(headerRow, NumberColumn, headerRow, lastColumn).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        sheet.Range(headerRow, NumberColumn, headerRow, lastColumn).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Row(headerRow).Height = 20;

        var shiftsByDay = days.ToDictionary(day => day, day => DutyRosterPlanner.ShiftsOfDay(roster, day));
        for (var m = 0; m < roster.Members.Count; m++)
        {
            var r = headerRow + 1 + m;
            var member = roster.Members[m];

            sheet.Cell(r, NumberColumn).Value = m + 1;
            sheet.Cell(r, NumberColumn).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            sheet.Cell(r, NameColumn).Value = member.DisplayName;
            sheet.Cell(r, NameColumn).Style.Font.FontSize = 11;

            if (member.ExemptWholePeriod)
                sheet.Range(r, NumberColumn, r, NameColumn).Style.Fill.BackgroundColor = exemptFill;

            for (var d = 0; d < days.Count; d++)
            {
                var cell = sheet.Cell(r, FirstDayColumn + d);
                if (roster.IsDuty(m, days[d]))
                {
                    cell.Value = DutyRosterPlanner.DutyText(roster, shiftsByDay[days[d]], m);
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontSize = 9;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                if (roster.IsExempt(m, days[d]))
                    cell.Style.Fill.BackgroundColor = exemptFill;
            }
        }

        var lastTableRow = headerRow + roster.Members.Count;
        var table = sheet.Range(headerRow, NumberColumn, lastTableRow, lastColumn);
        table.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        table.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        var footerRow = lastTableRow + 2;
        Bold(sheet.Cell(footerRow, NameColumn), $"Перезмінка: {roster.Footer.Changeover} год.");
        Bold(sheet.Cell(footerRow + 1, NameColumn), $"Прибирання: {roster.Footer.Cleaning} год.");

        var shiftsRow = footerRow + 4;
        var shifts = roster.Footer.Shifts;
        for (var k = 0; k < shifts.Count; k++)
        {
            var column = ShiftColumn(k, shifts.Count, days.Count);
            Bold(sheet.Cell(shiftsRow, column), $"{DutyRosterEngine.RomanShift(k + 1)} Зміна");
            Bold(sheet.Cell(shiftsRow + 1, column), shifts[k].Day);
            Bold(sheet.Cell(shiftsRow + 2, column), shifts[k].Night);
        }

        sheet.Column(NumberColumn).Width = 4.5;
        sheet.Column(NameColumn).Width = 22;
        for (var d = 0; d < days.Count; d++)
            sheet.Column(FirstDayColumn + d).Width = 2.9;

        var lastRow = shifts.Count > 0 ? shiftsRow + 2 : footerRow + 1;
        var page = sheet.PageSetup;
        page.PageOrientation = XLPageOrientation.Landscape;
        page.PaperSize = XLPaperSize.A4Paper;
        page.FitToPages(1, 1);
        page.CenterHorizontally = true;
        page.Margins.Left = 0.25;
        page.Margins.Right = 0.25;
        page.Margins.Top = 0.35;
        page.Margins.Bottom = 0.35;
        page.Margins.Header = 0;
        page.Margins.Footer = 0;
        page.PrintAreas.Add(1, NumberColumn, lastRow, lastColumn);
    }

    /// <summary>Нижче цього масштабу на A4 прізвища й «н» стають дрібними (менше ~9 pt).</summary>
    public const double ReadableScale = 0.8;

    /// <summary>
    /// Оцінка масштабу, з яким Excel впише аркуш в одну сторінку A4 (виміряно на прогонах:
    /// база ~157 pt + ~20.25 pt на день у ширину, ~187 pt + ~14 pt на людину у висоту; поле друку 806 × 545 pt).
    /// </summary>
    public static double PrintScale(int days, int people)
        => Math.Min(1.0, Math.Min(806.0 / (157 + 20.25 * days), 545.0 / (187 + 14.0 * people)));

    /// <summary>Блоки змін розкладено по ширині таблиці, перший - під «П.І.Б.», як на фото.</summary>
    internal static int ShiftColumn(int index, int count, int dayCount)
    {
        if (index == 0) return NameColumn;
        var column = FirstDayColumn + dayCount * index / count - 2;
        return Math.Max(FirstDayColumn, column);
    }

    private static void Bold(IXLCell cell, string text)
    {
        cell.Value = text;
        cell.Style.Font.Bold = true;
    }
}
