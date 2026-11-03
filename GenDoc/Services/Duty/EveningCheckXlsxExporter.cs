using ClosedXML.Excel;

namespace GenDoc.Services.Duty;

/// <summary>
/// Лист «СПИСОК вечірньої перевірки» як паперовий: період розбито на сторінки по 31 дню; на кожній сторінці
/// для кожного набору блоки по 25 рядків («№ п/п», «в/зв», «Прізвище та ім’я», дати стовпчиком) з рядком
/// «Підпис особи, яка проводила перевірку» і підписом начальника, а в кінці сторінки - блок «ОБЛІКОВІ ДАНІ…».
/// Кожен блок - окрема сторінка друку A4 альбомна з полем під підшивку зверху, вписана у сторінку.
/// </summary>
public static class EveningCheckXlsxExporter
{
    public const string SheetName = "Список";

    /// <summary>Стовпчиків дат на одній сторінці - як у паперовому списку (місяць).</summary>
    public const int DaysPerPage = 31;

    /// <summary>
    /// «Хвостик» до 5 днів після повних сторінок іде на останню сторінку (до 36 стовпчиків), а не окремою
    /// майже порожньою сторінкою з кількома датами й широким пустим полем.
    /// </summary>
    public const int ExtraDaysOnLastPage = 5;

    public const int MaxDaysPerPage = DaysPerPage + ExtraDaysOnLastPage;

    /// <summary>Рядків у блоку: короткий набір доповнюється порожніми пронумерованими рядками, довгий - у кілька блоків.</summary>
    public const int RowsPerBlock = 25;

    /// <summary>Довший період - уже не «список на два місяці», а стос аркушів; і так не буває.</summary>
    public const int MaxDays = 184;

    internal const string FontName = "Times New Roman";
    internal const int NumberColumn = 1;
    internal const int RankColumn = 2;
    internal const int NameColumn = 3;
    internal const int FirstDayColumn = 4;
    internal const int LastDayColumn = FirstDayColumn + MaxDaysPerPage - 1;

    internal const double NumberWidth = 4.78;
    internal const double RankWidth = 19.11;
    internal const double NameWidth = 35.66;
    internal const double DayWidth = 3.33;

    /// <summary>Рядок дат вищий за паперовий (17.4): дата «10/11» повернута на 90° має вміститися, інакше Excel показує «###».</summary>
    internal const double DateRowHeight = 27;

    /// <summary>
    /// Поля друку, дюйми. Альбомний аркуш підшивається у справу верхнім краєм - зверху 30 мм під підшивку,
    /// з інших боків 0.4".
    /// </summary>
    internal const double BindingMargin = 30 / 25.4;
    internal const double SideMargin = 0.4;

    private const double PrintableWidthPoints = (297 / 25.4 - 2 * SideMargin) * 72;
    private const double PrintableHeightPoints = (210 / 25.4 - BindingMargin - SideMargin) * 72;

    public static void Save(EveningCheckData data, string path)
    {
        using var workbook = Build(data);
        workbook.SaveAs(path);
    }

    public static XLWorkbook Build(EveningCheckData data)
    {
        var workbook = new XLWorkbook();
        BuildSheet(workbook.Worksheets.Add(SheetName), data);
        return workbook;
    }

    /// <summary>
    /// Сторінки днів: по 31 дню від початку періоду; остання - коротша або, коли лишається до 36 днів,
    /// довша на той «хвостик» (<see cref="ExtraDaysOnLastPage"/>).
    /// </summary>
    public static IReadOnlyList<(DateOnly From, DateOnly To)> Pages(DateOnly from, DateOnly to)
    {
        var pages = new List<(DateOnly, DateOnly)>();
        var start = from;
        while (start <= to)
        {
            var remaining = to.DayNumber - start.DayNumber + 1;
            var length = remaining <= MaxDaysPerPage ? remaining : DaysPerPage;
            pages.Add((start, start.AddDays(length - 1)));
            start = start.AddDays(length);
        }
        return pages;
    }

    /// <summary>Довжини сторінок днів: «31 і 34» для періоду на 65 днів.</summary>
    public static IReadOnlyList<int> PageLengths(DateOnly from, DateOnly to)
        => Pages(from, to).Select(p => p.To.DayNumber - p.From.DayNumber + 1).ToList();

    /// <summary>Скільки блоків по 25 рядків займе набір; порожній набір - один блок із самих порожніх рядків.</summary>
    public static int BlockCount(int people) => Math.Max(1, (people + RowsPerBlock - 1) / RowsPerBlock);

    /// <summary>
    /// Таблиці набору в порядку друку. З поділом за придатністю, коли є і ті, і ті, - дві окремі таблиці зі своїми
    /// заголовками, блоками по 25 рядків і нумерацією з 1: спершу обмежено придатні, далі придатні. Інакше - набір як є.
    /// </summary>
    public static IReadOnlyList<EveningCheckGroup> Tables(EveningCheckGroup group)
    {
        if (!group.SplitByFitness || group.People.All(p => p.IsLimited) || group.People.All(p => !p.IsLimited))
            return new[] { group };

        return new[] { true, false }.Select(limited =>
        {
            var table = new EveningCheckGroup { Title = EveningCheckText.FitnessTitle(group.Title, limited) };
            table.People.AddRange(group.People.Where(p => p.IsLimited == limited));
            return table;
        }).ToList();
    }

    /// <summary>Блоків по 25 рядків у наборі - разом в обох таблицях, коли його поділено за придатністю.</summary>
    public static int BlocksOf(EveningCheckGroup group) => Tables(group).Sum(t => BlockCount(t.People.Count));

    /// <summary>Сторінок друку: на кожній сторінці днів - блоки всіх наборів і підсумковий блок.</summary>
    public static int PrintPages(EveningCheckData data)
        => Pages(data.DateFrom, data.DateTo).Count * (data.Groups.Sum(BlocksOf) + 1);

    private static void BuildSheet(IXLWorksheet sheet, EveningCheckData data)
    {
        sheet.Style.Font.FontName = FontName;
        sheet.Style.Font.FontSize = 10;

        sheet.Column(NumberColumn).Width = NumberWidth;
        sheet.Column(RankColumn).Width = RankWidth;
        sheet.Column(NameColumn).Width = NameWidth;
        for (var column = FirstDayColumn; column <= LastDayColumn; column++)
            sheet.Column(column).Width = DayWidth;

        var pages = Pages(data.DateFrom, data.DateTo);
        var tables = data.Groups.SelectMany(Tables).ToList();
        var row = 1;
        var tallestPage = 0.0;
        for (var p = 0; p < pages.Count; p++)
        {
            var days = DutyRosterEngine.Days(pages[p].From, pages[p].To).ToList();
            int start;
            foreach (var table in tables)
            {
                for (var block = 0; block < BlockCount(table.People.Count); block++)
                {
                    start = row;
                    row = WriteBlock(sheet, row, table.Title, table.People, block, days, data.Signature);
                    tallestPage = Math.Max(tallestPage, Height(sheet, start, row));
                    sheet.PageSetup.AddHorizontalPageBreak(row - 1);
                }
            }

            start = row;
            row = WriteSummary(sheet, row, days, data.Signature);
            tallestPage = Math.Max(tallestPage, Height(sheet, start, row));
            if (p < pages.Count - 1) sheet.PageSetup.AddHorizontalPageBreak(row - 1);
        }

        var page = sheet.PageSetup;
        page.PageOrientation = XLPageOrientation.Landscape;
        page.PaperSize = XLPaperSize.A4Paper;
        page.CenterHorizontally = true;
        page.Margins.Left = SideMargin;
        page.Margins.Right = SideMargin;
        page.Margins.Top = BindingMargin;
        page.Margins.Bottom = SideMargin;
        page.Margins.Header = 0.2;
        page.Margins.Footer = 0.2;
        // Поле друку - по найширшій сторінці: зайві стовпчики лише зменшили б масштаб решти.
        var widestPage = PageLengths(data.DateFrom, data.DateTo).Max();
        page.PrintAreas.Add(1, NumberColumn, Math.Max(1, row - 1), FirstDayColumn + widestPage - 1);

        // По ширині лист вписується сам. Короткий період - вузький лист з масштабом під 100%, і блок не влазить
        // по висоті під полем підшивки: тоді масштаб явний, щоб кожен блок лишався цілим на своїй сторінці.
        var widthPoints = new[] { NumberWidth, RankWidth, NameWidth }.Concat(Enumerable.Repeat(DayWidth, widestPage))
            .Sum(DutyRosterXlsxExporter.Pixels) * DutyRosterXlsxExporter.PointsPerPixel;
        var heightFit = PrintableHeightPoints / tallestPage;
        if (heightFit < Math.Min(1, PrintableWidthPoints / widthPoints)) page.Scale = (int)Math.Floor(heightFit * 100);
        else page.FitToPages(1, 0);
    }

    /// <summary>Висота рядків з <paramref name="from"/> до <paramref name="to"/> (не включно), pt.</summary>
    private static double Height(IXLWorksheet sheet, int from, int to)
        => Enumerable.Range(from, to - from).Sum(r => sheet.Row(r).Height);

    /// <summary>Блок таблиці: заголовок, шапка, 25 рядків (номери наскрізні через блоки таблиці), підписи. Повертає наступний вільний рядок.</summary>
    private static int WriteBlock(IXLWorksheet sheet, int row, string title, IReadOnlyList<EveningCheckPerson> people, int block, IReadOnlyList<DateOnly> days, string signature)
    {
        var lastColumn = FirstDayColumn + days.Count - 1;

        Title(sheet, row, lastColumn, title);
        row++;

        WriteTableHeader(sheet, row, days);
        var number = sheet.Range(row, NumberColumn, row + 1, NumberColumn);
        Text(sheet.Cell(row, NumberColumn), EveningCheckText.NumberHeader, 8, XLAlignmentHorizontalValues.Center);
        Text(sheet.Cell(row + 1, NumberColumn), EveningCheckText.NumberHeaderSecondLine, 8, XLAlignmentHorizontalValues.Center);
        number.Style.Alignment.WrapText = true;
        Text(sheet.Range(row, RankColumn, row + 1, RankColumn).Merge().FirstCell(), EveningCheckText.RankHeader, 10, XLAlignmentHorizontalValues.Center);
        Text(sheet.Range(row, NameColumn, row + 1, NameColumn).Merge().FirstCell(), EveningCheckText.NameHeader, 10, XLAlignmentHorizontalValues.Center);
        Borders(sheet.Range(row, NumberColumn, row + 1, lastColumn));
        // «№» і «п/п» - дві клітинки без лінії між ними, як у паперовому списку.
        sheet.Cell(row, NumberColumn).Style.Border.BottomBorder = XLBorderStyleValues.None;
        sheet.Cell(row + 1, NumberColumn).Style.Border.TopBorder = XLBorderStyleValues.None;
        row += 2;

        var first = block * RowsPerBlock;
        for (var i = 0; i < RowsPerBlock; i++)
        {
            var r = row + i;
            var index = first + i;
            sheet.Row(r).Height = 16.2;
            var cell = sheet.Cell(r, NumberColumn);
            cell.Value = index + 1;
            cell.Style.Font.FontSize = 12;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            if (index >= people.Count) continue;
            var person = people[index];
            Text(sheet.Cell(r, RankColumn), person.Rank, 10, XLAlignmentHorizontalValues.Center);
            var name = Text(sheet.Cell(r, NameColumn), person.FullName, 10, XLAlignmentHorizontalValues.Left);
            name.Style.Alignment.Indent = 1;
        }
        var body = sheet.Range(row, NumberColumn, row + RowsPerBlock - 1, lastColumn);
        body.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        Borders(body);
        row += RowsPerBlock;

        var checker = Text(sheet.Range(row, NumberColumn, row, NameColumn).Merge().FirstCell(),
            EveningCheckText.CheckerSignature, 16, XLAlignmentHorizontalValues.Center);
        checker.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Row(row).Height = 49.2;
        Borders(sheet.Range(row, NumberColumn, row, lastColumn));
        row++;

        sheet.Row(row).Height = 18;
        row++;
        Signature(sheet.Cell(row, NumberColumn), signature);
        row++;
        sheet.Row(row).Height = 18;
        sheet.Row(row + 1).Height = 18;
        return row + 2;
    }

    /// <summary>Підсумковий блок сторінки: «ОБЛІКОВІ ДАНІ…», ті самі дати і рядки обліку, що заповнюються від руки.</summary>
    private static int WriteSummary(IXLWorksheet sheet, int row, IReadOnlyList<DateOnly> days, string signature)
    {
        var lastColumn = FirstDayColumn + days.Count - 1;

        Title(sheet, row, lastColumn, EveningCheckText.SummaryTitle, bottomBorder: false);
        row++;
        sheet.Row(row).Height = 18.6;
        row++;

        WriteTableHeader(sheet, row, days);
        sheet.Range(row, NameColumn, row + 1, NameColumn).Merge();
        sheet.Row(row).Height = 28.2;
        Borders(sheet.Range(row, NameColumn, row + 1, lastColumn));
        row += 2;

        foreach (var label in EveningCheckText.SummaryRows)
        {
            Text(sheet.Cell(row, NameColumn), label, 10, XLAlignmentHorizontalValues.Left);
            sheet.Row(row).Height = 15.6;
            row++;
        }

        var checker = Text(sheet.Cell(row, NameColumn), EveningCheckText.CheckerSignature, 10, XLAlignmentHorizontalValues.Left);
        checker.Style.Font.Bold = true;
        sheet.Row(row).Height = 64.8;
        row++;

        var table = sheet.Range(row - EveningCheckText.SummaryRows.Count - 1, NameColumn, row - 1, lastColumn);
        table.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        Borders(table);

        sheet.Row(row).Height = 18;
        sheet.Row(row + 1).Height = 17.4;
        row += 2;
        Signature(sheet.Cell(row, NameColumn), signature);
        row++;
        sheet.Row(row).Height = 18;
        return row + 1;
    }

    /// <summary>«15/9», «10/11» - текстом, а не датою: текст у вузькій клітинці Excel ніколи не стає «###».</summary>
    internal static string DateText(DateOnly day) => $"{day.Day}/{day.Month}";

    /// <summary>Два рядки шапки дат: «ДАТА» над усіма днями і дати «д/м» стовпчиком (повернуті на 90°).</summary>
    private static void WriteTableHeader(IXLWorksheet sheet, int row, IReadOnlyList<DateOnly> days)
    {
        var lastColumn = FirstDayColumn + days.Count - 1;
        sheet.Row(row).Height = 18.6;
        sheet.Row(row + 1).Height = DateRowHeight;

        Text(sheet.Range(row, FirstDayColumn, row, lastColumn).Merge().FirstCell(), EveningCheckText.DateHeader, 14, XLAlignmentHorizontalValues.Center);
        for (var d = 0; d < days.Count; d++)
        {
            var cell = sheet.Cell(row + 1, FirstDayColumn + d);
            cell.Value = DateText(days[d]);
            cell.Style.Font.FontSize = 8;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Alignment.TextRotation = 90;
        }
    }

    private static void Title(IXLWorksheet sheet, int row, int lastColumn, string text, bool bottomBorder = true)
    {
        var title = sheet.Range(row, NumberColumn, row, lastColumn).Merge();
        Text(title.FirstCell(), text, 14, XLAlignmentHorizontalValues.Center);
        title.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        if (bottomBorder) title.Style.Border.BottomBorder = XLBorderStyleValues.Medium;
        sheet.Row(row).Height = 18.6;
    }

    private static void Signature(IXLCell cell, string text)
    {
        cell.Value = text;
        cell.Style.Font.FontSize = 14;
        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        cell.WorksheetRow().Height = 18;
    }

    private static IXLCell Text(IXLCell cell, string text, double size, XLAlignmentHorizontalValues horizontal)
    {
        cell.Value = text;
        cell.Style.Font.FontSize = size;
        cell.Style.Alignment.Horizontal = horizontal;
        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        cell.Style.Alignment.WrapText = true;
        return cell;
    }

    private static void Borders(IXLRange range)
    {
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
        range.Style.Border.InsideBorder = XLBorderStyleValues.Medium;
    }
}
