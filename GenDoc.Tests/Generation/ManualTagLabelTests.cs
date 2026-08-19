using GenDoc.Services.Generation;

namespace GenDoc.Tests.Generation;

// Оператор бачив підписи полів рівно так, як їх пише розробник:
// {{дата_зарахування}}, {{кількість_патронів}}, {{причина_інструктажу}} -
// фігурні дужки й підкреслення. Людська назва виводиться з самого тега, тож
// працює й для міток, яких ніхто наперед не передбачив: словник потрібен лише
// там, де механічне правило дало б неправду.
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

    // Абревіатури механічне правило зіпсувало б: «Піб начальника» - неправда,
    // ПІБ пишеться великими. Саме для таких випадків і потрібен словник.
    [Theory]
    [InlineData("{{піб_начальника}}", "ПІБ начальника")]
    [InlineData("{{піб_підписанта}}", "ПІБ підписанта")]
    [InlineData("{{номер_вч}}", "Номер ВЧ")]
    public void Human_KeepsAbbreviationsUppercase(string tag, string expected)
    {
        Assert.Equal(expected, ManualTagLabel.Human(tag));
    }

    // Тег може прийти й без дужок (різні місця зберігають по-різному).
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

    // Підпис - це ПІДПИС, а не заміна тега: сам тег лишається доступним, бо
    // саме за ним оператор звіряється з шаблоном.
    [Fact]
    public void Human_DoesNotLoseTheOriginalTag()
    {
        const string tag = "{{опис_підрозділу}}";

        Assert.Equal("Опис підрозділу", ManualTagLabel.Human(tag));
        Assert.NotEqual(tag, ManualTagLabel.Human(tag));
    }
}
