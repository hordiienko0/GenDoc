using ClosedXML.Excel;

namespace GenDoc.Services.Duty;

/// <summary>
/// Лист «як на паперовому графіку»: шапка, «№ з/п», «П.І.Б.», числа днів за справжнім календарем,
/// у днях наряду номер зміни римськими (або «н», якщо так обрано), зелена заливка звільнених і вихідних,
/// жовта «к» - столова, унизу перезмінка, прибирання і години змін.
/// A4 альбомна, одна сторінка: таблиця на весь аркуш (і ширину, і висоту поля друку) і по центру.
/// </summary>
public static class DutyRosterXlsxExporter
{
    public const string SheetName = "Графік";
    public const string DutyMark = "н";
    public const string CanteenMark = "к";

    /// <summary>Колір документа (не UI): світло-зелений, як на паперовому графіку.</summary>
    public const string ExemptFillHex = "#C6E9D2";

    /// <summary>Колір документа (не UI): жовта заливка «к» наряду по столовій.</summary>
    public const string CanteenFillHex = "#FFFF00";

    internal const string FontName = "Times New Roman";
    internal const int NumberColumn = 1;
    internal const int NameColumn = 2;
    internal const int FirstDayColumn = 3;

    private const double NumberWidth = 4.5;
    private const double NameWidth = 22;
    private const double DayWidth = 2.9;

    /// <summary>Блок зміни в підвалі: «20:00 - 00:00» (~86 px) і проміжок до наступного.</summary>
    private const double ShiftBlockPixels = 100;

    /// <summary>Один знак жирної шапки 12 pt (виміряно на друці ~7.5 px) з запасом.</summary>
    private const double HeadingCharPixels = 8;

    /// <summary>Ширина поля друку: A4 альбомна мінус поля 0.25" з боків.</summary>
    public const double PrintableWidthPoints = 806;

    /// <summary>Висота поля друку: A4 альбомна мінус поля 0.35" згори й знизу.</summary>
    public const double PrintableHeightPoints = 545;

    /// <summary>Висоти рядків, pt: шапка графіка, рядок «№ з/п», решта (як рядок Excel під Times New Roman 11).</summary>
    internal const double TitleRowPoints = 16;
    internal const double HeaderRowPoints = 20;
    internal const double RowPoints = 14.4;

    /// <summary>Рядок людини вищає, поки лист не займе висоту аркуша, але на папері не вище ~1 см - кілька людей не стають смугами на пів сторінки.</summary>
    private const double MaxPrintedMemberRowPoints = 2 * RowPoints;

    /// <summary>
    /// Скільки pt на друці дає один піксель розрахунку <see cref="Pixels"/> (виміряно через Excel → PDF: ~0.81;
    /// база «№» і «П.І.Б.» ~158 pt і ~20.5 pt на вузький день).
    /// </summary>
    public const double PointsPerPixel = 0.81;

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

    internal enum SheetFill
    {
        None,
        Exempt,
        Canteen
    }

    /// <summary>
    /// Геометрія листа, однакова для Excel і друку (<c>Views/Duty/DutyRosterPrintLayout</c>): ширини колонок
    /// в одиницях ширини Excel, рядки (з 1, як в Excel), висота рядка людини в pt і колонки блоків змін у підвалі.
    /// </summary>
    internal sealed record SheetLayout(
        IReadOnlyList<string> TitleLines,
        IReadOnlyList<double> ColumnWidths,
        int LastTableColumn,
        int MemberCount,
        double MemberRowPoints,
        IReadOnlyList<int> ShiftColumns)
    {
        public int LastPrintColumn => ColumnWidths.Count;
        public int HeaderRow => TitleLines.Count + 2;
        public int LastTableRow => HeaderRow + MemberCount;
        public int FooterRow => LastTableRow + 2;
        public int ShiftsRow => FooterRow + 4;
        public int LastRow => ShiftColumns.Count > 0 ? ShiftsRow + 2 : FooterRow + 1;

        public double RowHeight(int row) => row <= TitleLines.Count ? TitleRowPoints
            : row == HeaderRow ? HeaderRowPoints
            : row > HeaderRow && row <= LastTableRow ? MemberRowPoints
            : RowPoints;

        /// <summary>Лист у натуральну величину на друці, pt: уся область друку.</summary>
        public double WidthPoints => ColumnWidths.Sum(Pixels) * PointsPerPixel;

        public double HeightPoints => Enumerable.Range(1, LastRow).Sum(RowHeight);

        /// <summary>З яким масштабом лист стане на одну сторінку A4 (Excel і друк лише зменшують, не збільшують).</summary>
        public double Scale => Math.Min(1.0, Math.Min(PrintableWidthPoints / WidthPoints, PrintableHeightPoints / HeightPoints));
    }

    /// <summary>
    /// Лист на весь аркуш: спершу найвужчий і найнижчий, що вміщує шапку й підвал, - з ним відомо, наскільки лист
    /// зменшиться до сторінки. Потім колонки днів ширшають, а рядки людей вищають, поки лист не набере пропорцій
    /// поля друку: зменшений він займає і всю ширину, і всю висоту, а не лише ту, що впирається першою.
    /// </summary>
    internal static SheetLayout Layout(DutyRosterData roster)
    {
        var dayCount = roster.DateTo.DayNumber - roster.DateFrom.DayNumber + 1;
        var titleLines = DutyRosterText.TitleLines(roster.Heading, roster.DateFrom, roster.DateTo);
        var shiftCount = roster.Footer.Shifts.Count;
        var memberCount = roster.Members.Count;

        var narrowest = Sheet(DayColumnWidth(dayCount, titleLines, shiftCount, pagePixels: 0), RowPoints);
        var scale = narrowest.Scale;
        var dayWidth = DayColumnWidth(dayCount, titleLines, shiftCount, PrintableWidthPoints / scale / PointsPerPixel);
        var otherRows = narrowest.HeightPoints - memberCount * RowPoints;
        var memberRow = memberCount == 0 ? RowPoints
            : Math.Clamp(Math.Floor((PrintableHeightPoints / scale - otherRows) / memberCount * 4) / 4,
                RowPoints, MaxPrintedMemberRowPoints / scale);
        return Sheet(dayWidth, memberRow);

        SheetLayout Sheet(double day, double member)
        {
            var lastTableColumn = FirstDayColumn + dayCount - 1;
            var shiftColumns = Enumerable.Range(0, shiftCount).Select(k => ShiftColumn(k, shiftCount, dayCount, day)).ToList();

            // На один день останній блок змін стає правіше таблиці - його колонка теж іде у друк.
            var lastPrintColumn = Math.Max(lastTableColumn, shiftColumns.DefaultIfEmpty(0).Max());
            var widths = new List<double> { NumberWidth, NameWidth };
            for (var column = FirstDayColumn; column <= lastPrintColumn; column++)
                widths.Add(column <= lastTableColumn ? day : ColumnWidth(ShiftBlockPixels));

            return new SheetLayout(titleLines, widths, lastTableColumn, memberCount, member, shiftColumns);
        }
    }

    /// <summary>Що стоїть у клітинці дня і як вона залита: наряд - номер зміни або «н», столова - жовта «к», звільнений і вихідний - зелене.</summary>
    internal static (string Text, SheetFill Fill) DayCell(
        DutyRosterData roster, IReadOnlyDictionary<int, int> shiftsOfDay, int memberIndex, DateOnly day)
    {
        var isDuty = roster.IsDuty(memberIndex, day);
        var isCanteen = !isDuty && roster.IsCanteen(memberIndex, day);
        var text = isDuty ? DutyRosterPlanner.DutyText(roster, shiftsOfDay, memberIndex) : isCanteen ? CanteenMark : string.Empty;
        var fill = isCanteen ? SheetFill.Canteen
            : roster.IsExempt(memberIndex, day) || roster.IsGreenWeekend(day) ? SheetFill.Exempt
            : SheetFill.None;
        return (text, fill);
    }

    private static void BuildRosterSheet(IXLWorksheet sheet, DutyRosterData roster)
    {
        var exemptFill = XLColor.FromHtml(ExemptFillHex);
        var canteenFill = XLColor.FromHtml(CanteenFillHex);
        var days = roster.Days.ToList();
        var layout = Layout(roster);
        var lastColumn = layout.LastTableColumn;

        sheet.Style.Font.FontName = FontName;
        sheet.Style.Font.FontSize = 11;
        sheet.ShowGridLines = false;

        for (var line = 0; line < layout.TitleLines.Count; line++)
        {
            var title = sheet.Range(line + 1, NumberColumn, line + 1, lastColumn).Merge();
            title.FirstCell().Value = layout.TitleLines[line];
            title.Style.Font.Bold = true;
            title.Style.Font.FontSize = 12;
            title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        var headerRow = layout.HeaderRow;
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

        var shiftsByDay = DutyRosterPlanner.ShiftsByDay(roster);
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
                var (text, fill) = DayCell(roster, shiftsByDay[days[d]], m, days[d]);
                if (text.Length > 0)
                {
                    cell.Value = text;
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontSize = 9;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                if (fill == SheetFill.Canteen)
                    cell.Style.Fill.BackgroundColor = canteenFill;
                else if (fill == SheetFill.Exempt)
                    cell.Style.Fill.BackgroundColor = exemptFill;
            }
        }

        var table = sheet.Range(headerRow, NumberColumn, layout.LastTableRow, lastColumn);
        table.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        table.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        Bold(sheet.Cell(layout.FooterRow, NameColumn), ChangeoverText(roster));
        Bold(sheet.Cell(layout.FooterRow + 1, NameColumn), CleaningText(roster));

        var shifts = roster.Footer.Shifts;
        for (var k = 0; k < shifts.Count; k++)
        {
            var column = layout.ShiftColumns[k];
            Bold(sheet.Cell(layout.ShiftsRow, column), ShiftTitle(k));
            Bold(sheet.Cell(layout.ShiftsRow + 1, column), shifts[k].Day);
            Bold(sheet.Cell(layout.ShiftsRow + 2, column), shifts[k].Night);
        }

        for (var column = 1; column <= layout.LastPrintColumn; column++)
            sheet.Column(column).Width = layout.ColumnWidths[column - 1];
        for (var row = 1; row <= layout.LastRow; row++)
            sheet.Row(row).Height = layout.RowHeight(row);

        var lastRow = layout.LastRow;
        var lastPrintColumn = layout.LastPrintColumn;
        var page = sheet.PageSetup;
        page.PageOrientation = XLPageOrientation.Landscape;
        page.PaperSize = XLPaperSize.A4Paper;
        page.FitToPages(1, 1);
        page.CenterHorizontally = true;
        page.CenterVertically = true;
        page.Margins.Left = 0.25;
        page.Margins.Right = 0.25;
        page.Margins.Top = 0.35;
        page.Margins.Bottom = 0.35;
        page.Margins.Header = 0;
        page.Margins.Footer = 0;
        page.PrintAreas.Add(1, NumberColumn, lastRow, lastPrintColumn);
    }

    /// <summary>Нижче цього масштабу на A4 прізвища й «н» стають дрібними (менше ~9 pt).</summary>
    public const double ReadableScale = 0.8;

    /// <summary>
    /// З яким масштабом Excel і друк впишуть графік в одну сторінку A4 (база ~158 pt + ~20.5 pt на день у ширину,
    /// ~198 pt + 14.4 pt на людину у висоту; поле друку 806 × 545 pt).
    /// </summary>
    public static double PrintScale(DutyRosterData roster) => Layout(roster).Scale;

    /// <summary>
    /// Колонка дня, щоб таблиця займала <paramref name="pagePixels"/> (з «№» і «П.І.Б.»): Excel лише зменшує аркуш
    /// до сторінки, а не збільшує - короткий період друкувався б дрібним блоком угорі. Не вужче за шапку й блоки змін
    /// у підвалі; 0 - найвужча, і довгий період лишає вузькі колонки.
    /// </summary>
    internal static double DayColumnWidth(int dayCount, IEnumerable<string> titleLines, int shiftCount, double pagePixels)
    {
        var fixedColumns = Pixels(NumberWidth) + Pixels(NameWidth);
        var page = pagePixels - fixedColumns;
        var heading = titleLines.Select(l => l.Length).DefaultIfEmpty(0).Max() * HeadingCharPixels - fixedColumns;
        var footer = Math.Max(0, shiftCount - 1) * ShiftBlockPixels;
        var perDay = Math.Max(page, Math.Max(heading, footer)) / Math.Max(1, dayCount);
        return Math.Max(DayWidth, Math.Floor(ColumnWidth(perDay) * 10) / 10);
    }

    /// <summary>
    /// Ширина колонки Excel - у знаках «0» стандартного шрифту (7 px) плюс ~5 px полів клітинки.
    /// Через ці поля вузькі колонки на друці ширші, ніж пропорційно їхній ширині.
    /// </summary>
    internal static double Pixels(double columnWidth) => 7 * columnWidth + 5;

    private static double ColumnWidth(double pixels) => Math.Max(0, pixels - 5) / 7;

    /// <summary>
    /// Блоки змін розкладено по ширині днів, перший - під «П.І.Б.», як на фото: наступні - на рівних частках
    /// ширини, трохи лівіше (на два вузькі дні). Кожен - не ближче, ніж на ширину блоку від попереднього.
    /// </summary>
    internal static int ShiftColumn(int index, int count, int dayCount, double dayWidth)
    {
        if (index == 0) return NameColumn;
        var dayPixels = Pixels(dayWidth);
        var daysPerBlock = (int)Math.Ceiling(ShiftBlockPixels / dayPixels);
        var x = dayCount * dayPixels * index / count - 2 * Pixels(DayWidth);
        var spread = FirstDayColumn + (int)Math.Floor(Math.Max(0, x) / dayPixels);
        return Math.Max(FirstDayColumn + (index - 1) * daysPerBlock, spread);
    }

    internal static string ChangeoverText(DutyRosterData roster) => $"Перезмінка: {roster.Footer.Changeover} год.";

    internal static string CleaningText(DutyRosterData roster) => $"Прибирання: {roster.Footer.Cleaning} год.";

    internal static string ShiftTitle(int index) => $"{DutyRosterEngine.RomanShift(index + 1)} Зміна";

    private static void Bold(IXLCell cell, string text)
    {
        cell.Value = text;
        cell.Style.Font.Bold = true;
    }
}
