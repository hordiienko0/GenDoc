using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GenDoc.Services.Templates;

namespace GenDoc.Tests;

public class TemplateServiceScanTests
{
    // Шаблон А з завдання: мануальні теги поза блоком + повторюваний блок {{#список}}.
    // Маркери й службові теги рушія (роздільник) не повинні потрапити у список полів.
    [Fact]
    public void ScanPlaceholders_GroupTemplate_DetectsBlockAndExcludesStructuralTags()
    {
        var bytes = BuildDocxWithGroupBlock();
        using var stream = new MemoryStream(bytes);
        using var doc = WordprocessingDocument.Open(stream, false);

        var result = TemplateService.ScanPlaceholders(doc);

        Assert.True(result.HasBlock);

        var tagNames = result.Tags.Select(t => t.Tag).ToList();
        Assert.Contains("{{адресат}}", tagNames);
        Assert.Contains("{{звання}}", tagNames);
        Assert.Contains("{{піб}}", tagNames);

        Assert.DoesNotContain("{{роздільник}}", tagNames);
        Assert.DoesNotContain("{{номер}}", tagNames); // явно виключений як службовий, попри збіг з RowNumber
        Assert.DoesNotContain("{{кількість_осіб}}", tagNames);
        Assert.DoesNotContain("{{#список}}", tagNames);
        Assert.DoesNotContain("{{/список}}", tagNames);

        Assert.False(result.Tags.First(t => t.Tag == "{{адресат}}").IsInsideBlock);
        Assert.True(result.Tags.First(t => t.Tag == "{{звання}}").IsInsideBlock);
        Assert.True(result.Tags.First(t => t.Tag == "{{піб}}").IsInsideBlock);
    }

    // Шаблон без блоку — HasBlock=false, усі теги позначені як "поза блоком".
    [Fact]
    public void ScanPlaceholders_PerRecipientTemplate_HasNoBlock()
    {
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            using (var wordDoc = WordprocessingDocument.Create(ms, DocumentFormat.OpenXml.WordprocessingDocumentType.Document, true))
            {
                var mainPart = wordDoc.AddMainDocumentPart();
                mainPart.Document = new Document(new Body(new Paragraph(new Run(new Text("{{піб}} — {{посада}}")))));
                mainPart.Document.Save();
            }
            bytes = ms.ToArray();
        }

        using var stream = new MemoryStream(bytes);
        using var doc = WordprocessingDocument.Open(stream, false);
        var result = TemplateService.ScanPlaceholders(doc);

        Assert.False(result.HasBlock);
        Assert.All(result.Tags, t => Assert.False(t.IsInsideBlock));
    }

    private static byte[] BuildDocxWithGroupBlock()
    {
        using var ms = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(ms, DocumentFormat.OpenXml.WordprocessingDocumentType.Document, true))
        {
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body(
                new Paragraph(new Run(new Text("{{адресат}}"))),
                new Paragraph(new Run(new Text("{{#список}}"))),
                new Paragraph(
                    new Run(new Text("{{звання}}")),
                    new Run(new TabChar()),
                    new Run(new Text("{{піб}}")),
                    new Run(new Text("{{роздільник}}"))),
                new Paragraph(new Run(new Text("{{/список}}"))),
                new Paragraph(new Run(new Text("Кількість: {{кількість_осіб}}"))),
                new Paragraph(new Run(new Text("№ {{номер}}")))));
            mainPart.Document.Save();
        }

        return ms.ToArray();
    }
}
