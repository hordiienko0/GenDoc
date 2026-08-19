using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GenDoc.Models.TemplateBuilder;
using GenDoc.Services.Templates;

namespace GenDoc.Tests.Templates;

/// <summary>Таблиця ширша за типові три колонки. Обмеження на кількість колонок
/// у моделі немає й не мусить з'явитися - вузьким місцем була тільки розкладка
/// на екрані (UniformGrid ділив ширину панелі між усіма колонками).</summary>
public class TemplateWideTableTests
{
    private const int Wide = 8;

    private static TableSpec WideTable() => new(
        Enumerable.Range(1, Wide)
            .Select(i => new TableColumn($"Колонка {i}", i == 1 ? "{{піб}}" : $"{{{{поле{i}}}}}"))
            .ToList(),
        RepeatPerPerson: true);

    private static TemplateBuilderDocument Document(TemplateBuilderMode mode) => new(
        new[] { new TemplateBlock(TemplateBlockKind.Table, Table: WideTable()) },
        Mode: mode);

    [Fact]
    public void Sheet_layout_counts_every_column()
    {
        var layout = TemplateSheetLayout.Compute(Document(TemplateBuilderMode.Excel).Blocks);

        Assert.Equal(Wide, layout.ColumnCount);
        // Восьма колонка - це літера H; підпис розкладки має довести саме до неї.
        Assert.Equal("H", TemplateSheetLayout.ColumnLetter(Wide));
    }

    [Fact]
    public void Xlsx_writes_every_column_of_a_wide_table()
    {
        var result = TemplateBlockXlsxWriter.Write(Document(TemplateBuilderMode.Excel));

        using var stream = new MemoryStream();
        stream.Write(result.Content, 0, result.Content.Length);
        stream.Position = 0;
        var sheet = new XLWorkbook(stream).Worksheet(1);

        Assert.Equal("Колонка 1", sheet.Cell(1, 1).GetString());
        Assert.Equal($"Колонка {Wide}", sheet.Cell(1, Wide).GetString());
        Assert.Equal($"{{{{поле{Wide}}}}}", sheet.Cell(2, Wide).GetString());
    }

    [Fact]
    public void Docx_writes_every_column_of_a_wide_table()
    {
        var docx = TemplateBlockDocxWriter.Write(Document(TemplateBuilderMode.Word));

        using var stream = new MemoryStream(docx);
        using var word = WordprocessingDocument.Open(stream, isEditable: false);

        var table = word.MainDocumentPart!.Document!.Body!.Elements<Table>().Single();
        var header = table.Elements<TableRow>().First();

        Assert.Equal(Wide, header.Elements<TableCell>().Count());
        Assert.Equal($"Колонка {Wide}", header.Elements<TableCell>().Last().InnerText);
    }

    // Маркерний рядок {{#особи}} - це ЦІЛИЙ рядок таблиці, тож він мусить мати
    // стільки ж комірок, скільки й решта, інакше рушій не впізнає блок.
    [Fact]
    public void Marker_row_spans_the_whole_width_of_a_wide_table()
    {
        var docx = TemplateBlockDocxWriter.Write(Document(TemplateBuilderMode.Word));

        using var stream = new MemoryStream(docx);
        using var word = WordprocessingDocument.Open(stream, isEditable: false);

        var rows = word.MainDocumentPart!.Document!.Body!
            .Elements<Table>().Single()
            .Elements<TableRow>()
            .ToList();

        var marker = rows[1];
        Assert.Equal("{{#особи}}", marker.InnerText);
        Assert.Equal(Wide, marker.Elements<TableCell>().Count());
    }

    [Fact]
    public void Preview_keeps_every_column_of_a_wide_table()
    {
        var table = Assert.IsType<PreviewTable>(
            TemplateBlockPreview.Build(
                Document(TemplateBuilderMode.Word), new Dictionary<string, string>()).Single());

        Assert.Equal(Wide, table.Headers.Count);
        Assert.Equal(Wide, table.Cells.Count);
    }
}
