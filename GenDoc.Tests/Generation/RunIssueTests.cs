using GenDoc.Services.Generation;

namespace GenDoc.Tests.Generation;

public class RunIssueTests
{
    [Fact]
    public void TryDeserialize_RejectsJsonArrayOfShapeMismatchedObjects()
    {
        var success = RunIssue.TryDeserialize("[{}]", out var issues);

        Assert.False(success);
        Assert.Empty(issues);
    }

    [Fact]
    public void TryDeserialize_AcceptsWellFormedPayload_WithFieldsIntact()
    {
        var json = RunIssue.Serialize(new List<RunIssue>
        {
            new(RunIssue.PhaseXlsx, string.Empty, "Зламана відомість", "текст помилки")
        });

        var success = RunIssue.TryDeserialize(json, out var issues);

        Assert.True(success);
        var issue = Assert.Single(issues);
        Assert.Equal(RunIssue.PhaseXlsx, issue.Phase);
        Assert.Equal(string.Empty, issue.Person);
        Assert.Equal("Зламана відомість", issue.TemplateName);
        Assert.Equal("текст помилки", issue.Message);
        Assert.True(issue.IsError);
    }

    [Fact]
    public void TryDeserialize_RoundTripsInformationalIssue_WithIsErrorFalse()
    {
        var json = RunIssue.Serialize(new List<RunIssue>
        {
            new(RunIssue.PhaseXlsx, string.Empty, "Відомість", "не заповнено теги - {{дата}}", IsError: false)
        });

        var success = RunIssue.TryDeserialize(json, out var issues);

        Assert.True(success);
        var issue = Assert.Single(issues);
        Assert.False(issue.IsError);
        Assert.Equal("не заповнено теги - {{дата}}", issue.Message);
    }

    [Fact]
    public void TryDeserialize_AcceptsPayloadPredatingIsErrorField_DefaultsToError()
    {
        const string legacyJson =
            "[{\"Phase\":\"xlsx\",\"Person\":\"\",\"TemplateName\":\"Зламана відомість\",\"Message\":\"текст помилки\"}]";

        var success = RunIssue.TryDeserialize(legacyJson, out var issues);

        Assert.True(success);
        var issue = Assert.Single(issues);
        Assert.Equal("текст помилки", issue.Message);
        Assert.True(issue.IsError);
    }
}
