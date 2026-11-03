using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;

namespace GenDoc.Services.Duty;

/// <summary>
/// Аркуш графіка для друку й PDF - той самий лист, що в Excel: A4 альбомна, таблиця на все поле друку
/// і по центру сторінки. Ширини колонок, висоти рядків і місця блоків змін беруться з <see cref="DutyRosterXlsxExporter.Layout"/>,
/// тож друк і Excel не розходяться. Кольори - кольори документа, а не теми застосунку.
/// </summary>
public static class DutyRosterPrintLayout
{
    /// <summary>A4 альбомна в одиницях WPF (1/96 дюйма).</summary>
    public static readonly Size PageSize = new(297 / 25.4 * 96, 210 / 25.4 * 96);

    /// <summary>Поля - як у Excel: 0.25" з боків, 0.35" згори й знизу.</summary>
    public static readonly Thickness PageMargin = new(0.25 * 96, 0.35 * 96, 0.25 * 96, 0.35 * 96);

    /// <summary>Піксель розрахунку ширини колонки Excel на друці, в одиницях WPF.</summary>
    private const double DipPerPixel = DutyRosterXlsxExporter.PointsPerPixel * 96 / 72;

    private const double Point = 96.0 / 72;
    private const double CellPadding = 2;

    private static readonly Brush Line = Brushes.Black;
    private static readonly Brush ExemptFill = Frozen(DutyRosterXlsxExporter.ExemptFillHex);
    private static readonly Brush CanteenFill = Frozen(DutyRosterXlsxExporter.CanteenFillHex);

    public static Size PrintableSize => new(
        PageSize.Width - PageMargin.Left - PageMargin.Right,
        PageSize.Height - PageMargin.Top - PageMargin.Bottom);

    /// <summary>Один аркуш: лист по центру поля друку; якщо він більший (довгий період, багато людей) - зменшений, щоб уміститися.</summary>
    public static FixedDocument Document(DutyRosterData roster)
    {
        var fit = new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = Sheet(roster)
        };
        var area = new Grid { Width = PrintableSize.Width, Height = PrintableSize.Height };
        area.Children.Add(fit);

        var page = new FixedPage { Width = PageSize.Width, Height = PageSize.Height, Background = Brushes.White };
        FixedPage.SetLeft(area, PageMargin.Left);
        FixedPage.SetTop(area, PageMargin.Top);
        page.Children.Add(area);
        page.Measure(PageSize);
        page.Arrange(new Rect(PageSize));
        page.UpdateLayout();

        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = PageSize;
        document.Pages.Add(content);
        return document;
    }

    /// <summary>Лист у натуральну величину: шапка, таблиця, підвал - за тією ж сіткою колонок і рядків, що й в Excel.</summary>
    internal static Grid Sheet(DutyRosterData roster)
    {
        var layout = DutyRosterXlsxExporter.Layout(roster);
        var days = roster.Days.ToList();
        var lastColumn = layout.LastTableColumn;
        var sheet = new Grid { SnapsToDevicePixels = true, UseLayoutRounding = true };
        TextElement.SetFontFamily(sheet, new FontFamily(DutyRosterXlsxExporter.FontName));
        TextElement.SetFontSize(sheet, 11 * Point);
        TextElement.SetForeground(sheet, Brushes.Black);

        foreach (var width in layout.ColumnWidths)
            sheet.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(DutyRosterXlsxExporter.Pixels(width) * DipPerPixel) });
        for (var row = 1; row <= layout.LastRow; row++)
        {
            var height = layout.RowHeight(row) * Point;
            sheet.RowDefinitions.Add(new RowDefinition { Height = new GridLength(height) });
        }

        for (var line = 0; line < layout.TitleLines.Count; line++)
        {
            Place(sheet, Text(layout.TitleLines[line], 12, bold: true, HorizontalAlignment.Center),
                line + 1, DutyRosterXlsxExporter.NumberColumn, lastColumn);
        }

        var header = layout.HeaderRow;
        var number = Text("№ з/п", 7, bold: true, HorizontalAlignment.Center);
        number.TextWrapping = TextWrapping.Wrap;
        number.TextAlignment = TextAlignment.Center;
        Cell(sheet, layout, header, DutyRosterXlsxExporter.NumberColumn, number, null);
        Cell(sheet, layout, header, DutyRosterXlsxExporter.NameColumn, Text("П.І.Б.", 11, bold: true, HorizontalAlignment.Center), null);
        for (var d = 0; d < days.Count; d++)
        {
            Cell(sheet, layout, header, DutyRosterXlsxExporter.FirstDayColumn + d,
                Text(days[d].Day.ToString(), 9, bold: true, HorizontalAlignment.Center), null);
        }

        var shiftsByDay = days.ToDictionary(day => day, day => DutyRosterPlanner.ShiftsOfDay(roster, day));
        for (var m = 0; m < roster.Members.Count; m++)
        {
            var row = header + 1 + m;
            var member = roster.Members[m];
            var nameFill = member.ExemptWholePeriod ? ExemptFill : null;
            Cell(sheet, layout, row, DutyRosterXlsxExporter.NumberColumn, Text((m + 1).ToString(), 11, bold: false, HorizontalAlignment.Right), nameFill);
            Cell(sheet, layout, row, DutyRosterXlsxExporter.NameColumn, Text(member.DisplayName, 11, bold: false, HorizontalAlignment.Left), nameFill);

            for (var d = 0; d < days.Count; d++)
            {
                var (text, fill) = DutyRosterXlsxExporter.DayCell(roster, shiftsByDay[days[d]], m, days[d]);
                var background = fill switch
                {
                    DutyRosterXlsxExporter.SheetFill.Canteen => CanteenFill,
                    DutyRosterXlsxExporter.SheetFill.Exempt => ExemptFill,
                    _ => null
                };
                Cell(sheet, layout, row, DutyRosterXlsxExporter.FirstDayColumn + d, Text(text, 9, bold: true, HorizontalAlignment.Center), background);
            }
        }

        var end = layout.LastPrintColumn;
        Place(sheet, Text(DutyRosterXlsxExporter.ChangeoverText(roster), 11, bold: true, HorizontalAlignment.Left),
            layout.FooterRow, DutyRosterXlsxExporter.NameColumn, end);
        Place(sheet, Text(DutyRosterXlsxExporter.CleaningText(roster), 11, bold: true, HorizontalAlignment.Left),
            layout.FooterRow + 1, DutyRosterXlsxExporter.NameColumn, end);

        var shifts = roster.Footer.Shifts;
        for (var k = 0; k < shifts.Count; k++)
        {
            var column = layout.ShiftColumns[k];
            Place(sheet, Text(DutyRosterXlsxExporter.ShiftTitle(k), 11, bold: true, HorizontalAlignment.Left), layout.ShiftsRow, column, end);
            Place(sheet, Text(shifts[k].Day, 11, bold: true, HorizontalAlignment.Left), layout.ShiftsRow + 1, column, end);
            Place(sheet, Text(shifts[k].Night, 11, bold: true, HorizontalAlignment.Left), layout.ShiftsRow + 2, column, end);
        }

        return sheet;
    }

    /// <summary>Клітинка таблиці з тонкою рамкою, як «Thin» в Excel: ліва й верхня лінії - лише в першій колонці й рядку.</summary>
    private static void Cell(Grid sheet, DutyRosterXlsxExporter.SheetLayout layout, int row, int column, TextBlock content, Brush? fill)
    {
        var border = new Border
        {
            BorderBrush = Line,
            BorderThickness = new Thickness(column == DutyRosterXlsxExporter.NumberColumn ? 1 : 0, row == layout.HeaderRow ? 1 : 0, 1, 1),
            Background = fill,
            Child = content
        };
        Place(sheet, border, row, column, column);
    }

    /// <summary>Текст у клітинці; виходить за її межі вправо, як в Excel (колонка-«хвіст» до кінця рядка).</summary>
    private static void Place(Grid sheet, UIElement element, int row, int firstColumn, int lastColumn)
    {
        Grid.SetRow(element, row - 1);
        Grid.SetColumn(element, firstColumn - 1);
        Grid.SetColumnSpan(element, lastColumn - firstColumn + 1);
        sheet.Children.Add(element);
    }

    private static TextBlock Text(string text, double points, bool bold, HorizontalAlignment alignment) => new()
    {
        Text = text,
        FontSize = points * Point,
        FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
        HorizontalAlignment = alignment,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(CellPadding, 0, CellPadding, 0),
        TextTrimming = TextTrimming.None
    };

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
