using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using GenDoc.Models.Enums;
using GenDoc.Services.Duty;

namespace GenDoc.Tests.Duty;

public class DutyRosterPrintLayoutTests
{
    private static readonly DateOnly From = new(2026, 10, 30);

    private static DutyRosterData Roster(int days)
    {
        var roster = new DutyRosterData { DateFrom = From, DateTo = From.AddDays(days - 1), Seed = 7 };
        foreach (var name in new[] { "КОПЧАК Б.М.", "ФУРТАК С.Б.", "ГОРДІЄНКО М.Г.", "ЛУПАЦА А.І.", "ШЕЛЕСТ І.В.", "ДНІСТРЯНСЬКИЙ В." })
            roster.Members.Add(new DutyRosterMemberData { DisplayName = name });
        roster.Members[5].ExemptWholePeriod = true;
        roster.Members[4].Duties[From] = DutyCellSource.Manual;
        roster.Members[4].Shifts[From] = 1;
        roster.Members[2].Duties[From] = DutyCellSource.Manual;
        roster.Members[0].Duties[From] = DutyCellSource.Manual;
        roster.Members[1].Canteen.Add(From);
        return roster;
    }

    /// <summary>WPF-елементи створюються лише в STA-потоці.</summary>
    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
    }

    private static List<TextBlock> Texts(DependencyObject root)
    {
        var found = new List<TextBlock>();
        void Walk(object node)
        {
            if (node is TextBlock text) found.Add(text);
            if (node is DependencyObject element)
                foreach (var child in LogicalTreeHelper.GetChildren(element)) Walk(child);
        }
        Walk(root);
        return found;
    }

    [Fact]
    public void OnePageA4LandscapeWithTheWholeSheet() => Sta(() =>
    {
        var document = DutyRosterPrintLayout.Document(Roster(4));

        Assert.Single(document.Pages);
        var page = document.Pages[0].Child;
        Assert.Equal(297 / 25.4 * 96, page.Width, 1);
        Assert.Equal(210 / 25.4 * 96, page.Height, 1);
        Assert.Equal(DutyRosterPrintLayout.PageSize, document.DocumentPaginator.PageSize);

        var texts = Texts(page).Select(t => t.Text).ToList();
        Assert.Contains("Графік чергувань особового складу", texts);
        Assert.Contains("на жовтень-листопад 2026 р.", texts);
        Assert.Contains("ДНІСТРЯНСЬКИЙ В.", texts);
        Assert.Contains("Перезмінка: 08:00 год.", texts);
        Assert.Contains("ІІІ Зміна", texts);
        Assert.Contains("04:00 - 08:00", texts);
        Assert.Equal(new[] { "ІІ", "ІІІ", "І" }, texts.Where(t => t is "І" or "ІІ" or "ІІІ"));
    });

    [Fact]
    public void CellsCarryTheSameMarksAndColoursAsTheExcelSheet() => Sta(() =>
    {
        var sheet = DutyRosterPrintLayout.Sheet(Roster(4));
        Brush? BackgroundOf(string text) => Texts(sheet).Where(t => t.Text == text).Select(t => (t.Parent as Border)?.Background).First();
        Color ColorOf(Brush? brush) => ((SolidColorBrush)brush!).Color;

        Assert.Equal((Color)ColorConverter.ConvertFromString(DutyRosterXlsxExporter.CanteenFillHex), ColorOf(BackgroundOf("к")));
        Assert.Equal((Color)ColorConverter.ConvertFromString(DutyRosterXlsxExporter.ExemptFillHex), ColorOf(BackgroundOf("ДНІСТРЯНСЬКИЙ В.")));
        Assert.Null(BackgroundOf("КОПЧАК Б.М."));

        var saturday = Texts(sheet).Where(t => t.Parent is Border && t.Text.Length == 0)
            .Select(t => (Border)t.Parent)
            .Count(b => b.Background is SolidColorBrush green
                        && green.Color == (Color)ColorConverter.ConvertFromString(DutyRosterXlsxExporter.ExemptFillHex));
        Assert.True(saturday >= 2 * 5, "суботу й неділю зафарбовано для кожного, хто в черзі");
    });

    [Theory]
    [InlineData(4)]
    [InlineData(14)]
    [InlineData(31)]
    public void SheetSpansThePrintableWidth(int days) => Sta(() =>
    {
        var sheet = DutyRosterPrintLayout.Sheet(Roster(days));
        sheet.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var width = sheet.ColumnDefinitions.Take(2 + days).Sum(c => c.Width.Value);
        Assert.InRange(width, 0.97 * DutyRosterPrintLayout.PrintableSize.Width, DutyRosterPrintLayout.PrintableSize.Width + 0.5);
    });

    [Fact]
    public void LongPeriodIsShrunkOntoThePage() => Sta(() =>
    {
        var document = DutyRosterPrintLayout.Document(Roster(62));
        var page = document.Pages[0].Child;
        var fit = (Viewbox)((Grid)page.Children[0]).Children[0];

        Assert.True(((Grid)fit.Child).DesiredSize.Width > DutyRosterPrintLayout.PrintableSize.Width);
        Assert.True(fit.RenderSize.Width <= DutyRosterPrintLayout.PrintableSize.Width + 0.5);
        Assert.True(fit.RenderSize.Height <= DutyRosterPrintLayout.PrintableSize.Height + 0.5);
    });

    /// <summary>Як на фото: 43 дні й 55 осіб - лист зменшено під висоту, а таблиця все одно на всю ширину аркуша.</summary>
    [Fact]
    public void BigGroupFillsBothTheWidthAndTheHeightOfThePage() => Sta(() =>
    {
        var roster = new DutyRosterData { DateFrom = new DateOnly(2026, 11, 3), DateTo = new DateOnly(2026, 12, 15), Seed = 7 };
        for (var p = 1; p <= 55; p++)
            roster.Members.Add(new DutyRosterMemberData { DisplayName = $"ПРІЗВИЩЕ{p} А.Б." });

        var document = DutyRosterPrintLayout.Document(roster);
        var fit = (Viewbox)((Grid)document.Pages[0].Child.Children[0]).Children[0];
        var printable = DutyRosterPrintLayout.PrintableSize;

        Assert.InRange(fit.RenderSize.Width, 0.97 * printable.Width, printable.Width + 0.5);
        Assert.InRange(fit.RenderSize.Height, 0.97 * printable.Height, printable.Height + 0.5);
    });

    [Theory]
    [InlineData("Microsoft Print to PDF", true)]
    [InlineData("Microsoft XPS Document Writer", true)]
    [InlineData("OneNote (Desktop)", true)]
    [InlineData("HP LaserJet M111w", false)]
    [InlineData("Canon LBP2900", false)]
    public void VirtualPrintersAreNotTakenForAConnectedPrinter(string name, bool isVirtual)
        => Assert.Equal(isVirtual, DutyRosterPrinter.IsVirtual(name));
}
