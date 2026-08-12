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

    [Fact]
    public void Table_block_is_skipped_until_it_is_implemented()
    {
        var doc = new TemplateBuilderDocument(new[]
        {
            new TemplateBlock(TemplateBlockKind.Title, "Заголовок"),
            new TemplateBlock(TemplateBlockKind.Table, Table: new TableSpec(
                new[] { new TableColumn("№", "1") }, RepeatPerPerson: true))
        });

        Assert.Equal(new[] { "Заголовок" }, ParagraphsOf(TemplateBlockDocxWriter.Write(doc)));
    }
}
