using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GenDoc.Models;
using GenDoc.Services.Generation;

namespace GenDoc.Tests.Generation;

// Списки людей у військових паперах майже завжди табличні, тож повторення
// рядка — не окрема фіча, а той самий блок {{#…}}/{{/…}}, лише одиниця
// повторення інша: рядок замість абзацу.
public class TableBlockTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-tblock-{Guid.NewGuid():N}");

    public TableBlockTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static Template TemplateStub(string name) => new()
    {
        Id = 1, Name = name, OriginalFileName = $"{name}.docx",
        Content = Array.Empty<byte>(), UploadedAt = DateTime.Now
    };

    private static TableRow Row(params string[] cells)
        => new(cells.Select(c => new TableCell(new Paragraph(new Run(new Text(c))))));

    // Шапка, маркерний рядок, один рядок тіла, закриваючий маркер, підсумок.
    // {{кількість_осіб}} свідомо поза блоком — воно спільне для документа.
    private static byte[] BuildTableBlockDocx()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            doc.AddMainDocumentPart().Document = new Document(new Body());
            var body = doc.MainDocumentPart!.Document!.Body!;

            body.AppendChild(new Paragraph(new Run(new Text("Список особового складу"))));
            body.AppendChild(new Table(
                Row("№", "ПІБ", "Звання"),
                Row("{{#список}}"),
                Row("{{номер}}", "{{піб}}{{роздільник}}", "{{звання}}"),
                Row("{{/список}}"),
                Row("Усього", "{{кількість_осіб}}", "")));

            doc.MainDocumentPart.Document.Save();
        }
        return stream.ToArray();
    }

    private static List<IDictionary<string, string>> People(params (string Pib, string Rank)[] people)
        => people.Select(p => (IDictionary<string, string>)new Dictionary<string, string>
        {
            ["{{піб}}"] = p.Pib,
            ["{{звання}}"] = p.Rank
        }).ToList();

    private static List<List<string>> TableCells(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var table = doc.MainDocumentPart!.Document!.Body!.Elements<Table>().Single();
        return table.Elements<TableRow>()
            .Select(r => r.Elements<TableCell>()
                .Select(c => string.Concat(c.Descendants<Text>().Select(t => t.Text)).Trim())
                .ToList())
            .ToList();
    }

    private string Generate(byte[] template, List<IDictionary<string, string>> people, string fileName)
    {
        var path = Path.Combine(_folder, fileName);
        var result = new DocumentGenerationService().GenerateGroup(
            TemplateStub("Список"), template, people, new Dictionary<string, string>(), path);

        Assert.True(result.Success, result.ErrorMessage);
        return path;
    }

    [Fact]
    public void TableBlock_ExpandsOneRowPerPerson_AndRemovesMarkerRows()
    {
        var path = Generate(BuildTableBlockDocx(),
            People(("ШЕВЧЕНКО Т.", "полковник"), ("ФРАНКО І.", "майор"), ("ЛЕСЯ У.", "капітан")),
            "expand.docx");

        var rows = TableCells(path);

        // Шапка + 3 людини + підсумок. Маркерних рядків уже нема.
        Assert.Equal(5, rows.Count);
        Assert.Equal("№", rows[0][0]);
        Assert.Equal("ШЕВЧЕНКО Т.;", rows[1][1]);
        Assert.Equal("ФРАНКО І.;", rows[2][1]);
        Assert.Equal("ЛЕСЯ У..", rows[3][1]);
        Assert.Equal("Усього", rows[4][0]);
        Assert.DoesNotContain(rows, r => r.Any(c => c.Contains("{{#") || c.Contains("{{/")));
    }

    [Fact]
    public void TableBlock_NumbersRowsFromOne()
    {
        var path = Generate(BuildTableBlockDocx(),
            People(("ПЕРШИЙ", "солдат"), ("ДРУГИЙ", "солдат")),
            "numbers.docx");

        var rows = TableCells(path);
        Assert.Equal("1", rows[1][0]);
        Assert.Equal("2", rows[2][0]);
    }

    [Fact]
    public void TableBlock_FillsSharedTagsOutsideTheBlock()
    {
        var path = Generate(BuildTableBlockDocx(),
            People(("ПЕРШИЙ", "солдат"), ("ДРУГИЙ", "солдат"), ("ТРЕТІЙ", "солдат")),
            "count.docx");

        var rows = TableCells(path);
        Assert.Equal("3", rows[^1][1]);
    }

    [Fact]
    public void TableBlock_EmptyRoster_LeavesHeaderAndFooterOnly()
    {
        var path = Generate(BuildTableBlockDocx(), new List<IDictionary<string, string>>(), "empty.docx");

        var rows = TableCells(path);
        Assert.Equal(2, rows.Count);
        Assert.Equal("№", rows[0][0]);
        Assert.Equal("Усього", rows[1][0]);
    }

    // Горизонтальне об'єднання живе у властивостях комірки, тож має пережити
    // клонування рядка без окремого коду.
    [Fact]
    public void TableBlock_KeepsHorizontalMergeOnClonedRows()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            doc.AddMainDocumentPart().Document = new Document(new Body());
            var body = doc.MainDocumentPart!.Document!.Body!;

            var wide = new TableCell(
                new TableCellProperties(new GridSpan { Val = 2 }),
                new Paragraph(new Run(new Text("{{піб}}"))));

            body.AppendChild(new Table(
                Row("{{#список}}"),
                new TableRow(wide),
                Row("{{/список}}")));

            doc.MainDocumentPart.Document.Save();
        }

        var path = Generate(stream.ToArray(),
            People(("ПЕРШИЙ", "солдат"), ("ДРУГИЙ", "солдат")),
            "gridspan.docx");

        using var produced = WordprocessingDocument.Open(path, false);
        var spans = produced.MainDocumentPart!.Document!.Body!
            .Elements<Table>().Single()
            .Elements<TableRow>()
            .SelectMany(r => r.Elements<TableCell>())
            .Select(c => c.TableCellProperties?.GridSpan?.Val?.Value)
            .Where(v => v is not null)
            .ToList();

        Assert.Equal(new int?[] { 2, 2 }, spans);
    }

    // Абзац, загорнутий у елемент керування вмістом (w:sdt), участі в блоках
    // не бере — він не маркер і не тіло. Але груповий режим не має через це
    // мовчки лишати там сирий {{тег}}: одиночний режим (GenerateOne) його б
    // заповнив, і груповий мусить поводитись так само.
    [Fact]
    public void TableBlock_FillsTagInsideContentControlWrapper()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            doc.AddMainDocumentPart().Document = new Document(new Body());
            var body = doc.MainDocumentPart!.Document!.Body!;

            body.AppendChild(new Table(
                Row("{{#список}}"),
                Row("{{піб}}"),
                Row("{{/список}}")));

            body.AppendChild(new SdtBlock(
                new SdtContentBlock(new Paragraph(new Run(new Text("Підписав {{піб_командира}}"))))));

            doc.MainDocumentPart.Document.Save();
        }

        var path = Path.Combine(_folder, "sdt.docx");
        var shared = new Dictionary<string, string> { ["{{піб_командира}}"] = "КОМАНДИР" };
        var result = new DocumentGenerationService().GenerateGroup(
            TemplateStub("Список"), stream.ToArray(),
            People(("ПЕРШИЙ", "солдат"), ("ДРУГИЙ", "солдат")),
            shared, path);

        Assert.True(result.Success, result.ErrorMessage);

        using var produced = WordprocessingDocument.Open(path, false);
        var text = string.Concat(produced.MainDocumentPart!.Document!.Body!.Descendants<Text>().Select(t => t.Text));

        Assert.Contains("Підписав КОМАНДИР", text);
        Assert.DoesNotContain("{{", text);
    }
}
