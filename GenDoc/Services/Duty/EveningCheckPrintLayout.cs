using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;

namespace GenDoc.Services.Duty;

/// <summary>
/// Сторінки списку вечірньої перевірки у WPF - для попереднього перегляду у вкладці й для друку/PDF.
/// Той самий лист, що в Excel (<see cref="EveningCheckXlsxExporter"/>): кожен блок і підсумок «Облікові дані» -
/// окрема сторінка A4 альбомна, ті самі ширини колонок, висоти рядків, тексти й рамки.
/// </summary>
public static class EveningCheckPrintLayout
{
    /// <summary>A4 альбомна в одиницях WPF (1/96 дюйма).</summary>
    public static readonly Size PageSize = new(297 / 25.4 * 96, 210 / 25.4 * 96);

    /// <summary>Поля - як у Excel: зверху 30 мм під підшивку, з інших боків 0.4".</summary>
    public static readonly Thickness PageMargin = new(
        EveningCheckXlsxExporter.SideMargin * 96, EveningCheckXlsxExporter.BindingMargin * 96,
        EveningCheckXlsxExporter.SideMargin * 96, EveningCheckXlsxExporter.SideMargin * 96);

    private const double DipPerPixel = DutyRosterXlsxExporter.PointsPerPixel * 96 / 72;
    private const double Point = 96.0 / 72;
    private const double Line = 1.5;
    private const double CellPadding = 3;

    private static readonly Brush Ink = Brushes.Black;

    public static Size PrintableSize => new(
        PageSize.Width - PageMargin.Left - PageMargin.Right,
        PageSize.Height - PageMargin.Top - PageMargin.Bottom);

    /// <summary>Листи в натуральну величину, по одному на кожен блок і підсумок - у тому ж порядку, що в Excel.</summary>
    public static IReadOnlyList<FrameworkElement> Sheets(EveningCheckData data)
    {
        var sheets = new List<FrameworkElement>();
        var tables = data.Groups.SelectMany(EveningCheckXlsxExporter.Tables).ToList();
        foreach (var (from, to) in EveningCheckXlsxExporter.Pages(data.DateFrom, data.DateTo))
        {
            var days = DutyRosterEngine.Days(from, to).ToList();
            foreach (var table in tables)
            {
                for (var block = 0; block < EveningCheckXlsxExporter.BlockCount(table.People.Count); block++)
                    sheets.Add(BlockSheet(table.Title, table.People, block, days, data.Signature));
            }
            sheets.Add(SummarySheet(days, data.Signature));
        }
        return sheets;
    }

    /// <summary>Сторінка A4 з листом угорі поля друку по центру, зменшеним, якщо не влазить.</summary>
    public static FrameworkElement Page(FrameworkElement sheet)
    {
        var fit = new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Child = sheet
        };
        var area = new Grid { Width = PrintableSize.Width, Height = PrintableSize.Height, Margin = PageMargin };
        area.Children.Add(fit);
        return new Border { Width = PageSize.Width, Height = PageSize.Height, Background = Brushes.White, Child = area };
    }

    /// <summary>Документ для друку: усі сторінки списку.</summary>
    public static FixedDocument Document(EveningCheckData data)
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = PageSize;
        foreach (var sheet in Sheets(data))
        {
            var page = new FixedPage { Width = PageSize.Width, Height = PageSize.Height, Background = Brushes.White };
            page.Children.Add(Page(sheet));
            page.Measure(PageSize);
            page.Arrange(new Rect(PageSize));
            page.UpdateLayout();

            var content = new PageContent();
            ((IAddChild)content).AddChild(page);
            document.Pages.Add(content);
        }
        return document;
    }

    private static Grid BlockSheet(string title, IReadOnlyList<EveningCheckPerson> people, int block, IReadOnlyList<DateOnly> days, string signature)
    {
        var heights = new List<double> { 18.6, 18.6, EveningCheckXlsxExporter.DateRowHeight };
        heights.AddRange(Enumerable.Repeat(16.2, EveningCheckXlsxExporter.RowsPerBlock));
        heights.AddRange(new[] { 49.2, 18.0, 18.0 });
        var sheet = NewSheet(days.Count, heights);
        var last = EveningCheckXlsxExporter.FirstDayColumn + days.Count - 1;
        const int firstRow = 2;

        var heading = new Border { BorderBrush = Ink, BorderThickness = new Thickness(0, 0, 0, Line), Child = Text(title, 14, HorizontalAlignment.Center) };
        Place(sheet, heading, 1, 1, last);

        var number = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        number.Children.Add(Text(EveningCheckText.NumberHeader, 8, HorizontalAlignment.Center));
        number.Children.Add(Text(EveningCheckText.NumberHeaderSecondLine, 8, HorizontalAlignment.Center));
        Cell(sheet, firstRow, EveningCheckXlsxExporter.NumberColumn, number, firstRow, 1, rowSpan: 2);
        Cell(sheet, firstRow, EveningCheckXlsxExporter.RankColumn, Text(EveningCheckText.RankHeader, 10, HorizontalAlignment.Center), firstRow, 1, rowSpan: 2);
        Cell(sheet, firstRow, EveningCheckXlsxExporter.NameColumn, Text(EveningCheckText.NameHeader, 10, HorizontalAlignment.Center), firstRow, 1, rowSpan: 2);
        DateHeader(sheet, firstRow, days, firstRow, 1);

        var first = block * EveningCheckXlsxExporter.RowsPerBlock;
        for (var i = 0; i < EveningCheckXlsxExporter.RowsPerBlock; i++)
        {
            var row = firstRow + 2 + i;
            var index = first + i;
            var person = index < people.Count ? people[index] : null;
            Cell(sheet, row, EveningCheckXlsxExporter.NumberColumn, Text((index + 1).ToString(), 12, HorizontalAlignment.Center), firstRow, 1);
            Cell(sheet, row, EveningCheckXlsxExporter.RankColumn, Text(person?.Rank ?? string.Empty, 10, HorizontalAlignment.Center), firstRow, 1);
            var name = Text(person?.FullName ?? string.Empty, 10, HorizontalAlignment.Left);
            name.Margin = new Thickness(CellPadding + 6, 0, CellPadding, 0);
            Cell(sheet, row, EveningCheckXlsxExporter.NameColumn, name, firstRow, 1);
            EmptyDayCells(sheet, row, days.Count, firstRow, 1);
        }

        var checkerRow = firstRow + 2 + EveningCheckXlsxExporter.RowsPerBlock;
        Cell(sheet, checkerRow, 1, Text(EveningCheckText.CheckerSignature, 16, HorizontalAlignment.Center), firstRow, 1,
            columnSpan: EveningCheckXlsxExporter.NameColumn);
        EmptyDayCells(sheet, checkerRow, days.Count, firstRow, 1);

        Place(sheet, Text(signature, 14, HorizontalAlignment.Left), checkerRow + 2, 1, last);
        return sheet;
    }

    private static Grid SummarySheet(IReadOnlyList<DateOnly> days, string signature)
    {
        var heights = new List<double> { 18, 18.6, 28.2, EveningCheckXlsxExporter.DateRowHeight };
        heights.AddRange(Enumerable.Repeat(15.6, EveningCheckText.SummaryRows.Count));
        heights.AddRange(new[] { 64.8, 18.0, 17.4, 18.0 });
        var sheet = NewSheet(days.Count, heights);
        var last = EveningCheckXlsxExporter.FirstDayColumn + days.Count - 1;
        const int firstRow = 3;
        const int firstColumn = EveningCheckXlsxExporter.NameColumn;

        Place(sheet, Text(EveningCheckText.SummaryTitle, 14, HorizontalAlignment.Center), 1, 1, last);

        Cell(sheet, firstRow, firstColumn, new TextBlock(), firstRow, firstColumn, rowSpan: 2);
        DateHeader(sheet, firstRow, days, firstRow, firstColumn);

        var row = firstRow + 2;
        foreach (var label in EveningCheckText.SummaryRows)
        {
            Cell(sheet, row, firstColumn, Text(label, 10, HorizontalAlignment.Left), firstRow, firstColumn);
            EmptyDayCells(sheet, row, days.Count, firstRow, firstColumn);
            row++;
        }

        var checker = Text(EveningCheckText.CheckerSignature, 10, HorizontalAlignment.Left);
        checker.FontWeight = FontWeights.Bold;
        Cell(sheet, row, firstColumn, checker, firstRow, firstColumn);
        EmptyDayCells(sheet, row, days.Count, firstRow, firstColumn);

        Place(sheet, Text(signature, 14, HorizontalAlignment.Left), row + 3, firstColumn, last);
        return sheet;
    }

    /// <summary>«ДАТА» над усіма днями і дати «д/м» стовпчиком, повернуті на 90°, як в Excel.</summary>
    private static void DateHeader(Grid sheet, int row, IReadOnlyList<DateOnly> days, int firstRow, int firstColumn)
    {
        Cell(sheet, row, EveningCheckXlsxExporter.FirstDayColumn, Text(EveningCheckText.DateHeader, 14, HorizontalAlignment.Center),
            firstRow, firstColumn, columnSpan: days.Count);
        for (var d = 0; d < days.Count; d++)
        {
            var date = Text(EveningCheckXlsxExporter.DateText(days[d]), 8, HorizontalAlignment.Center);
            date.Margin = new Thickness(0);
            date.LayoutTransform = new RotateTransform(-90);
            Cell(sheet, row + 1, EveningCheckXlsxExporter.FirstDayColumn + d, date, firstRow, firstColumn);
        }
    }

    private static void EmptyDayCells(Grid sheet, int row, int dayCount, int firstRow, int firstColumn)
    {
        for (var d = 0; d < dayCount; d++)
            Cell(sheet, row, EveningCheckXlsxExporter.FirstDayColumn + d, null, firstRow, firstColumn);
    }

    private static Grid NewSheet(int dayCount, IReadOnlyList<double> rowHeightsPoints)
    {
        var sheet = new Grid { SnapsToDevicePixels = true, UseLayoutRounding = true, Background = Brushes.White };
        TextElement.SetFontFamily(sheet, new FontFamily(EveningCheckXlsxExporter.FontName));
        TextElement.SetForeground(sheet, Ink);

        var widths = new List<double> { EveningCheckXlsxExporter.NumberWidth, EveningCheckXlsxExporter.RankWidth, EveningCheckXlsxExporter.NameWidth };
        widths.AddRange(Enumerable.Repeat(EveningCheckXlsxExporter.DayWidth, dayCount));
        foreach (var width in widths)
            sheet.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(DutyRosterXlsxExporter.Pixels(width) * DipPerPixel) });
        foreach (var height in rowHeightsPoints)
            sheet.RowDefinitions.Add(new RowDefinition { Height = new GridLength(height * Point) });
        return sheet;
    }

    /// <summary>Клітинка з рамкою «Medium»: ліва й верхня лінії - лише в першій колонці й рядку таблиці, щоб лінії не подвоювались.</summary>
    private static void Cell(Grid sheet, int row, int column, UIElement? content, int firstRow, int firstColumn, int rowSpan = 1, int columnSpan = 1)
    {
        var border = new Border
        {
            BorderBrush = Ink,
            BorderThickness = new Thickness(column == firstColumn ? Line : 0, row == firstRow ? Line : 0, Line, Line),
            Child = content
        };
        Grid.SetRow(border, row - 1);
        Grid.SetColumn(border, column - 1);
        Grid.SetRowSpan(border, rowSpan);
        Grid.SetColumnSpan(border, columnSpan);
        sheet.Children.Add(border);
    }

    private static void Place(Grid sheet, UIElement element, int row, int firstColumn, int lastColumn)
    {
        Grid.SetRow(element, row - 1);
        Grid.SetColumn(element, firstColumn - 1);
        Grid.SetColumnSpan(element, lastColumn - firstColumn + 1);
        sheet.Children.Add(element);
    }

    private static TextBlock Text(string text, double points, HorizontalAlignment alignment) => new()
    {
        Text = text,
        FontSize = points * Point,
        HorizontalAlignment = alignment,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = alignment == HorizontalAlignment.Center ? TextAlignment.Center : TextAlignment.Left,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(CellPadding, 0, CellPadding, 0)
    };
}
