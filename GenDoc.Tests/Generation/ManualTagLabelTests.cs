using GenDoc.Services.Generation;

namespace GenDoc.Tests.Generation;

public class ManualTagLabelTests
{
    [Theory]
    [InlineData("{{дата_зарахування}}", "Дата зарахування")]
    [InlineData("{{кількість_патронів}}", "Кількість патронів")]
    [InlineData("{{причина_інструктажу}}", "Причина інструктажу")]
    [InlineData("{{номер_відомості}}", "Номер відомості")]
    public void Human_TurnsUnderscoresIntoWordsAndCapitalisesTheFirst(string tag, string expected)
    {
        Assert.Equal(expected, ManualTagLabel.Human(tag));
    }

    [Fact]
    public void Human_HandlesASingleWordTag()
    {
        Assert.Equal("Калібр", ManualTagLabel.Human("{{калібр}}"));
    }

    [Theory]
    [InlineData("{{піб_начальника}}", "ПІБ начальника")]
    [InlineData("{{піб_підписанта}}", "ПІБ підписанта")]
    [InlineData("{{номер_вч}}", "Номер ВЧ")]
    public void Human_KeepsAbbreviationsUppercase(string tag, string expected)
    {
        Assert.Equal(expected, ManualTagLabel.Human(tag));
    }

    [Fact]
    public void Human_AcceptsABareTag()
    {
        Assert.Equal("Дата рапорту", ManualTagLabel.Human("дата_рапорту"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{{}}")]
    public void Human_FallsBackToEmptyForNothing(string? tag)
    {
        Assert.Equal(string.Empty, ManualTagLabel.Human(tag));
    }

    [Fact]
    public void Human_DoesNotLoseTheOriginalTag()
    {
        const string tag = "{{опис_підрозділу}}";

        Assert.Equal("Опис підрозділу", ManualTagLabel.Human(tag));
        Assert.NotEqual(tag, ManualTagLabel.Human(tag));
    }
}
