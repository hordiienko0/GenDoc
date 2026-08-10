using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GenDoc.Models;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Generation;

public class DocxRealTemplateTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-docx-{Guid.NewGuid():N}");

    public DocxRealTemplateTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private string OutputPath(string name) => Path.Combine(_folder, name);

    private static Template TemplateStub(string name) => new()
    {
        Id = 1, Name = name, OriginalFileName = $"{name}.docx",
        Content = Array.Empty<byte>(), UploadedAt = DateTime.Now
    };

    private static string ReadAllText(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var parts = new List<string>();
        if (doc.MainDocumentPart?.Document?.Body is { } body)
            parts.Add(string.Concat(body.Descendants<Text>().Select(t => t.Text)));
        foreach (var header in doc.MainDocumentPart!.HeaderParts)
        {
            if (header.Header is { } headerElement)
                parts.Add(string.Concat(headerElement.Descendants<Text>().Select(t => t.Text)));
        }
        foreach (var footer in doc.MainDocumentPart!.FooterParts)
        {
            if (footer.Footer is { } footerElement)
                parts.Add(string.Concat(footerElement.Descendants<Text>().Select(t => t.Text)));
        }
        return string.Join("\n", parts);
    }

    private static List<string> ParagraphTexts(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        // Тіло щойно відкритого/побудованого документа завжди є — це гарантія
        // структури docx, а не гіпотетичний випадок; кидаємо явно замість "!",
        // щоб компілятор бачив ненульовість без придушення попередження.
        var body = doc.MainDocumentPart?.Document?.Body
            ?? throw new InvalidOperationException($"У документі відсутнє тіло (Body): {path}");
        return body
            .Descendants<Paragraph>()
            .Select(p => string.Concat(p.Descendants<Text>().Select(t => t.Text)).Trim())
            .ToList();
    }

    [Fact]
    public void IndividualRaport_FillsEveryTag_AndLeavesNoRawPlaceholders()
    {
        var values = new Dictionary<string, string>
        {
            ["{{дата_прибуття}}"] = "01 серпня 2026 року",
            ["{{дата_зарахування}}"] = "02 серпня 2026 року",
            ["{{дата_рапорту}}"] = "07.08.2026",
            ["{{дата_посвідчення}}"] = "01 серпня 2026 року",
            ["{{номер_посвідчення}}"] = "№ 123",
            ["{{прод_атестат}}"] = "ПА-77",
            ["{{звання_зв}}"] = "майора",
            ["{{піб_зв}}"] = "ШЕВЧЕНКА Тараса Григоровича",
            ["{{прибув}}"] = "прибув",
            ["{{таким}}"] = "таким",
            ["{{звання_підписанта}}"] = "полковник",
            ["{{піб_підписанта}}"] = "І. ПЕТРЕНКО"
        };

        var path = OutputPath("individual.docx");
        var result = new DocumentGenerationService().GenerateOne(
            TemplateStub("Рапорт"), TemplateFixtures.Bytes(TemplateFixtures.RaportIndividualDocx), values, path);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(result.UnfilledTags);

        var text = ReadAllText(path);
        Assert.DoesNotContain("{{", text);
        Assert.Contains("ШЕВЧЕНКА Тараса Григоровича", text);
        Assert.Contains("ПА-77", text);
    }

    // Порожнє значення має потрапити в UnfilledTags, а тег — не лишитись сирим.
    [Fact]
    public void IndividualRaport_EmptyValue_IsReportedAsUnfilled()
    {
        var values = new Dictionary<string, string> { ["{{прод_атестат}}"] = string.Empty };

        var path = OutputPath("individual-partial.docx");
        var result = new DocumentGenerationService().GenerateOne(
            TemplateStub("Рапорт"), TemplateFixtures.Bytes(TemplateFixtures.RaportIndividualDocx), values, path);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Contains("{{прод_атестат}}", result.UnfilledTags);
    }

    [Fact]
    public void GroupRaport_ExpandsBlockOncePerPerson_WithCorrectSeparators()
    {
        var people = new[]
        {
            ("полковник ", "ШЕВЧЕНКО Т.Г."),
            ("майор ", "ФРАНКО І.Я."),
            ("капітан ", "ЛЕСЯ У.П.")
        };

        var perRecipient = people
            .Select(p => (IDictionary<string, string>)new Dictionary<string, string>
            {
                ["{{звання}}"] = p.Item1,
                ["{{піб}}"] = p.Item2
            })
            .ToList();

        var shared = new Dictionary<string, string>
        {
            ["{{дата_прибуття}}"] = "01 серпня 2026 року",
            ["{{дата_зарахування}}"] = "02 серпня 2026 року",
            ["{{дата_рапорту}}"] = "07.08.2026",
            ["{{звання_підписанта}}"] = "полковник",
            ["{{піб_підписанта}}"] = "І. ПЕТРЕНКО"
        };

        var path = OutputPath("group.docx");
        var result = new DocumentGenerationService().GenerateGroup(
            TemplateStub("Рапорт груповий"),
            TemplateFixtures.Bytes(TemplateFixtures.RaportGroupDocx),
            perRecipient, shared, path);

        Assert.True(result.Success, result.ErrorMessage);

        var paragraphs = ParagraphTexts(path);

        // Маркерні абзаци зникли повністю.
        Assert.DoesNotContain(paragraphs, p => p.StartsWith("{{#", StringComparison.Ordinal));
        Assert.DoesNotContain(paragraphs, p => p.StartsWith("{{/", StringComparison.Ordinal));

        // Кожна людина — свій абзац; останній закінчується крапкою, решта — крапкою з комою.
        var personParagraphs = paragraphs.Where(p => p.Contains("ШЕВЧЕНКО") || p.Contains("ФРАНКО") || p.Contains("ЛЕСЯ")).ToList();
        Assert.Equal(3, personParagraphs.Count);
        Assert.EndsWith(";", personParagraphs[0]);
        Assert.EndsWith(";", personParagraphs[1]);
        Assert.EndsWith(".", personParagraphs[2]);

        Assert.DoesNotContain("{{", ReadAllText(path));
    }

    [Fact]
    public void GroupRaport_EmptyRoster_RemovesBlockEntirely()
    {
        var path = OutputPath("group-empty.docx");
        var result = new DocumentGenerationService().GenerateGroup(
            TemplateStub("Рапорт груповий"),
            TemplateFixtures.Bytes(TemplateFixtures.RaportGroupDocx),
            new List<IDictionary<string, string>>(),
            new Dictionary<string, string>
            {
                ["{{дата_прибуття}}"] = "01 серпня 2026 року",
                ["{{дата_зарахування}}"] = "02 серпня 2026 року",
                ["{{дата_рапорту}}"] = "07.08.2026",
                ["{{звання_підписанта}}"] = "полковник",
                ["{{піб_підписанта}}"] = "І. ПЕТРЕНКО"
            },
            path);

        Assert.True(result.Success, result.ErrorMessage);

        var text = ReadAllText(path);
        Assert.DoesNotContain("{{звання}}", text);
        Assert.DoesNotContain("{{піб}}", text);
        Assert.DoesNotContain("{{#список}}", text);
    }

    // {{номер}} і {{кількість_осіб}} у справжньому шаблоні не трапляються —
    // перевіряємо їх на синтетичному документі з таким самим блоком.
    [Fact]
    public void GroupBlock_ProvidesRowNumberAndPeopleCount()
    {
        var bytes = BuildSyntheticGroupDocx();
        var perRecipient = new List<IDictionary<string, string>>
        {
            new Dictionary<string, string> { ["{{піб}}"] = "ПЕРШИЙ" },
            new Dictionary<string, string> { ["{{піб}}"] = "ДРУГИЙ" }
        };

        var path = OutputPath("synthetic-group.docx");
        var result = new DocumentGenerationService().GenerateGroup(
            TemplateStub("Синтетичний"), bytes, perRecipient, new Dictionary<string, string>(), path);

        Assert.True(result.Success, result.ErrorMessage);

        var paragraphs = ParagraphTexts(path);
        Assert.Contains("1. ПЕРШИЙ;", paragraphs);
        Assert.Contains("2. ДРУГИЙ.", paragraphs);
        Assert.Contains(paragraphs, p => p.Contains("Усього: 2"));
    }

    private static byte[] BuildSyntheticGroupDocx()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            doc.AddMainDocumentPart().Document = new Document(new Body());
            // Тіло щойно створено рядком вище — гарантовано не null.
            var target = doc.MainDocumentPart?.Document?.Body
                ?? throw new InvalidOperationException("Не вдалося створити тіло синтетичного документа.");

            void P(string text) => target.AppendChild(new Paragraph(new Run(new Text(text))));

            P("{{#список}}");
            P("{{номер}}. {{піб}}{{роздільник}}");
            P("{{/список}}");
            P("Усього: {{кількість_осіб}}");

            doc.MainDocumentPart.Document.Save();
        }
        return stream.ToArray();
    }

    // Побічний наслідок переходу на обхід блокових дітей: якщо в тілі блоку
    // лежить ціла таблиця, вона клонується на кожну людину — окрема таблиця на
    // особу. Раніше це була відмова (Task 17), бо обхід був плоским і абзаци
    // комірок мали інший батько, ніж маркери.
    [Fact]
    public void GroupBlockAroundTable_ClonesTheWholeTablePerPerson()
    {
        var bytes = BuildGroupDocxWithBlockInsideTable();

        var path = OutputPath("table-block.docx");
        var result = new DocumentGenerationService().GenerateGroup(
            TemplateStub("Відомість у таблиці"), bytes,
            new List<IDictionary<string, string>>
            {
                new Dictionary<string, string> { ["{{піб}}"] = "ПЕРШИЙ" },
                new Dictionary<string, string> { ["{{піб}}"] = "ДРУГИЙ" }
            },
            new Dictionary<string, string>(), path);

        Assert.True(result.Success, result.ErrorMessage);

        using var produced = WordprocessingDocument.Open(path, false);
        var tables = produced.MainDocumentPart!.Document!.Body!.Elements<Table>().ToList();

        Assert.Equal(2, tables.Count);
        Assert.Equal("ПЕРШИЙ", string.Concat(tables[0].Descendants<Text>().Select(t => t.Text)).Trim());
        Assert.Equal("ДРУГИЙ", string.Concat(tables[1].Descendants<Text>().Select(t => t.Text)).Trim());
        Assert.DoesNotContain("{{", ReadAllText(path));
    }

    // Огляд перед злиттям гілки: об'єднана по вертикалі шапка — майже
    // стандарт для списків особового складу — не бере участі в клонуванні
    // (клонується вся таблиця цілком, а не її шапка), тож не повинна більше
    // валити генерацію. Стара перевірка дивилась на будь-яке merge в тілі
    // блоку, звужена — лише на комірки рядків, що самі клонуються.
    [Fact]
    public void GroupBlockAroundTable_WithMergedHeaderCell_GeneratesSuccessfully()
    {
        var bytes = BuildGroupDocxWithMergedHeaderTable();

        var path = OutputPath("table-block-merged-header.docx");
        var result = new DocumentGenerationService().GenerateGroup(
            TemplateStub("Відомість з шапкою"), bytes,
            new List<IDictionary<string, string>>
            {
                new Dictionary<string, string> { ["{{піб}}"] = "ПЕРШИЙ" },
                new Dictionary<string, string> { ["{{піб}}"] = "ДРУГИЙ" }
            },
            new Dictionary<string, string>(), path);

        Assert.True(result.Success, result.ErrorMessage);

        using var produced = WordprocessingDocument.Open(path, false);
        var tables = produced.MainDocumentPart!.Document!.Body!.Elements<Table>().ToList();

        Assert.Equal(2, tables.Count);
        Assert.DoesNotContain("{{", ReadAllText(path));
    }

    private static byte[] BuildGroupDocxWithMergedHeaderTable()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            doc.AddMainDocumentPart().Document = new Document(new Body());
            var body = doc.MainDocumentPart!.Document!.Body!;

            body.AppendChild(new Paragraph(new Run(new Text("{{#список}}"))));

            var headerCell = new TableCell(
                new TableCellProperties(new VerticalMerge { Val = MergedCellValues.Restart }),
                new Paragraph(new Run(new Text("Шапка"))));
            var bodyCell = new TableCell(new Paragraph(new Run(new Text("{{піб}}"))));
            body.AppendChild(new Table(new TableRow(headerCell), new TableRow(bodyCell)));

            body.AppendChild(new Paragraph(new Run(new Text("{{/список}}"))));

            doc.MainDocumentPart.Document.Save();
        }
        return stream.ToArray();
    }

    private static byte[] BuildGroupDocxWithBlockInsideTable()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            doc.AddMainDocumentPart().Document = new Document(new Body());
            var body = doc.MainDocumentPart!.Document!.Body!;

            body.AppendChild(new Paragraph(new Run(new Text("{{#список}}"))));

            // Тіло блоку — усередині комірки таблиці, тобто на іншому рівні,
            // ніж маркери.
            var cell = new TableCell(new Paragraph(new Run(new Text("{{піб}}"))));
            body.AppendChild(new Table(new TableRow(cell)));

            body.AppendChild(new Paragraph(new Run(new Text("{{/список}}"))));

            doc.MainDocumentPart.Document.Save();
        }
        return stream.ToArray();
    }
}
