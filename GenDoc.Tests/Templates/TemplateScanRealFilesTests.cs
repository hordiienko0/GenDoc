using DocumentFormat.OpenXml.Packaging;
using GenDoc.Models.Enums;
using GenDoc.Services.Templates;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Templates;

public class TemplateScanRealFilesTests
{
    private static readonly HashSet<string> ManualTagWhitelist = new(StringComparer.Ordinal)
    {
        "{{дата_прибуття}}", "{{дата_зарахування}}", "{{дата_рапорту}}",
        "{{звання_підписанта}}", "{{піб_підписанта}}"
    };

    private static TemplateService.ScanResult Scan(string path)
    {
        using var stream = new MemoryStream(TemplateFixtures.Bytes(path));
        using var doc = WordprocessingDocument.Open(stream, false);
        return TemplateService.ScanPlaceholders(doc);
    }

    [Fact]
    public void GroupRaport_IsDetectedAsRepeatingBlockTemplate()
    {
        var scan = Scan(TemplateFixtures.RaportGroupDocx);

        Assert.True(scan.HasBlock);

        var tags = scan.Tags.ToDictionary(t => t.Tag, t => t.IsInsideBlock, StringComparer.Ordinal);

        Assert.True(tags["{{звання}}"]);
        Assert.True(tags["{{піб}}"]);

        Assert.False(tags["{{дата_прибуття}}"]);
        Assert.False(tags["{{дата_зарахування}}"]);
        Assert.False(tags["{{дата_рапорту}}"]);
        Assert.False(tags["{{звання_підписанта}}"]);
        Assert.False(tags["{{піб_підписанта}}"]);
    }

    [Fact]
    public void GroupRaport_DoesNotMapReservedBlockEngineTags()
    {
        var scan = Scan(TemplateFixtures.RaportGroupDocx);

        Assert.DoesNotContain(scan.Tags, t => t.Tag == "{{роздільник}}");
        Assert.DoesNotContain(scan.Tags, t => t.Tag == "{{номер}}");
        Assert.DoesNotContain(scan.Tags, t => t.Tag == "{{кількість_осіб}}");
    }

    [Fact]
    public void IndividualRaport_HasNoRepeatingBlock()
    {
        var scan = Scan(TemplateFixtures.RaportIndividualDocx);

        Assert.False(scan.HasBlock);
        Assert.All(scan.Tags, t => Assert.False(t.IsInsideBlock));
    }

    [Fact]
    public void IndividualRaport_ContainsExpectedRecipientTags()
    {
        var scan = Scan(TemplateFixtures.RaportIndividualDocx);
        var tags = scan.Tags.Select(t => t.Tag).ToHashSet(StringComparer.Ordinal);

        foreach (var expected in new[]
                 {
                     "{{звання_зв}}", "{{піб_зв}}", "{{прибув}}", "{{таким}}",
                     "{{номер_посвідчення}}", "{{прод_атестат}}", "{{дата_посвідчення}}"
                 })
        {
            Assert.Contains(expected, tags);
        }
    }

    [Theory]
    [InlineData(nameof(TemplateFixtures.RaportGroupDocx))]
    [InlineData(nameof(TemplateFixtures.RaportIndividualDocx))]
    public void EveryScannedTag_IsEitherMappedOrExplicitlyManual(string fixtureName)
    {
        var path = fixtureName == nameof(TemplateFixtures.RaportGroupDocx)
            ? TemplateFixtures.RaportGroupDocx
            : TemplateFixtures.RaportIndividualDocx;

        var unexpectedManual = Scan(path).Tags
            .Select(t => t.Tag)
            .Where(tag => PlaceholderTagMaps.Classify(tag).SourceType == MappingSourceType.Manual)
            .Where(tag => !ManualTagWhitelist.Contains(tag))
            .ToList();

        Assert.True(unexpectedManual.Count == 0,
            "Ці теги мовчки впали в Manual - додайте їх у PlaceholderTagMaps або в білий список тесту: "
            + string.Join(", ", unexpectedManual));
    }
}
