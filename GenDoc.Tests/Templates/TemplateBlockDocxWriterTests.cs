using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GenDoc.Models.TemplateBuilder;
using GenDoc.Services.Templates;

namespace GenDoc.Tests.Templates;

public class TemplateBlockDocxWriterTests
{
    private static List<string> ParagraphsOf(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        using var word = WordprocessingDocument.Open(stream, isEditable: false);

        return word.MainDocumentPart!.Document!.Body!
            .Elements<Paragraph>()
            .Select(p => p.InnerText)
            .ToList();
    }

    [Fact]
    public void Writes_a_document_that_opens_as_docx()
    {
        var doc = new TemplateBuilderDocument(new[]
        {
            new TemplateBlock(TemplateBlockKind.Title, "АКТ")
        });

        var paragraphs = ParagraphsOf(TemplateBlockDocxWriter.Write(doc));

        Assert.Equal(new[] { "АКТ" }, paragraphs);
    }

    // Головне про конструктор: він віддає звичайний шаблон із {{тегами}}, який далі
    // читає наявний конвеєр генерації. Теги мають дожити до файлу недоторканими.
    [Fact]
    public void Keeps_placeholder_tags_intact()
    {
        var doc = new TemplateBuilderDocument(new[]
        {
            new TemplateBlock(TemplateBlockKind.Paragraph, "Я, {{звання}} {{піб}}, передав.")
        });

        var paragraphs = ParagraphsOf(TemplateBlockDocxWriter.Write(doc));

        Assert.Equal("Я, {{звання}} {{піб}}, передав.", Assert.Single(paragraphs));
    }

    [Fact]
    public void Keeps_block_order()
    {
        var doc = new TemplateBuilderDocument(new[]
        {
            new TemplateBlock(TemplateBlockKind.Title, "Заголовок"),
            new TemplateBlock(TemplateBlockKind.DateAndCity, "м. {{місто}}"),
            new TemplateBlock(TemplateBlockKind.Paragraph, "Текст")
        });

        Assert.Equal(
            new[] { "Заголовок", "м. {{місто}}", "Текст" },
            ParagraphsOf(TemplateBlockDocxWriter.Write(doc)));
    }

    [Fact]
    public void Multiline_text_becomes_separate_paragraphs()
    {
        var doc = new TemplateBuilderDocument(new[]
        {
            new TemplateBlock(TemplateBlockKind.Header, "ЗАТВЕРДЖУЮ\nНачальник курсу")
        });

        Assert.Equal(
            new[] { "ЗАТВЕРДЖУЮ", "Начальник курсу" },
            ParagraphsOf(TemplateBlockDocxWriter.Write(doc)));
    }

    [Fact]
    public void Signature_uses_the_person_picked_from_permanent_staff()
    {
        var doc = new TemplateBuilderDocument(new[]
        {
            new TemplateBlock(TemplateBlockKind.Signatures, Signatures: new[]
            {
                new SignatureLine("Прийняв", 7)
            })
        });

        var signatories = new Dictionary<int, SignatoryInfo>
        {
            [7] = new("майор", "Даниленко Є. О.")
        };

        Assert.Equal(
            "Прийняв: майор _______________ Даниленко Є. О.",
            Assert.Single(ParagraphsOf(TemplateBlockDocxWriter.Write(doc, signatories))));
    }

    // Підписанта могли не обрати або він міг зникнути з постійного складу — рядок
    // мусить лишитися під ручний підпис, а не пропасти з документа.
    [Fact]
    public void Signature_without_a_known_person_still_leaves_a_line()
    {
        var doc = new TemplateBuilderDocument(new[]
        {
            new TemplateBlock(TemplateBlockKind.Signatures, Signatures: new[]
            {
                new SignatureLine("Здав", null),
                new SignatureLine("Прийняв", 999)
            })
        });

        var signatories = new Dictionary<int, SignatoryInfo>();

        Assert.Equal(
            new[] { "Здав: _______________", "Прийняв: _______________" },
            ParagraphsOf(TemplateBlockDocxWriter.Write(doc, signatories)));
    }

    private static List<List<string>> TableRowsOf(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        using var word = WordprocessingDocument.Open(stream, isEditable: false);

        return word.MainDocumentPart!.Document!.Body!
            .Elements<DocumentFormat.OpenXml.Wordprocessing.Table>()
            .Single()
            .Elements<TableRow>()
            .Select(r => r.Elements<TableCell>().Select(c => c.InnerText).ToList())
            .ToList();
    }

    private static TemplateBuilderDocument WithTable(bool repeatPerPerson) => new(new[]
    {
        new TemplateBlock(TemplateBlockKind.Table, Table: new TableSpec(new[]
        {
            new TableColumn("№ з/п", "{{номер}}"),
            new TableColumn("ПІБ", "{{піб}}")
        }, repeatPerPerson))
    });

    // Повторюваний рядок — це маркерні рядки {{#особи}}/{{/особи}} навколо рядка
    // з тегами. Саме такий синтаксис розгортає наявний рушій, тож конструктор
    // не заводить власного способу повторення.
    [Fact]
    public void Repeating_table_wraps_the_data_row_in_block_markers()
    {
        var rows = TableRowsOf(TemplateBlockDocxWriter.Write(WithTable(repeatPerPerson: true)));

        Assert.Equal(4, rows.Count);
        Assert.Equal(new[] { "№ з/п", "ПІБ" }, rows[0]);
        Assert.Equal("{{#особи}}", rows[1][0]);
        Assert.Equal(new[] { "{{номер}}", "{{піб}}" }, rows[2]);
        Assert.Equal("{{/особи}}", rows[3][0]);

        // Маркер має займати ВЕСЬ рядок (BlockStructure зчіплює текст усіх комірок),
        // тому решта комірок маркерного рядка — порожні.
        Assert.Equal(2, rows[1].Count);
        Assert.Equal(string.Empty, rows[1][1]);
    }

    [Fact]
    public void Static_table_has_no_markers()
    {
        var rows = TableRowsOf(TemplateBlockDocxWriter.Write(WithTable(repeatPerPerson: false)));

        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows.SelectMany(r => r), cell => cell.Contains("{{#") || cell.Contains("{{/"));
    }

    // Найважливіше про таблицю: зібраний .docx має читатись наявним сканером так
    // само, як завантажений файлом — інакше шаблон мовчки лишиться PerRecipient
    // і на генерації дасть документ на одну людину замість списку.
    [Fact]
    public void Scanner_sees_a_repeating_block_and_marks_tags_inside_it()
    {
        using var stream = new MemoryStream(TemplateBlockDocxWriter.Write(WithTable(repeatPerPerson: true)));
        using var word = WordprocessingDocument.Open(stream, isEditable: false);

        var scan = TemplateService.ScanPlaceholders(word);

        Assert.True(scan.HasBlock);
        Assert.All(scan.Tags, t => Assert.True(t.IsInsideBlock));

        // {{номер}} у мапінг не потрапляє навмисно: усередині повторюваного блоку
        // це обчислюваний тег рушія (номер копії), а не поле з бази.
        Assert.Equal(new[] { "{{піб}}" }, scan.Tags.Select(t => t.Tag));

        // Самі маркери — не поля, у мапінг вони потрапляти не мають.
        Assert.DoesNotContain(scan.Tags, t => t.Tag.Contains('#') || t.Tag.Contains('/'));
    }

    [Fact]
    public void Static_table_gives_no_block_to_the_scanner()
    {
        using var stream = new MemoryStream(TemplateBlockDocxWriter.Write(WithTable(repeatPerPerson: false)));
        using var word = WordprocessingDocument.Open(stream, isEditable: false);

        var scan = TemplateService.ScanPlaceholders(word);

        Assert.False(scan.HasBlock);
        Assert.All(scan.Tags, t => Assert.False(t.IsInsideBlock));
    }
}
