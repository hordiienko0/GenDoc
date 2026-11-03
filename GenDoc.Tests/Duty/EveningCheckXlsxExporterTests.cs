using ClosedXML.Excel;
using GenDoc.Models;
using GenDoc.Services.Duty;

namespace GenDoc.Tests.Duty;

public class EveningCheckXlsxExporterTests
{
    private static readonly DateOnly From = new(2026, 9, 15);
    private static readonly DateOnly To = new(2026, 11, 14);

    private static EveningCheckData Data(int course = 17, int seconded = 33)
    {
        var data = new EveningCheckData
        {
            DateFrom = From,
            DateTo = To,
            Signature = "Начальник курсів П та ПК:  підполковник                     Д. ВЕРЕМЕЄНКО"
        };
        var courseGroup = new EveningCheckGroup { Title = EveningCheckText.CourseTitle };
        for (var i = 1; i <= course; i++) courseGroup.People.Add(new EveningCheckPerson("майор", $"КУРСОВИЙ{i:00} Іван Петрович"));
        var secondedGroup = new EveningCheckGroup { Title = EveningCheckText.SecondedTitle };
        for (var i = 1; i <= seconded; i++) secondedGroup.People.Add(new EveningCheckPerson("лейтенант", $"СЛУХАЧ{i:00} Андрій Олегович"));
        data.Groups.Add(courseGroup);
        data.Groups.Add(secondedGroup);
        return data;
    }

    private static IXLWorksheet Sheet(EveningCheckData data) => EveningCheckXlsxExporter.Build(data).Worksheet(EveningCheckXlsxExporter.SheetName);

    private static IEnumerable<IXLCell> TitleCells(IXLWorksheet sheet, string title)
        => sheet.Column(1).CellsUsed(c => c.GetString() == title);

    [Fact]
    public void PeriodSplitsIntoPagesOf31Days()
    {
        var pages = EveningCheckXlsxExporter.Pages(From, To);

        Assert.Equal(2, pages.Count);
        Assert.Equal((From, new DateOnly(2026, 10, 15)), pages[0]);
        Assert.Equal((new DateOnly(2026, 10, 16), To), pages[1]);
        Assert.Single(EveningCheckXlsxExporter.Pages(From, From));
        Assert.Equal(new[] { 31, 30 }, EveningCheckXlsxExporter.PageLengths(From, To));
    }

    [Fact]
    public void ATailOfUpToFiveDaysJoinsTheLastPageInsteadOfANearlyEmptyOne()
    {
        Assert.Equal(new[] { 36 }, EveningCheckXlsxExporter.PageLengths(From, From.AddDays(35)));
        Assert.Equal(new[] { 31, 6 }, EveningCheckXlsxExporter.PageLengths(From, From.AddDays(36)));
        Assert.Equal(new[] { 31, 34 }, EveningCheckXlsxExporter.PageLengths(From, From.AddDays(64)));
        Assert.Equal(new[] { 31, 36 }, EveningCheckXlsxExporter.PageLengths(From, From.AddDays(66)));
        Assert.Equal(new[] { 31, 31, 6 }, EveningCheckXlsxExporter.PageLengths(From, From.AddDays(67)));

        var data = Data();
        data.DateTo = From.AddDays(64);
        var sheet = Sheet(data);
        var secondPage = TitleCells(sheet, EveningCheckText.CourseTitle).ElementAt(1).Address.RowNumber + 2;
        Assert.Equal("16/10", sheet.Cell(secondPage, 4).GetString());
        Assert.Equal("18/11", sheet.Cell(secondPage, 4 + 33).GetString());
        Assert.True(sheet.Cell(secondPage, 4 + 34).IsEmpty());
        Assert.Equal(4 + 33, sheet.PageSetup.PrintAreas.Single().LastColumn().ColumnNumber());
    }

    [Fact]
    public void BlocksHold25RowsAndAnEmptyGroupStillGetsOne()
    {
        Assert.Equal(1, EveningCheckXlsxExporter.BlockCount(0));
        Assert.Equal(1, EveningCheckXlsxExporter.BlockCount(25));
        Assert.Equal(2, EveningCheckXlsxExporter.BlockCount(26));
        Assert.Equal(2, EveningCheckXlsxExporter.BlockCount(33));
    }

    [Fact]
    public void EveryDatePageHasBlocksOfBothGroupsAndASummary()
    {
        var sheet = Sheet(Data());

        // 2 сторінки дат × (1 блок КППК + 2 блоки прикомандированих + облікові дані)
        Assert.Equal(2, TitleCells(sheet, EveningCheckText.CourseTitle).Count());
        Assert.Equal(4, TitleCells(sheet, EveningCheckText.SecondedTitle).Count());
        Assert.Equal(2, TitleCells(sheet, EveningCheckText.SummaryTitle).Count());
        Assert.Equal(8, EveningCheckXlsxExporter.PrintPages(Data()));
    }

    [Fact]
    public void FirstBlockHasPaperHeaderDatesAndNumberedRows()
    {
        var sheet = Sheet(Data());

        Assert.Equal(EveningCheckText.CourseTitle, sheet.Cell(1, 1).GetString());
        Assert.True(sheet.Cell(1, 1).IsMerged());
        Assert.Equal("№", sheet.Cell(2, 1).GetString());
        Assert.Equal("п/п", sheet.Cell(3, 1).GetString());
        Assert.Equal("в/зв", sheet.Cell(2, 2).GetString());
        Assert.Equal("Прізвище та ім’я", sheet.Cell(2, 3).GetString());
        Assert.Equal("ДАТА", sheet.Cell(2, 4).GetString());
        Assert.True(sheet.Cell(2, 4).IsMerged());

        var dates = Enumerable.Range(4, 31).Select(c => sheet.Cell(3, c).GetString()).ToList();
        Assert.Equal("15/9", dates[0]);
        Assert.Equal("15/10", dates[^1]);
        Assert.Equal(XLDataType.Text, sheet.Cell(3, 4).DataType);
        Assert.Equal(EveningCheckXlsxExporter.DateRowHeight, sheet.Row(3).Height);
        Assert.Equal(90, sheet.Cell(3, 4).Style.Alignment.TextRotation);
        Assert.True(sheet.Cell(3, 35).IsEmpty());

        Assert.Equal(1, (int)sheet.Cell(4, 1).GetDouble());
        Assert.Equal("майор", sheet.Cell(4, 2).GetString());
        Assert.Equal("КУРСОВИЙ01 Іван Петрович", sheet.Cell(4, 3).GetString());
        Assert.Equal("КУРСОВИЙ17 Іван Петрович", sheet.Cell(20, 3).GetString());

        // 25 рядків: після 17 людей - порожні пронумеровані
        Assert.Equal(25, (int)sheet.Cell(28, 1).GetDouble());
        Assert.True(sheet.Cell(28, 3).IsEmpty());
        Assert.Equal(EveningCheckText.CheckerSignature, sheet.Cell(29, 1).GetString());
        Assert.Equal("Начальник курсів П та ПК:  підполковник                     Д. ВЕРЕМЕЄНКО", sheet.Cell(31, 1).GetString());
    }

    [Fact]
    public void NumberingContinuesAcrossBlocksOfTheSameGroup()
    {
        var sheet = Sheet(Data());
        var secondedTitles = TitleCells(sheet, EveningCheckText.SecondedTitle).ToList();

        var firstBlockRow = secondedTitles[0].Address.RowNumber + 3;
        Assert.Equal(1, (int)sheet.Cell(firstBlockRow, 1).GetDouble());
        Assert.Equal("СЛУХАЧ01 Андрій Олегович", sheet.Cell(firstBlockRow, 3).GetString());
        Assert.Equal("СЛУХАЧ25 Андрій Олегович", sheet.Cell(firstBlockRow + 24, 3).GetString());

        var secondBlockRow = secondedTitles[1].Address.RowNumber + 3;
        Assert.Equal(26, (int)sheet.Cell(secondBlockRow, 1).GetDouble());
        Assert.Equal("СЛУХАЧ26 Андрій Олегович", sheet.Cell(secondBlockRow, 3).GetString());
        Assert.Equal("СЛУХАЧ33 Андрій Олегович", sheet.Cell(secondBlockRow + 7, 3).GetString());
        Assert.Equal(50, (int)sheet.Cell(secondBlockRow + 24, 1).GetDouble());
        Assert.True(sheet.Cell(secondBlockRow + 8, 3).IsEmpty());
    }

    [Fact]
    public void SecondDatePageStartsWhereTheFirstEndedAndIsShorter()
    {
        var sheet = Sheet(Data());
        var secondPageTitle = TitleCells(sheet, EveningCheckText.CourseTitle).ElementAt(1);
        var headerRow = secondPageTitle.Address.RowNumber + 2;

        Assert.Equal("16/10", sheet.Cell(headerRow, 4).GetString());
        Assert.Equal("14/11", sheet.Cell(headerRow, 33).GetString());
        Assert.True(sheet.Cell(headerRow, 34).IsEmpty());
    }

    [Fact]
    public void SummaryBlockListsTheAccountingRowsWithTheSameDates()
    {
        var sheet = Sheet(Data());
        var summary = TitleCells(sheet, EveningCheckText.SummaryTitle).First();
        var row = summary.Address.RowNumber;

        Assert.Equal("ДАТА", sheet.Cell(row + 2, 4).GetString());
        Assert.Equal("15/9", sheet.Cell(row + 3, 4).GetString());

        var labels = Enumerable.Range(row + 4, 10).Select(r => sheet.Cell(r, 3).GetString()).ToList();
        Assert.Equal(EveningCheckText.SummaryRows, labels);
        Assert.Equal(EveningCheckText.CheckerSignature, sheet.Cell(row + 14, 3).GetString());
        Assert.True(sheet.Cell(row + 14, 3).Style.Font.Bold);
        Assert.Contains("Начальник курсів П та ПК", sheet.Cell(row + 17, 3).GetString());
    }

    [Fact]
    public void EveryBlockIsItsOwnPrintedPage()
    {
        var sheet = Sheet(Data());

        var breaks = sheet.PageSetup.RowBreaks;
        Assert.Equal(EveningCheckXlsxExporter.PrintPages(Data()) - 1, breaks.Count);
        Assert.Equal(XLPageOrientation.Landscape, sheet.PageSetup.PageOrientation);
        Assert.Equal(XLPaperSize.A4Paper, sheet.PageSetup.PaperSize);
        Assert.Equal(1, sheet.PageSetup.PagesWide);
        Assert.Equal("Times New Roman", sheet.Style.Font.FontName);
    }

    [Fact]
    public void SplitByFitnessPrintsLimitedAndFitAsSeparateTablesEachNumberedFromOne()
    {
        var data = Data(course: 3, seconded: 0);
        var seconded = data.Groups[1];
        seconded.SplitByFitness = true;
        seconded.People.Add(new EveningCheckPerson("капітан", "ПРИДАТНИЙ01 Іван Іванович"));
        seconded.People.Add(new EveningCheckPerson("капітан", "ОБМЕЖЕНИЙ01 Іван Іванович", IsLimited: true));
        seconded.People.Add(new EveningCheckPerson("лейтенант", "ПРИДАТНИЙ02 Іван Іванович"));
        seconded.People.Add(new EveningCheckPerson("лейтенант", "ОБМЕЖЕНИЙ02 Іван Іванович", IsLimited: true));
        data.DateTo = From.AddDays(10);

        const string limitedTitle = "ПРИКОМАНДИРОВАНІ (ОБМЕЖЕНО ПРИДАТНІ)";
        const string fitTitle = "ПРИКОМАНДИРОВАНІ (ПРИДАТНІ)";
        var tables = EveningCheckXlsxExporter.Tables(seconded);
        Assert.Equal(new[] { limitedTitle, fitTitle }, tables.Select(t => t.Title));
        Assert.Equal(new[] { "ОБМЕЖЕНИЙ01", "ОБМЕЖЕНИЙ02" }, tables[0].People.Select(p => p.FullName.Split(' ')[0]));
        Assert.Equal(new[] { "ПРИДАТНИЙ01", "ПРИДАТНИЙ02" }, tables[1].People.Select(p => p.FullName.Split(' ')[0]));
        Assert.Equal(2, EveningCheckXlsxExporter.BlocksOf(seconded));
        Assert.Equal(4, EveningCheckXlsxExporter.PrintPages(data));

        // Кожна таблиця - свій блок на своїй сторінці, свій заголовок і номери з 1; жирної лінії немає
        var sheet = Sheet(data);
        Assert.Empty(TitleCells(sheet, EveningCheckText.SecondedTitle));
        var limited = Assert.Single(TitleCells(sheet, limitedTitle)).Address.RowNumber + 3;
        Assert.Equal(1, (int)sheet.Cell(limited, 1).GetDouble());
        Assert.Equal("ОБМЕЖЕНИЙ01 Іван Іванович", sheet.Cell(limited, 3).GetString());
        Assert.Equal("ОБМЕЖЕНИЙ02 Іван Іванович", sheet.Cell(limited + 1, 3).GetString());
        Assert.Equal(3, (int)sheet.Cell(limited + 2, 1).GetDouble());
        Assert.True(sheet.Cell(limited + 2, 3).IsEmpty());
        Assert.Equal(XLBorderStyleValues.Medium, sheet.Cell(limited + 2, 3).Style.Border.TopBorder);

        var fit = Assert.Single(TitleCells(sheet, fitTitle)).Address.RowNumber + 3;
        Assert.Equal(1, (int)sheet.Cell(fit, 1).GetDouble());
        Assert.Equal("ПРИДАТНИЙ01 Іван Іванович", sheet.Cell(fit, 3).GetString());
        Assert.Equal("ПРИДАТНИЙ02 Іван Іванович", sheet.Cell(fit + 1, 3).GetString());
        Assert.Equal(25, (int)sheet.Cell(fit + 24, 1).GetDouble());
        Assert.Equal(EveningCheckText.LimitedPart, EveningCheckText.FitnessTitle("", limited: true));

        // Без поділу - одна таблиця як є
        seconded.SplitByFitness = false;
        var single = Assert.Single(EveningCheckXlsxExporter.Tables(seconded));
        Assert.Equal(EveningCheckText.SecondedTitle, single.Title);
        Assert.Equal(4, single.People.Count);

        // З поділом, але всі придатні - теж одна таблиця з тим самим заголовком
        seconded.SplitByFitness = true;
        seconded.People.RemoveAll(p => p.IsLimited);
        Assert.Equal(EveningCheckText.SecondedTitle, Assert.Single(EveningCheckXlsxExporter.Tables(seconded)).Title);
    }

    [Fact]
    public void TopMarginLeavesRoomForBindingAndEveryBlockStaysOnItsPage()
    {
        var page = Sheet(Data()).PageSetup;
        Assert.Equal(30 / 25.4, page.Margins.Top, 6);
        Assert.Equal(0.4, page.Margins.Left);
        Assert.Equal(0.4, page.Margins.Bottom);
        Assert.Equal(1, page.PagesWide);

        // Тиждень - вузький лист без зменшення по ширині, і блок не влазив би по висоті: масштаб задано явно
        var data = Data();
        data.DateTo = From.AddDays(6);
        var shortPage = Sheet(data).PageSetup;
        Assert.Equal(0, shortPage.PagesWide);
        Assert.InRange(shortPage.Scale, 50, 99);
    }

    [Fact]
    public void NumberHeaderHasNoLineBetweenItsTwoCells()
    {
        var sheet = Sheet(Data());

        Assert.Equal(XLBorderStyleValues.None, sheet.Cell(2, 1).Style.Border.BottomBorder);
        Assert.Equal(XLBorderStyleValues.None, sheet.Cell(3, 1).Style.Border.TopBorder);
        Assert.Equal(XLBorderStyleValues.Medium, sheet.Cell(3, 1).Style.Border.BottomBorder);
        Assert.Equal(XLBorderStyleValues.Medium, sheet.Cell(4, 4).Style.Border.LeftBorder);
    }

    [Fact]
    public void SavedFileReopensWithTheSameSheet()
    {
        var path = Path.Combine(Path.GetTempPath(), $"evening-check-{Guid.NewGuid():N}.xlsx");
        try
        {
            EveningCheckXlsxExporter.Save(Data(), path);

            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheet(EveningCheckXlsxExporter.SheetName);
            Assert.Equal(EveningCheckText.CourseTitle, sheet.Cell(1, 1).GetString());
            Assert.Equal("Список!$A$1:$AH$236", sheet.PageSetup.PrintAreas.Single().ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FullNameIsCapsSurnameThenGivenNames()
    {
        Assert.Equal("ЛОПУШАНСЬКИЙ Олександр Степанович", EveningCheckText.FullName("Лопушанський", "Олександр", "Степанович"));
        Assert.Equal("УСС Олег", EveningCheckText.FullName(" Усс ", "Олег", null));
    }

    [Theory]
    [InlineData("Дмитро ВЕРЕМЕЄНКО", "Д. ВЕРЕМЕЄНКО")]
    [InlineData("Веремеєнко Дмитро Іванович", "Д. ВЕРЕМЕЄНКО")]
    [InlineData("ВЕРЕМЕЄНКО", "ВЕРЕМЕЄНКО")]
    [InlineData("", "")]
    public void ShortNameIsInitialAndCapsSurname(string full, string expected)
        => Assert.Equal(expected, EveningCheckText.ShortName(full));

    [Fact]
    public void SignatureComesFromOrganizationSettings()
    {
        var org = new OrganizationSettings { CommanderRank = "підполковник", CommanderFullName = "Дмитро ВЕРЕМЕЄНКО" };

        Assert.Equal("Начальник курсів П та ПК:  підполковник                     Д. ВЕРЕМЕЄНКО", EveningCheckText.Signature(org));
        Assert.Equal("Начальник курсів П та ПК:", EveningCheckText.Signature(null));
        Assert.Equal("Начальник курсів П та ПК:  Д. ВЕРЕМЕЄНКО",
            EveningCheckText.Signature(new OrganizationSettings { CommanderFullName = "Дмитро ВЕРЕМЕЄНКО" }));
    }

    [Fact]
    public void FileNameCarriesThePeriod()
        => Assert.Equal("Список вечірньої перевірки 15.09-14.11.2026.xlsx", EveningCheckText.FileName(From, To));
}
