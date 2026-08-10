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
        Assert.True(issue.IsError); // default лишається помилкою, якщо конструктор не каже інше
    }

    // Інформаційна нотатка (пропуск через порожній склад, незаповнені теги при
    // успішній генерації) мусить пережити серіалізацію-десеріалізацію з IsError = false.
    [Fact]
    public void TryDeserialize_RoundTripsInformationalIssue_WithIsErrorFalse()
    {
        var json = RunIssue.Serialize(new List<RunIssue>
        {
            new(RunIssue.PhaseXlsx, string.Empty, "Відомість", "не заповнено теги — {{дата}}", IsError: false)
        });

        var success = RunIssue.TryDeserialize(json, out var issues);

        Assert.True(success);
        var issue = Assert.Single(issues);
        Assert.False(issue.IsError);
        Assert.Equal("не заповнено теги — {{дата}}", issue.Message);
    }

    // Зворотна сумісність: JSON, записаний до появи поля IsError (усі запуски
    // цієї гілки до цього фіксу), не містить його взагалі. System.Text.Json має
    // підставити значення за замовчуванням параметра конструктора (true), а не
    // відкинути валідний масив і піти в легасі-текстовий парсер.
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
