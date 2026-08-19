using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GenDoc.Models;
using GenDoc.Services.Generation;
using GenDoc.Services.Templates;

namespace GenDoc.Tests.Generation;

// Огляд перед злиттям гілки: жоден тест у наборі не звіряв сканер із рушієм
// одне з одним, і саме тому вся ця дірка (маркери, що мовчки стираються поза
// структурним обходом) лишалась непоміченою. Ці два тести - саме та звірка,
// яка мала б це виявити раніше.
public class EngineScannerParityTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-parity-{Guid.NewGuid():N}");

    public EngineScannerParityTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static Template TemplateStub(string name) => new()
    {
        Id = 1, Name = name, OriginalFileName = $"{name}.docx",
        Content = Array.Empty<byte>(), UploadedAt = DateTime.Now
    };

    private static string ReadAllText(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        return string.Concat(doc.MainDocumentPart!.Document!.Body!.Descendants<Text>().Select(t => t.Text));
    }

    // Звичайні (не блокові) теги на п'ятьох різних глибинах: абзац рівня
    // документа, комірка звичайної таблиці, абзац у елементі керування
    // вмістом Word (w:sdt), комірка таблиці, вкладеної в комірку іншої
    // таблиці, і рядок, загорнутий у елемент керування вмістом на рівні
    // рядка (w:sdtRow) - саме глибина, яку стара «інша» підмітка бачила лише
    // для прямих дітей контейнера і пропускала тут. Без блоку - навмисно:
    // цей документ підходить під обидва режими генерації одразу.
    private static byte[] BuildDocWithTagsAtVariousDepths()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            doc.AddMainDocumentPart().Document = new Document(new Body());
            var body = doc.MainDocumentPart!.Document!.Body!;

            body.AppendChild(new Paragraph(new Run(new Text("Абзац: {{a}}"))));

            var plainCell = new TableCell(new Paragraph(new Run(new Text("{{b}}"))));
            body.AppendChild(new Table(new TableRow(plainCell)));

            body.AppendChild(new SdtBlock(new SdtContentBlock(
                new Paragraph(new Run(new Text("У елементі керування вмістом: {{c}}"))))));

            var nested = new Table(new TableRow(new TableCell(new Paragraph(new Run(new Text("{{d}}"))))));
            var outerCell = new TableCell(nested, new Paragraph());
            body.AppendChild(new Table(new TableRow(outerCell)));

            var sdtRowCell = new TableCell(new Paragraph(new Run(new Text("{{e}}"))));
            var sdtWrappedTable = new Table();
            sdtWrappedTable.AppendChild(new SdtRow(new SdtContentRow(new TableRow(sdtRowCell))));
            body.AppendChild(sdtWrappedTable);

            doc.MainDocumentPart.Document.Save();
        }
        return stream.ToArray();
    }

    // Та сама звичайна (не блокова) заміна, пропущена через обидва шляхи
    // генерації: GenerateOne іде плоским Descendants<Paragraph>() і бачить
    // усе за побудовою; GenerateGroup - структурним обходом плюс підмітанням.
    // Якщо підмітання колись перестане покривати якусь глибину, цей тест
    // розійдеться першим - саме такого порівняння в наборі раніше не було.
    [Fact]
    public void OrdinaryTagsAtEveryDepth_LeaveNoRawPlaceholders_ThroughEitherGenerationPath()
    {
        var bytes = BuildDocWithTagsAtVariousDepths();
        var values = new Dictionary<string, string>
        {
            ["{{a}}"] = "А", ["{{b}}"] = "Б", ["{{c}}"] = "В", ["{{d}}"] = "Г", ["{{e}}"] = "Д"
        };

        var onePath = Path.Combine(_folder, "one.docx");
        var oneResult = new DocumentGenerationService().GenerateOne(
            TemplateStub("Проба"), bytes, values, onePath);

        Assert.True(oneResult.Success, oneResult.ErrorMessage);
        Assert.Empty(oneResult.UnfilledTags);
        Assert.DoesNotContain("{{", ReadAllText(onePath));

        var groupPath = Path.Combine(_folder, "group.docx");
        var groupResult = new DocumentGenerationService().GenerateGroup(
            TemplateStub("Проба"), bytes,
            new List<IDictionary<string, string>>(), values, groupPath);

        Assert.True(groupResult.Success, groupResult.ErrorMessage);
        Assert.Empty(groupResult.UnfilledTags);
        Assert.DoesNotContain("{{", ReadAllText(groupPath));
    }

    // Звірка сканера з рушієм: беремо все, що сканер зібрав як теги на
    // фікстурі з тегами на різних глибинах, підставляємо значення рівно для
    // цих тегів - і рушій має заповнити геть усе без винятку. Якби сканер
    // мав сліпу зону (як до фіксу з w:sdt), тут лишився б {{тег}} без
    // значення в словнику, і UnfilledTags або сирий текст це б показали.
    [Fact]
    public void EveryTagTheEngineSubstitutes_WasCollectedByTheScanner()
    {
        var bytes = BuildDocWithTagsAtVariousDepths();

        using var scanStream = new MemoryStream(bytes);
        using var scanDoc = WordprocessingDocument.Open(scanStream, false);
        var scan = TemplateService.ScanPlaceholders(scanDoc);

        Assert.Equal(new[] { "{{a}}", "{{b}}", "{{c}}", "{{d}}", "{{e}}" },
            scan.Tags.Select(t => t.Tag).OrderBy(t => t, StringComparer.Ordinal));

        var values = scan.Tags.ToDictionary(
            t => t.Tag,
            t => $"ЗНАЧЕННЯ_{t.Tag.Trim('{', '}')}",
            StringComparer.Ordinal);

        var path = Path.Combine(_folder, "from-scan.docx");
        var result = new DocumentGenerationService().GenerateOne(
            TemplateStub("Проба"), bytes, values, path);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(result.UnfilledTags);
        Assert.DoesNotContain("{{", ReadAllText(path));
    }
}
