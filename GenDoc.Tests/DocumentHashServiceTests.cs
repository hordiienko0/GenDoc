using GenDoc.Services.Documents;

namespace GenDoc.Tests;

// Критично для анти-дубля групових документів (DOCX і XLSX): якщо змінюється
// підмножина одержувачів, RosterHash має змінюватись теж - інакше згенерований
// для 3 обраних людей звіт "задедуплікується" проти попереднього прогону на
// весь склад і просто не створиться.
public class DocumentHashServiceTests
{
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
