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

    [Theory]
    [InlineData("Роздавальна відомість", "Роздавальна відомість")]
    [InlineData("Акт за шаблоном 5", "Акт за шаблоном 5")]
    public void Clean_LeavesOrdinaryNamesAlone(string input, string expected)
        => Assert.Equal(expected, TemplateNaming.Clean(input));

    [Theory]
    [InlineData("Шаблони обліку", "Шаблони обліку")]
    [InlineData("Шаблонний перелік", "Шаблонний перелік")]
    [InlineData("Шаблонування", "Шаблонування")]
    public void Clean_DoesNotBiteIntoLongerWordStartingWithTemplate(string input, string expected)
        => Assert.Equal(expected, TemplateNaming.Clean(input));

    [Theory]
    [InlineData("Рапорт котлове ГРУПОВИЙ (3)", "Рапорт котлове ГРУПОВИЙ")]
    [InlineData("Шаблон_Залік - копія", "Залік")]
    [InlineData("Залік - копія (2)", "Залік")]
    [InlineData("Залік – копія", "Залік")]
    [InlineData("Роздавальна (1) (2)", "Роздавальна")]
    public void Clean_StripsCopySuffixesFromFileNames(string input, string expected)
        => Assert.Equal(expected, TemplateNaming.Clean(input));

    [Theory]
    [InlineData("Допуск (Додаток 5)", "Допуск (Додаток 5)")]
    [InlineData("Залік 2026", "Залік 2026")]
    public void Clean_KeepsMeaningfulParenthesesAndNumbers(string input, string expected)
        => Assert.Equal(expected, TemplateNaming.Clean(input));

    [Fact]
    public void Clean_IsIdempotent()
    {
        var once = TemplateNaming.Clean("Шаблон_Залік_Додаток_8");
        Assert.Equal(once, TemplateNaming.Clean(once));
    }

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
