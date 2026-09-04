using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Documents;

namespace GenDoc.Tests;

public class DocumentHashServiceTests
{
    private static readonly Recipient Person = new() { LastName = "ШЕВЧЕНКО", FirstName = "Тарас", Rank = "солдат" };

    private static List<TemplateFieldMapping> Mappings(bool withManual) => new()
    {
        new() { PlaceholderTag = "{{прізвище}}", SourceType = MappingSourceType.Recipient, FieldName = "LastName" },
        new() { PlaceholderTag = "{{номер_наказу}}", SourceType = withManual ? MappingSourceType.Manual : MappingSourceType.Recipient, FieldName = withManual ? null : "FirstName" }
    };

    [Fact]
    public void ComputeSourceHash_WithManualValues_ChangesWhenAManualValueChanges()
    {
        var service = new DocumentHashService();
        var mappings = Mappings(withManual: true);

        var first = service.ComputeSourceHash(mappings, Person, null, new Dictionary<string, string> { ["{{номер_наказу}}"] = "12" }, null);
        var second = service.ComputeSourceHash(mappings, Person, null, new Dictionary<string, string> { ["{{номер_наказу}}"] = "13" }, null);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ComputeSourceHash_WithManualValues_KeepsTheAutoPartComparable()
    {
        var service = new DocumentHashService();
        var mappings = Mappings(withManual: true);

        var full = service.ComputeSourceHash(mappings, Person, null, new Dictionary<string, string> { ["{{номер_наказу}}"] = "12" }, null);
        var auto = service.ComputeSourceHash(mappings, Person, null);

        Assert.Equal(auto, DocumentHashService.AutoPart(full));
        Assert.Equal(auto, DocumentHashService.AutoPart(auto));
    }

    [Fact]
    public void ComputeSourceHash_NoManualDrivenMappings_EqualsTheAutoOnlyHash()
    {
        var service = new DocumentHashService();
        var mappings = Mappings(withManual: false);

        var full = service.ComputeSourceHash(mappings, Person, null, new Dictionary<string, string> { ["{{номер_наказу}}"] = "12" }, null);

        Assert.Equal(service.ComputeSourceHash(mappings, Person, null), full);
    }

    [Fact]
    public void ComputeSourceHash_CourseOfficerSignatureCounts()
    {
        var service = new DocumentHashService();
        var mappings = new List<TemplateFieldMapping>
        {
            new() { PlaceholderTag = "{{курсовий_офіцер}}", SourceType = MappingSourceType.Recipient, FieldName = nameof(ExportFieldKey.CourseOfficerSignature) }
        };
        var none = new Dictionary<string, string>();

        Assert.NotEqual(
            service.ComputeSourceHash(mappings, Person, null, none, "майор Мельник П. І."),
            service.ComputeSourceHash(mappings, Person, null, none, "капітан Ковальчук В. П."));
    }

    [Fact]
    public void ComputeRosterHash_ManualFingerprint_ChangesTheHash()
    {
        var service = new DocumentHashService();
        var roster = new (int RecipientId, string SourceHash)[] { (1, "a"), (2, "b") };

        var plain = service.ComputeRosterHash(5, roster);
        var withValues = service.ComputeRosterHash(5, roster, "відбиток-1");
        var otherValues = service.ComputeRosterHash(5, roster, "відбиток-2");

        Assert.NotEqual(plain, withValues);
        Assert.NotEqual(withValues, otherValues);
        Assert.Equal(plain, service.ComputeRosterHash(5, roster, null));
    }

    [Fact]
    public void ComputeRosterHash_AutoPartIgnoresTheManualFingerprint()
    {
        var service = new DocumentHashService();
        var roster = new (int RecipientId, string SourceHash)[] { (1, "a"), (2, "b") };

        var plain = service.ComputeRosterHash(5, roster);
        var withValues = service.ComputeRosterHash(5, roster, "відбиток-1");

        Assert.Equal(plain, DocumentHashService.AutoPart(withValues));
        Assert.Equal(plain, DocumentHashService.AutoPart(plain));
    }
    [Fact]
    public void ComputeRosterHash_DifferentSubsetOfSameRoster_ProducesDifferentHash()
    {
        var service = new DocumentHashService();

        var full = new (int RecipientId, string SourceHash)[]
        {
            (1, "hash1"), (2, "hash2"), (3, "hash3"), (4, "hash4"),
        };
        var subset = new (int RecipientId, string SourceHash)[]
        {
            (1, "hash1"), (2, "hash2"),
        };

        var fullHash = service.ComputeRosterHash(exportTemplateId: 10, roster: full);
        var subsetHash = service.ComputeRosterHash(exportTemplateId: 10, roster: subset);

        Assert.NotEqual(fullHash, subsetHash);
    }

    [Fact]
    public void ComputeRosterHash_SameRosterSameTemplate_IsStable()
    {
        var service = new DocumentHashService();
        var roster = new (int RecipientId, string SourceHash)[] { (1, "a"), (2, "b") };

        var first = service.ComputeRosterHash(5, roster);
        var second = service.ComputeRosterHash(5, roster);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ComputeRosterHash_DifferentTemplateId_ProducesDifferentHash()
    {
        var service = new DocumentHashService();
        var roster = new (int RecipientId, string SourceHash)[] { (1, "a"), (2, "b") };

        var forTemplateA = service.ComputeRosterHash(1, roster);
        var forTemplateB = service.ComputeRosterHash(2, roster);

        Assert.NotEqual(forTemplateA, forTemplateB);
    }
}
