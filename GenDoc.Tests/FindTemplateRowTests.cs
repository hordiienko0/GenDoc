using ClosedXML.Excel;
using GenDoc.Services;

namespace GenDoc.Tests;

public class FindTemplateRowTests
{
    [Fact]
    public void FindTemplateRow_HeaderHasTwoTagsInOneCell_PicksDataRowNotHeader()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Аркуш1");

        sheet.Cell("A1").Value = "Додаток 5";
        sheet.Cell("B3").Value =
            "ВІДОМІСТЬ\nрезультатів контрольного заняття на допуск до виконання\n" +
            " вправ стрільб  {{номери_вправ}}\nз особовим складом {{номер_вч}}";
        sheet.Cell("B4").Value = "№ п/п";
        sheet.Cell("B6").Value = "1";

        sheet.Cell("B7").Value = "{{№}}";
        sheet.Cell("C7").Value = "слухач";
        sheet.Cell("D7").Value = "{{звання}}";
        sheet.Cell("E7").Value = "{{піб}}";
        sheet.Cell("F7").Value = "{{оцінка_1}}";
        sheet.Cell("G7").Value = "{{оцінка_2}}";
        sheet.Cell("J7").Value = "{{оцінка_загальна}}";

        sheet.Cell("B8").Value = "Начальник курсів {{звання_начальника}} {{піб_начальника}}\n{{дата_аркуша}} р.";

        Assert.Equal(7, ExportTemplateService.FindTemplateRow(sheet.RangeUsed()!));
    }

    [Fact]
    public void FindTemplateRow_SignatureBlockBelowData_PicksDataRow()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Аркуш1");

        sheet.Cell("B5").Value = "Військова частина : {{номер_вч}}";
        sheet.Cell("B6").Value = "Підрозділ : {{опис_підрозділу}}";
        sheet.Cell("B8").Value = "№ з/п";

        sheet.Cell("B10").Value = "{{№}}";
        sheet.Cell("C10").Value = "слухач";
        sheet.Cell("D10").Value = "{{звання}}";
        sheet.Cell("E10").Value = "{{піб}}";
        sheet.Cell("F10").Value = "{{дата_аркуша}}";
        sheet.Cell("I10").Value = "{{курсовий_офіцер}}";

        sheet.Cell("B11").Value = "Начальник курсів {{звання_начальника}} {{піб_начальника}}";

        Assert.Equal(10, ExportTemplateService.FindTemplateRow(sheet.RangeUsed()!));
    }

    [Fact]
    public void FindTemplateRow_SingleRecipientTagAmongManualOnes_StillWins()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Аркуш1");

        sheet.Cell("A1").Value = "РОЗДАВАЛЬНО-ЗДАВАЛЬНА ВІДОМІСТЬ №{{номер_відомості}}";
        sheet.Cell("A8").Value = "1";

        sheet.Cell("A9").Value = "{{піб_ініціали}}";
        sheet.Cell("D9").Value = "{{калібр}}";
        sheet.Cell("E9").Value = "{{кількість_патронів}}";
        sheet.Cell("F9").Value = "{{дата_аркуша}}";

        Assert.Equal(9, ExportTemplateService.FindTemplateRow(sheet.RangeUsed()!));
    }

    [Fact]
    public void FindTemplateRow_NoRecipientTags_FallsBackToFirstMultiTagRow()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Аркуш1");

        sheet.Cell("A1").Value = "{{номер_вч}}";
        sheet.Cell("A3").Value = "{{калібр}}";
        sheet.Cell("B3").Value = "{{кількість_патронів}}";

        Assert.Equal(3, ExportTemplateService.FindTemplateRow(sheet.RangeUsed()!));
    }

    [Fact]
    public void FindTemplateRow_NoTagsAtAll_ReturnsNull()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Аркуш1");
        sheet.Cell("A1").Value = "Прізвище";
        sheet.Cell("B1").Value = "Звання";

        Assert.Null(ExportTemplateService.FindTemplateRow(sheet.RangeUsed()!));
    }

    [Fact]
    public void FindTemplateRow_RowWithMoreManualTags_LosesToRowWithRecipientTags()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Аркуш1");

        sheet.Cell("A4").Value = "{{калібр}}";
        sheet.Cell("B4").Value = "{{кількість_патронів}}";
        sheet.Cell("C4").Value = "{{номер_відомості}}";
        sheet.Cell("D4").Value = "{{опис_підрозділу}}";

        sheet.Cell("A6").Value = "{{звання}}";
        sheet.Cell("B6").Value = "{{піб}}";

        Assert.Equal(6, ExportTemplateService.FindTemplateRow(sheet.RangeUsed()!));
    }
}
