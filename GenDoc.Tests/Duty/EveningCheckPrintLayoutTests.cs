using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GenDoc.Services.Duty;

namespace GenDoc.Tests.Duty;

public class EveningCheckPrintLayoutTests
{
    private static EveningCheckData Data()
    {
        var data = new EveningCheckData
        {
            DateFrom = new DateOnly(2026, 9, 15),
            DateTo = new DateOnly(2026, 11, 14),
            Signature = "Начальник курсів П та ПК:  підполковник                     Д. ВЕРЕМЕЄНКО"
        };
        var course = new EveningCheckGroup { Title = EveningCheckText.CourseTitle };
        for (var i = 1; i <= 17; i++) course.People.Add(new EveningCheckPerson("майор", $"КУРСОВИЙ{i:00} Іван Петрович"));
        var seconded = new EveningCheckGroup { Title = EveningCheckText.SecondedTitle };
        for (var i = 1; i <= 33; i++) seconded.People.Add(new EveningCheckPerson("лейтенант", $"СЛУХАЧ{i:00} Андрій Олегович"));
        data.Groups.Add(course);
        data.Groups.Add(seconded);
        return data;
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

    private static IEnumerable<TextBlock> Texts(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock text) yield return text;
            foreach (var inner in Texts(child)) yield return inner;
        }
    }

    [Fact]
    public void OneSheetPerBlockAndSummaryInExcelOrder() => Sta(() =>
    {
        var data = Data();
        var sheets = EveningCheckPrintLayout.Sheets(data);

        Assert.Equal(EveningCheckXlsxExporter.PrintPages(data), sheets.Count);
        var titles = sheets.Select(s => Texts(s).First().Text).ToList();
        Assert.Equal(new[]
        {
            EveningCheckText.CourseTitle, EveningCheckText.SecondedTitle, EveningCheckText.SecondedTitle, EveningCheckText.SummaryTitle,
            EveningCheckText.CourseTitle, EveningCheckText.SecondedTitle, EveningCheckText.SecondedTitle, EveningCheckText.SummaryTitle
        }, titles);
    });

    [Fact]
    public void BlockSheetHasTheSameColumnsRowsAndTextsAsExcel() => Sta(() =>
    {
        var sheet = (Grid)EveningCheckPrintLayout.Sheets(Data())[0];
        sheet.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        sheet.Arrange(new Rect(sheet.DesiredSize));

        Assert.Equal(3 + 31, sheet.ColumnDefinitions.Count);
        Assert.Equal(3 + EveningCheckXlsxExporter.RowsPerBlock + 3, sheet.RowDefinitions.Count);

        var texts = Texts(sheet).Select(t => t.Text).ToList();
        Assert.Contains("ДАТА", texts);
        Assert.Contains("15/9", texts);
        Assert.Contains("15/10", texts);
        Assert.DoesNotContain("16/10", texts);
        Assert.Contains("КУРСОВИЙ17 Іван Петрович", texts);
        Assert.Contains("25", texts);
        Assert.Contains(EveningCheckText.CheckerSignature, texts);
        Assert.Contains("Начальник курсів П та ПК:  підполковник                     Д. ВЕРЕМЕЄНКО", texts);

        var date = Texts(sheet).First(t => t.Text == "15/9");
        Assert.IsType<RotateTransform>(date.LayoutTransform);
        Assert.True(sheet.DesiredSize.Width > EveningCheckPrintLayout.PrintableSize.Width, "лист ширший за поле друку і зменшується у Viewbox");
    });

    [Fact]
    public void SummarySheetListsAccountingRowsAndSecondPageIsShorter() => Sta(() =>
    {
        var sheets = EveningCheckPrintLayout.Sheets(Data());
        var summary = (Grid)sheets[3];
        var texts = Texts(summary).Select(t => t.Text).ToList();
        foreach (var label in EveningCheckText.SummaryRows) Assert.Contains(label, texts);
        Assert.Contains("15/9", texts);

        var secondPage = (Grid)sheets[4];
        Assert.Equal(3 + 30, secondPage.ColumnDefinitions.Count);
        Assert.Contains("16/10", Texts(secondPage).Select(t => t.Text));
    });

    [Fact]
    public void BindingMarginIsAtTheTopAndSplitGroupPrintsTwoTables() => Sta(() =>
    {
        Assert.Equal(30 / 25.4 * 96, EveningCheckPrintLayout.PageMargin.Top, 6);
        Assert.Equal(0.4 * 96, EveningCheckPrintLayout.PageMargin.Left, 6);

        var data = Data();
        var seconded = data.Groups[1];
        seconded.SplitByFitness = true;
        seconded.People[0] = seconded.People[0] with { IsLimited = true };
        var sheets = EveningCheckPrintLayout.Sheets(data);

        Assert.Equal(EveningCheckXlsxExporter.PrintPages(data), sheets.Count);
        Assert.Equal(new[]
        {
            EveningCheckText.CourseTitle,
            EveningCheckText.FitnessTitle(EveningCheckText.SecondedTitle, limited: true),
            EveningCheckText.FitnessTitle(EveningCheckText.SecondedTitle, limited: false),
            EveningCheckText.FitnessTitle(EveningCheckText.SecondedTitle, limited: false),
            EveningCheckText.SummaryTitle
        }, sheets.Take(5).Select(s => Texts(s).First().Text));
        Assert.Contains("СЛУХАЧ02 Андрій Олегович", Texts(sheets[2]).Select(t => t.Text));
    });

    [Fact]
    public void DocumentHasAPagePerSheetInA4Landscape() => Sta(() =>
    {
        var document = EveningCheckPrintLayout.Document(Data());

        Assert.Equal(8, document.Pages.Count);
        Assert.Equal(EveningCheckPrintLayout.PageSize, document.DocumentPaginator.PageSize);
        Assert.True(EveningCheckPrintLayout.PageSize.Width > EveningCheckPrintLayout.PageSize.Height);
    });
}
