using GenDoc.Services.Generation;

namespace GenDoc.Tests.Generation;

// RunIssue.TryDeserialize — єдине місце, чия робота полягає в тому, щоб пережити
// будь-який вміст колонки GenerationPackageRun.Summary: без CHECK-обмеження, редагованої
// вручну, з рантаймом System.Text.Json, який не перевіряє non-nullable параметри запису.
public class RunIssueTests
{
    [Fact]
    public void TryDeserialize_RejectsJsonArrayOfShapeMismatchedObjects()
    {
        // "[{}]" — валідний JSON-масив, System.Text.Json охоче створює з нього
        // RunIssue(null, null, null, null), хоча всі чотири параметри — string,
        // а не string?. Якби це пройшло як "успіх", виклик у
        // DocumentArchiveService.GetRunItemsAsync ("issue.Person.Length") впав би
        // з NullReferenceException.
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
    }
}
