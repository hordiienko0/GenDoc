using GenDoc.Services;

namespace GenDoc.Tests;

public class TemplateNamingTests
{
    [Theory]
    [InlineData("Шаблон_Залік_Додаток_8", "Залік Додаток 8")]
    [InlineData("Шаблон_Роздавальна_відомість", "Роздавальна відомість")]
    [InlineData("Шаблон_Допуск_Додаток_5", "Допуск Додаток 5")]
    [InlineData("Шаблон Рапорт котлове ІНДИВІДУАЛЬНИЙ", "Рапорт котлове ІНДИВІДУАЛЬНИЙ")]
    [InlineData("шаблон-Залік", "Залік")]
    [InlineData("ШАБЛОН: Залік", "Залік")]
    public void Clean_StripsTemplatePrefixAndUnderscores(string input, string expected)
        => Assert.Equal(expected, TemplateNaming.Clean(input));

    // Назва без префікса не повинна постраждати, а слово «шаблон» усередині назви —
    // прибирається лише з початку.
    [Theory]
    [InlineData("Роздавальна відомість", "Роздавальна відомість")]
    [InlineData("Акт за шаблоном 5", "Акт за шаблоном 5")]
    public void Clean_LeavesOrdinaryNamesAlone(string input, string expected)
        => Assert.Equal(expected, TemplateNaming.Clean(input));

    // Міграція v20 проганяє Clean по наявних назвах; повторний запуск застосунку
    // не повинен нічого зіпсувати.
    [Fact]
    public void Clean_IsIdempotent()
    {
        var once = TemplateNaming.Clean("Шаблон_Залік_Додаток_8");
        Assert.Equal(once, TemplateNaming.Clean(once));
    }

    // Назва з самого лише слова «Шаблон» не має перетворитись на порожній рядок —
    // інакше документ лишиться без імені.
    [Fact]
    public void Clean_NameThatIsOnlyThePrefix_KeepsOriginal()
        => Assert.Equal("Шаблон", TemplateNaming.Clean("Шаблон"));

    [Fact]
    public void Clean_NullOrBlank_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, TemplateNaming.Clean(null));
        Assert.Equal(string.Empty, TemplateNaming.Clean("   "));
    }

    [Fact]
    public void FormatDate_UsesDotsNotUnderscores()
        => Assert.Equal("06.08.2026", TemplateNaming.FormatDate(new DateTime(2026, 8, 6)));
}
