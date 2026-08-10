using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GenDoc.Models;
using GenDoc.Services.Generation;

namespace GenDoc.Tests.Generation;

// Кожен випадок тут дав би зіпсований документ мовчки. Відмова з названим
// шаблоном і блоком краща за файл, який виглядає готовим.
public class TableBlockErrorTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-tblockerr-{Guid.NewGuid():N}");

    public TableBlockErrorTests() => Directory.CreateDirectory(_folder);

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

    private static byte[] Build(Action<Body> fill)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            doc.AddMainDocumentPart().Document = new Document(new Body());
            fill(doc.MainDocumentPart!.Document!.Body!);
            doc.MainDocumentPart.Document.Save();
        }
        return stream.ToArray();
    }

    private GenerationItemResult Generate(byte[] template, string fileName)
        => new DocumentGenerationService().GenerateGroup(
            TemplateStub("Проба"), template,
            new List<IDictionary<string, string>>
            {
                new Dictionary<string, string> { ["{{піб}}"] = "ПЕРШИЙ" },
                new Dictionary<string, string> { ["{{піб}}"] = "ДРУГИЙ" }
            },
            new Dictionary<string, string>(),
            Path.Combine(_folder, fileName));

    [Fact]
    public void VerticalMergeInRepeatedRow_FailsWithReadableMessage()
    {
        var merged = new TableCell(
            new TableCellProperties(new VerticalMerge { Val = MergedCellValues.Restart }),
            new Paragraph(new Run(new Text("{{піб}}"))));

        var bytes = Build(body => body.AppendChild(new Table(
            Row("{{#список}}"),
            new TableRow(merged),
            Row("{{/список}}"))));

        var result = Generate(bytes, "vmerge.docx");

        Assert.False(result.Success);
        Assert.Contains("Проба", result.ErrorMessage);
        Assert.Contains("список", result.ErrorMessage);
        Assert.Contains("вертикально", result.ErrorMessage);
    }

    [Fact]
    public void RowMarkerInsideParagraphBlock_FailsAsNestedBlock()
    {
        var bytes = Build(body =>
        {
            body.AppendChild(new Paragraph(new Run(new Text("{{#зовнішній}}"))));
            body.AppendChild(new Table(
                Row("{{#внутрішній}}"),
                Row("{{піб}}"),
                Row("{{/внутрішній}}")));
            body.AppendChild(new Paragraph(new Run(new Text("{{/зовнішній}}"))));
        });

        var result = Generate(bytes, "nested.docx");

        Assert.False(result.Success);
        Assert.Contains("Проба", result.ErrorMessage);
        Assert.Contains("внутрішній", result.ErrorMessage);
        Assert.Contains("вкладені", result.ErrorMessage);
    }

    [Fact]
    public void BlockOpenedByParagraphAndClosedByRow_FailsWithCrossingMessage()
    {
        var bytes = Build(body =>
        {
            body.AppendChild(new Paragraph(new Run(new Text("{{#список}}"))));
            body.AppendChild(new Table(
                Row("{{піб}}"),
                Row("{{/список}}")));
        });

        var result = Generate(bytes, "crossing.docx");

        Assert.False(result.Success);
        Assert.Contains("Проба", result.ErrorMessage);
        Assert.Contains("список", result.ErrorMessage);
        Assert.Contains("на одному рівні", result.ErrorMessage);
    }

    [Fact]
    public void BlockOpenedInTableAndNotClosedThere_TellsUserToCloseItInTheSameTable()
    {
        var bytes = Build(body => body.AppendChild(new Table(
            Row("{{#список}}"),
            Row("{{піб}}"))));

        var result = Generate(bytes, "unclosed-row.docx");

        Assert.False(result.Success);
        Assert.Contains("Проба", result.ErrorMessage);
        Assert.Contains("список", result.ErrorMessage);
        // Не просто "таблиц" — це стрічка спільна з повідомленням про
        // "перетин рівнів" (crossing branch), і збіг з нею тест не помітив би.
        // "не закрито в ній же" є лише в повідомленні цієї, in-table гілки.
        Assert.Contains("не закрито в ній же", result.ErrorMessage);
    }

    // Огляд перед злиттям гілки: маркерна таблиця (відкриваючий/тіло/
    // закриваючий рядки), уся загорнута в елемент керування вмістом Word,
    // невидима для структурного обходу (BlockChildren бере лише Paragraph і
    // Table серед прямих дітей контейнера). До GuardResidualMarkers це
    // мовчки тихо стиралось підміткою — Success=True, порожня таблиця.
    [Fact]
    public void MarkerTableInsideSdtBlock_FailsWithMessageNamingTemplate()
    {
        var bytes = Build(body =>
        {
            var table = new Table(Row("{{#список}}"), Row("{{піб}}"), Row("{{/список}}"));
            body.AppendChild(new SdtBlock(new SdtContentBlock(table)));
        });

        var result = Generate(bytes, "sdt-marker.docx");

        Assert.False(result.Success);
        Assert.Contains("Проба", result.ErrorMessage);
        Assert.Contains("{{#список}}", result.ErrorMessage);
    }

    // Огляд перед злиттям гілки: маркерні рядки таблиці, вкладеної в комірку
    // іншої (звичайної) таблиці. Зовнішній рядок — не маркер (його текст —
    // зчеплення текстів усіх вкладених маркерів), тож структурний обхід
    // трактує його як звичайний вміст і раніше стирав маркери всередині як
    // незаповнені теги — так само тихо, як і у випадку з w:sdt.
    [Fact]
    public void MarkerRowsInTableNestedInsideACell_FailWithMessageNamingTemplate()
    {
        var bytes = Build(body =>
        {
            var inner = new Table(Row("{{#список}}"), Row("{{піб}}"), Row("{{/список}}"));
            var cell = new TableCell(inner, new Paragraph());
            body.AppendChild(new Table(new TableRow(cell)));
        });

        var result = Generate(bytes, "nested-cell-marker.docx");

        Assert.False(result.Success);
        Assert.Contains("Проба", result.ErrorMessage);
        Assert.Contains("{{#список}}", result.ErrorMessage);
    }

    // Другий огляд перед злиттям гілки: маркер, що ділить абзац з іншим
    // текстом («Список: {{#список}}», «кінець {{/список}}»), — так само не
    // «елемент-маркер», яким уміє оперувати ProcessSiblings (той бачить лише
    // абзац, чий ЦІЛИЙ текст дорівнює маркеру). Перше виправлення робило
    // preserveMarkers безумовним і GuardResidualMarkers — лише
    // весь-абзац-разом, тож такий текст лишався буквально в тексті й
    // друкувався в Success=True документі. GuardResidualMarkers тепер шукає
    // маркер будь-де в тексті абзаца (BlockStructure.EmbeddedMarkerRegex).
    [Fact]
    public void MarkerSharingAParagraphWithOtherText_FailsWithMessageNamingTemplate()
    {
        var bytes = Build(body =>
        {
            body.AppendChild(new Paragraph(new Run(new Text("Список: {{#список}}"))));
            body.AppendChild(new Paragraph(new Run(new Text("{{піб}}"))));
            body.AppendChild(new Paragraph(new Run(new Text("кінець {{/список}}"))));
        });

        var result = Generate(bytes, "embedded-marker.docx");

        Assert.False(result.Success);
        Assert.Contains("Проба", result.ErrorMessage);
        Assert.Contains("{{#список}}", result.ErrorMessage);
    }
}
