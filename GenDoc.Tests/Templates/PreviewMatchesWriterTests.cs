using System.IO;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using GenDoc.Models.TemplateBuilder;
using GenDoc.Services.Templates;

namespace GenDoc.Tests.Templates;

// Прев'ю конструктора рендериться з тієї самої моделі блоків, що й документ, -
// саме щоб не відставати від нього. Але «з тієї самої моделі» ще не означає «за
// тими самими правилами»: у трьох місцях правила розійшлися, і оператор бачив
// на екрані не те, що потім відкривав (аудит 2026-08-28).
public class PreviewMatchesWriterTests
{
    private static readonly IReadOnlyDictionary<string, string> NoValues =
        new Dictionary<string, string>();

    private static string SheetCellText(byte[] content, int sheetIndex, int row, int column)
    {
        using var stream = new MemoryStream(content);
        using var workbook = new XLWorkbook(stream);
        return workbook.Worksheet(sheetIndex + 1).Cell(row, column).GetString();
    }

    private static List<string> DocxParagraphs(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var word = WordprocessingDocument.Open(stream, false);
        return word.MainDocumentPart!.Document!.Body!
            .Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
            .Select(p => p.InnerText)
            .ToList();
    }

    private static string LineText(PreviewLine line)
        => string.Concat(line.Runs.Select(r => r.Text));

    private static string RowText(SheetPreviewRow row)
        => string.Concat(row.Cells.SelectMany(c => c).Select(r => r.Text));

    // ─── 1. «Дата і місто» з кількох рядків ──────────────────────────────────

    // TemplateSheetLayout резервує під цей блок LineCount(Text) рядків, і
    // xlsx-writer стільки й пише. Прев'ю (як і docx-writer) віддавало ОДИН
    // рядок із переносом усередині: на екрані блок займав один рядок, у книзі -
    // два, а в Word перенос узагалі зникав, бо Word ігнорує \n усередині <w:t>.
    private static readonly TemplateBuilderDocument TwoLineDateAndCity = new(
        new[]
        {
            new TemplateBlock(TemplateBlockKind.DateAndCity, "м. Київ\n01.09.2026")
        },
        Mode: TemplateBuilderMode.Excel);

    [Fact]
    public void MultilineDateAndCity_TakesAsManyPreviewRowsAsTheLayoutReserves()
    {
        var blocks = TwoLineDateAndCity.Blocks;
        var layout = TemplateSheetLayout.Compute(blocks);
        var preview = TemplateBlockPreview.BuildSheet(blocks, NoValues);

        Assert.Equal(2, layout.LastRow);
        Assert.Equal(2, preview.Rows.Count);
        Assert.Equal("м. Київ", RowText(preview.Rows[0]));
        Assert.Equal("01.09.2026", RowText(preview.Rows[1]));
    }

    [Fact]
    public void MultilineDateAndCity_LandsOnTheSameSheetRowsAsThePreviewShows()
    {
        var content = TemplateBlockXlsxWriter.Write(TwoLineDateAndCity).Content;
        var preview = TemplateBlockPreview.BuildSheet(TwoLineDateAndCity.Blocks, NoValues);

        foreach (var row in preview.Rows)
            Assert.Equal(RowText(row), SheetCellText(content, 0, row.Number, 1));
    }

    [Fact]
    public void MultilineDateAndCity_BecomesTwoParagraphsInWord()
    {
        var word = new TemplateBuilderDocument(TwoLineDateAndCity.Blocks);
        var paragraphs = DocxParagraphs(TemplateBlockDocxWriter.Write(word));

        Assert.Equal(new[] { "м. Київ", "01.09.2026" }, paragraphs);
    }

    // Захист від зворотного перекосу: «Заголовок» навмисно НЕ розбивається -
    // під нього розкладка резервує рівно один рядок.
    [Fact]
    public void MultilineTitle_StillTakesExactlyOneRow()
    {
        var blocks = new[] { new TemplateBlock(TemplateBlockKind.Title, "ВІДОМІСТЬ\nрезультатів") };

        Assert.Equal(1, TemplateSheetLayout.Compute(blocks).LastRow);
        Assert.Single(TemplateBlockPreview.BuildSheet(blocks, NoValues).Rows);
    }

    // ─── 2. Підпис без обраного підписанта ───────────────────────────────────

    // Writer склеює лише непорожні частини: «Начальник курсу: _____». Прев'ю
    // додавало пробіли безумовно й показувало «Начальник курсу:  _____ » -
    // подвійний пробіл після двокрапки й хвостовий.
    [Fact]
    public void SignatureWithoutASignatory_ReadsTheSameInThePreviewAndInTheDocument()
    {
        var document = new TemplateBuilderDocument(new[]
        {
            new TemplateBlock(TemplateBlockKind.Signatures,
                Signatures: new[] { new SignatureLine("Начальник курсу", null) })
        });

        var previewLine = Assert.IsType<PreviewLine>(
            Assert.Single(TemplateBlockPreview.Build(document, NoValues)));
        var paragraph = Assert.Single(DocxParagraphs(TemplateBlockDocxWriter.Write(document)));

        Assert.Equal(paragraph, LineText(previewLine));
    }

    [Fact]
    public void SignatureWithASignatory_ReadsTheSameInThePreviewAndInTheDocument()
    {
        var document = new TemplateBuilderDocument(new[]
        {
            new TemplateBlock(TemplateBlockKind.Signatures,
                Signatures: new[] { new SignatureLine("Начальник курсу", 7) })
        });
        var signatories = new Dictionary<int, SignatoryInfo>
        {
            [7] = new("полковник", "І. ПЕТРЕНКО")
        };

        var previewLine = Assert.IsType<PreviewLine>(
            Assert.Single(TemplateBlockPreview.Build(document, NoValues, signatories)));
        var paragraph = Assert.Single(DocxParagraphs(TemplateBlockDocxWriter.Write(document, signatories)));

        Assert.Equal(paragraph, LineText(previewLine));
    }

    // Порожня посада - писати «: _____» безглуздо, але правило одне на прев'ю
    // й writer, тож достатньо, щоб вони не розходились.
    [Fact]
    public void SignatureWithAnEmptyCaption_StillReadsTheSameInBoth()
    {
        var document = new TemplateBuilderDocument(new[]
        {
            new TemplateBlock(TemplateBlockKind.Signatures,
                Signatures: new[] { new SignatureLine(string.Empty, null) })
        });

        var previewLine = Assert.IsType<PreviewLine>(
            Assert.Single(TemplateBlockPreview.Build(document, NoValues)));
        var paragraph = Assert.Single(DocxParagraphs(TemplateBlockDocxWriter.Write(document)));

        Assert.Equal(paragraph, LineText(previewLine));
    }

    // Відомість і документ підписують однаково - це записано в коментарі
    // xlsx-writer'а, але тестом закріплено не було.
    [Fact]
    public void SignatureReadsTheSameInTheWorkbookAsInTheDocument()
    {
        var blocks = new[]
        {
            new TemplateBlock(TemplateBlockKind.Signatures,
                Signatures: new[] { new SignatureLine("Начальник курсу", null) })
        };
        var word = new TemplateBuilderDocument(blocks);
        var excel = new TemplateBuilderDocument(blocks, Mode: TemplateBuilderMode.Excel);

        var paragraph = Assert.Single(DocxParagraphs(TemplateBlockDocxWriter.Write(word)));
        var cell = SheetCellText(TemplateBlockXlsxWriter.Write(excel).Content, 0, 1, 1);

        Assert.Equal(paragraph, cell);
    }
}
