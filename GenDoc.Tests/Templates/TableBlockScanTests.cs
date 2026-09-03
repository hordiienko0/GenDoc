using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GenDoc.Services.Templates;

namespace GenDoc.Tests.Templates;

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

    [Fact]
    public void EngineTags_AreNotCollected()
    {
        var tags = Scan(FillTableTemplate).Tags.Select(t => t.Tag).ToList();

        Assert.DoesNotContain("{{номер}}", tags);
        Assert.DoesNotContain("{{кількість_осіб}}", tags);
    }

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

    [Fact]
    public void TagInsideSdtWrapper_IsCollectedByScanner()
    {
        var scan = Scan(body =>
        {
            body.AppendChild(new Paragraph(new Run(new Text("Наказ"))));
            body.AppendChild(new SdtBlock(new SdtContentBlock(
                new Paragraph(new Run(new Text("Підписав {{піб_командира}}"))))));
        });

        var tag = Assert.Single(scan.Tags, t => t.Tag == "{{піб_командира}}");
        Assert.False(tag.IsInsideBlock);
    }
}
