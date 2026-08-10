using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GenDoc.Services.Templates;

namespace GenDoc.Tests.Templates;

// Якщо сканер не бачить маркерних рядків, шаблон класифікується як документ на
// одну людину й ніколи не потрапляє в групову фазу — мовчки, без помилки.
public class TableBlockScanTests
{
    private static TableRow Row(params string[] cells)
        => new(cells.Select(c => new TableCell(new Paragraph(new Run(new Text(c))))));

    private static TemplateService.ScanResult Scan(Action<Body> fill)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            doc.AddMainDocumentPart().Document = new Document(new Body());
            fill(doc.MainDocumentPart!.Document!.Body!);
            doc.MainDocumentPart.Document.Save();
        }

        using var reopened = WordprocessingDocument.Open(new MemoryStream(stream.ToArray()), false);
        return TemplateService.ScanPlaceholders(reopened);
    }

    private static void FillTableTemplate(Body body)
    {
        body.AppendChild(new Paragraph(new Run(new Text("Затверджую {{піб_командира}}"))));
        body.AppendChild(new Table(
            Row("№", "ПІБ"),
            Row("{{#список}}"),
            Row("{{номер}}", "{{піб}} {{звання}}"),
            Row("{{/список}}"),
            Row("Усього", "{{кількість_осіб}}")));
    }

    // ── Характеризаційні: проходять і до змін ────────────────────────
    // Плоский обхід перелічує абзаци в порядку документа, включно з тими, що в
    // комірках, тож для простої таблиці він маркерний рядок таки бачить. Ці
    // тести не доводять виправлення — вони стережуть, щоб перебудова нічого не
    // зламала.

    [Fact]
    public void TableMarkerRows_MarkTemplateAsGroup()
    {
        var scan = Scan(FillTableTemplate);
        Assert.True(scan.HasBlock);
    }

    [Fact]
    public void TagsInsideRepeatedRow_AreFlaggedAsInsideBlock()
    {
        var tags = Scan(FillTableTemplate).Tags.ToDictionary(t => t.Tag, t => t.IsInsideBlock, StringComparer.Ordinal);

        Assert.True(tags["{{піб}}"]);
        Assert.True(tags["{{звання}}"]);
        Assert.False(tags["{{піб_командира}}"]);
    }

    // {{номер}} і {{кількість_осіб}} обчислює рушій — вони не поля й у мапінг
    // потрапляти не мають, інакше з'являться зайві рядки у формі ручних міток.
    [Fact]
    public void EngineTags_AreNotCollected()
    {
        var tags = Scan(FillTableTemplate).Tags.Select(t => t.Tag).ToList();

        Assert.DoesNotContain("{{номер}}", tags);
        Assert.DoesNotContain("{{кількість_осіб}}", tags);
    }

    // ── Розрізняльні: падають до змін ────────────────────────────────
    // Обидва нижче падають на плоскому обході й проходять лише після
    // перебудови на BlockStructure — вони і є доказом того, що задача не
    // декоративна.

    // Плоский обхід тримає стан блоку в одній змінній на весь контейнер, тож
    // незакритий маркер у таблиці позначає «всередині блоку» все, що йде далі
    // по документу. Структурований обхід тримає стан у межах тієї таблиці.
    [Fact]
    public void UnclosedMarkerRow_DoesNotLeakBlockStateOntoTheRestOfTheDocument()
    {
        var scan = Scan(body =>
        {
            body.AppendChild(new Table(
                Row("{{#список}}"),
                Row("{{піб}}")));
            body.AppendChild(new Paragraph(new Run(new Text("Підписав {{піб_командира}}"))));
        });

        var tags = scan.Tags.ToDictionary(t => t.Tag, t => t.IsInsideBlock, StringComparer.Ordinal);

        Assert.False(tags["{{піб_командира}}"]);
    }

    // Маркерний рядок усередині блоку рівня документа: плоский обхід приймає
    // його за справжній блок і на «{{/внутрішній}}» закриває облік, тож теги
    // після нього, але всередині зовнішнього блоку, лишаються непозначеними.
    [Fact]
    public void RowMarkerInsideParagraphBlock_DoesNotCloseTheOuterBlockEarly()
    {
        var scan = Scan(body =>
        {
            body.AppendChild(new Paragraph(new Run(new Text("{{#зовнішній}}"))));
            body.AppendChild(new Table(
                Row("{{#внутрішній}}"),
                Row("{{піб}}"),
                Row("{{/внутрішній}}")));
            body.AppendChild(new Paragraph(new Run(new Text("{{звання}}"))));
            body.AppendChild(new Paragraph(new Run(new Text("{{/зовнішній}}"))));
        });

        var tags = scan.Tags.ToDictionary(t => t.Tag, t => t.IsInsideBlock, StringComparer.Ordinal);

        Assert.True(tags["{{звання}}"]);
    }

    // ── Захист від регресії перебудови (проходить і до, і після) ──────
    // Цей тест НЕ падає на плоскому обході — там анкоровані BlockOpenRegex/
    // BlockCloseRegex перевіряють текст усього абзацу чи комірки-рядка й
    // роблять `continue` до PlaceholderRegex.Matches, тож маркер-рядок з
    // одним осередком ("{{#внутрішній}}") ніколи не доходить до збирача полів
    // — «зелено» тут нічого не доводить про стару поведінку.
    //
    // Але сама перебудова на BlockStructure вводить новий шлях: коли блок
    // рівня документа вже відкритий, ScanSiblings більше не заходить у
    // таблицю окремо — CollectTags отримує всю таблицю разом з маркерним
    // рядком і йде по кожному її абзацу звичайним регексом плейсхолдера, під
    // яким «{{#внутрішній}}» так само підпадає. Без явної перевірки
    // BlockStructure.IsMarkerTag маркер осів би в мапінгу як поле й
    // з'явився б фантомним рядком у формі ручних міток. Тест лишається
    // корисним не як розрізняльний, а як вартовий саме цього нового шляху
    // (перевірено: видалення `if (BlockStructure.IsMarkerTag(...)) continue;`
    // валить цей тест — див. task-3-report.md).
    [Fact]
    public void BlockMarkersAreNeverCollectedAsFields()
    {
        var scan = Scan(body =>
        {
            body.AppendChild(new Paragraph(new Run(new Text("{{#зовнішній}}"))));
            body.AppendChild(new Table(
                Row("{{#внутрішній}}"),
                Row("{{піб}}"),
                Row("{{/внутрішній}}")));
            body.AppendChild(new Paragraph(new Run(new Text("{{/зовнішній}}"))));
        });

        Assert.DoesNotContain(scan.Tags, t => t.Tag.StartsWith("{{#", StringComparison.Ordinal));
        Assert.DoesNotContain(scan.Tags, t => t.Tag.StartsWith("{{/", StringComparison.Ordinal));
    }

    [Fact]
    public void TemplateWithoutMarkers_IsNotAGroupTemplate()
    {
        var scan = Scan(body => body.AppendChild(new Table(
            Row("№", "ПІБ"),
            Row("1", "{{піб}}"))));

        Assert.False(scan.HasBlock);
        Assert.All(scan.Tags, t => Assert.False(t.IsInsideBlock));
    }
}
