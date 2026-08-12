using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GenDoc.Models.TemplateBuilder;
using GenDoc.Services.Templates;

namespace GenDoc.Tests.Templates;

/// <summary>Оформлення блока в обох writer'ах. Головне тут — не те, що заданий
/// шрифт доїжджає до файлу, а те, що НЕзаданий не доїжджає: у базі лежать
/// шаблони, зібрані до появи форматування, і вони мусять збиратися так само.</summary>
public class TemplateBlockStyleWriterTests
{
    private static RunProperties? FirstRunProperties(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        using var word = WordprocessingDocument.Open(stream, isEditable: false);

        return word.MainDocumentPart!.Document!.Body!
            .Elements<Paragraph>()
            .First()
            .Elements<Run>()
            .First()
            .RunProperties;
    }

    private static Justification? FirstJustification(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        using var word = WordprocessingDocument.Open(stream, isEditable: false);

        return word.MainDocumentPart!.Document!.Body!
            .Elements<Paragraph>()
            .First()
            .ParagraphProperties?
            .Justification;
    }

    private static TemplateBuilderDocument One(TemplateBlock block)
        => new(new[] { block });

    [Fact]
    public void Docx_writes_font_size_colour_and_slant()
    {
        var docx = TemplateBlockDocxWriter.Write(One(new TemplateBlock(
            TemplateBlockKind.Paragraph, "Текст",
            Style: new BlockStyle(
                FontFamily: "Arial", FontSize: 14, Bold: true, Italic: true, Color: "C00000"))));

        var properties = FirstRunProperties(docx);

        Assert.Equal("Arial", properties!.RunFonts!.Ascii!.Value);
        // Word міряє кегль у пів-пунктах: 14 пт → "28".
        Assert.Equal("28", properties.FontSize!.Val!.Value);
        Assert.NotNull(properties.Bold);
        Assert.NotNull(properties.Italic);
        Assert.Equal("C00000", properties.Color!.Val!.Value);
    }

    // Регресія на сумісність: шаблон, збережений до появи форматування, не має
    // раптом отримати явні шрифти — інакше всі наявні документи перемалюються.
    [Fact]
    public void Docx_writes_nothing_extra_when_no_style_is_set()
    {
        var docx = TemplateBlockDocxWriter.Write(One(
            new TemplateBlock(TemplateBlockKind.Paragraph, "Текст")));

        var properties = FirstRunProperties(docx);

        Assert.Null(properties!.RunFonts);
        Assert.Null(properties.FontSize);
        Assert.Null(properties.Color);
        Assert.Null(properties.Bold);
        Assert.Null(properties.Italic);
    }

    [Theory]
    [InlineData(BlockAlignment.Left, "left")]
    [InlineData(BlockAlignment.Center, "center")]
    [InlineData(BlockAlignment.Right, "right")]
    [InlineData(BlockAlignment.Justify, "both")]
    public void Docx_maps_alignment_to_justification(BlockAlignment alignment, string expected)
    {
        var docx = TemplateBlockDocxWriter.Write(One(new TemplateBlock(
            TemplateBlockKind.Paragraph, "Текст", Style: new BlockStyle(Alignment: alignment))));

        // InnerText, а не Val.Value.ToString(): EnumValue<T> віддає з ToString()
        // назву типу, а не серіалізоване значення.
        Assert.Equal(expected, FirstJustification(docx)!.Val!.InnerText);
    }

    // Заголовок жирний за замовчуванням — саме тому важливо, що явний false
    // доїжджає до файлу, а не тоне в типовому значенні.
    [Fact]
    public void Docx_lets_the_operator_unbold_a_title()
    {
        var docx = TemplateBlockDocxWriter.Write(One(new TemplateBlock(
            TemplateBlockKind.Title, "АКТ", Style: new BlockStyle(Bold: false))));

        Assert.Null(FirstRunProperties(docx)!.Bold);
    }

    private static IXLWorksheet SheetOf(byte[] xlsx, MemoryStream stream)
    {
        stream.Write(xlsx, 0, xlsx.Length);
        stream.Position = 0;
        return new XLWorkbook(stream).Worksheet(1);
    }

    [Fact]
    public void Xlsx_applies_font_colour_and_alignment()
    {
        var result = TemplateBlockXlsxWriter.Write(One(new TemplateBlock(
            TemplateBlockKind.Title, "ВІДОМІСТЬ",
            Style: new BlockStyle(FontFamily: "Verdana", FontSize: 16, Italic: true, Color: "1F4E79"))));

        using var stream = new MemoryStream();
        var cell = SheetOf(result.Content, stream).Cell(1, 1);

        Assert.Equal("Verdana", cell.Style.Font.FontName);
        Assert.Equal(16, cell.Style.Font.FontSize);
        Assert.True(cell.Style.Font.Italic);
        Assert.Equal(XLColor.FromHtml("#1F4E79"), cell.Style.Font.FontColor);
    }

    // У книзі «нічого не задано» не може означати «нічого не писати»: Excel тоді
    // виведе Calibri. Типове має лишитися тим самим, що й було, — Times New Roman 11.
    [Fact]
    public void Xlsx_keeps_its_own_default_font_when_none_is_set()
    {
        var result = TemplateBlockXlsxWriter.Write(One(
            new TemplateBlock(TemplateBlockKind.Title, "ВІДОМІСТЬ")));

        using var stream = new MemoryStream();
        var cell = SheetOf(result.Content, stream).Cell(1, 1);

        Assert.Equal("Times New Roman", cell.Style.Font.FontName);
        Assert.Equal(11, cell.Style.Font.FontSize);
    }

    [Fact]
    public void Xlsx_table_header_stays_bold_and_centred_when_the_block_is_not()
    {
        var table = new TableSpec(new[] { new TableColumn("ПІБ", "{{піб}}") }, RepeatPerPerson: true);

        var result = TemplateBlockXlsxWriter.Write(One(new TemplateBlock(
            TemplateBlockKind.Table, Table: table,
            Style: new BlockStyle(Bold: false, FontFamily: "Arial"))));

        using var stream = new MemoryStream();
        var sheet = SheetOf(result.Content, stream);

        var header = sheet.Cell(1, 1);
        Assert.True(header.Style.Font.Bold);
        Assert.Equal(XLAlignmentHorizontalValues.Center, header.Style.Alignment.Horizontal);
        Assert.Equal("Arial", header.Style.Font.FontName);

        // Рядок-шаблон — навпаки, слухається блока.
        var template = sheet.Cell(2, 1);
        Assert.False(template.Style.Font.Bold);
        Assert.Equal("Arial", template.Style.Font.FontName);
    }

    // Смуга в книзі — об'єднані клітинки, «по ширині» в них виглядає зламано,
    // тож Justify лягає ліворуч. Так writer поводився й до появи форматування.
    [Fact]
    public void Xlsx_maps_justify_to_left()
    {
        var result = TemplateBlockXlsxWriter.Write(One(new TemplateBlock(
            TemplateBlockKind.Paragraph, "Текст", Style: new BlockStyle(Alignment: BlockAlignment.Justify))));

        using var stream = new MemoryStream();
        var cell = SheetOf(result.Content, stream).Cell(1, 1);

        Assert.Equal(XLAlignmentHorizontalValues.Left, cell.Style.Alignment.Horizontal);
    }

    // Прев'ю — дзеркало writer'ів, і дзеркалить воно саме розв'язаний стиль.
    [Fact]
    public void Preview_line_carries_the_same_resolved_style()
    {
        var style = new BlockStyle(FontFamily: "Calibri", FontSize: 13, Alignment: BlockAlignment.Right);
        var block = new TemplateBlock(TemplateBlockKind.Paragraph, "Текст", Style: style);

        var line = Assert.IsType<PreviewLine>(
            TemplateBlockPreview.Build(One(block), new Dictionary<string, string>()).Single());

        Assert.Equal(BlockStyleDefaults.Resolve(block.Kind, style), line.Style);
    }
}
