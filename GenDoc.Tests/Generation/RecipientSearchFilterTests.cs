using GenDoc.ViewModels.Generation;

namespace GenDoc.Tests.Generation;

public class RecipientSearchFilterTests
{
    [Theory]
    [InlineData("ШЕВЧЕНКО Тарас", null, true)]
    [InlineData("ШЕВЧЕНКО Тарас", "", true)]
    [InlineData("ШЕВЧЕНКО Тарас", "шевч", true)]
    [InlineData("ШЕВЧЕНКО Тарас", "тарас", true)]
    [InlineData("ШЕВЧЕНКО Тарас", "Іван", false)]
    public void Matches_IsCaseInsensitiveSubstring(string fullName, string? query, bool expected)
        => Assert.Equal(expected, RecipientCheckRowViewModel.Matches(fullName, query));

    [Fact]
    public void GenerateButtonText_ReflectsModeAndCount()
    {
        Assert.Equal("Згенерувати всім (34)", GenerationViewModel.BuildGenerateButtonText(true, 34, 0));
        Assert.Equal("Згенерувати обраним (3)", GenerationViewModel.BuildGenerateButtonText(false, 34, 3));
    }

    [Fact]
    public void GenerateButtonText_WithActiveIntake_NamesTheIntakeForEveryoneMode()
    {
        Assert.Equal("Згенерувати всім (Набір №5: 6)", GenerationViewModel.BuildGenerateButtonText(true, 6, 0, "Набір №5"));
        Assert.Equal("Згенерувати обраним (2)", GenerationViewModel.BuildGenerateButtonText(false, 6, 2, "Набір №5"));
    }

    [Theory]
    [InlineData(5, "Набір №5", "Набір №5")]
    [InlineData(5, "", "набір №5")]
    [InlineData(5, "  ", "набір №5")]
    public void IntakeLabel_PrefersDisplayNumber(int number, string displayNumber, string expected)
        => Assert.Equal(expected, GenerationViewModel.BuildIntakeLabel(number, displayNumber));

    [Fact]
    public void RosterScopeHint_ExplainsWhoIsInTheList()
    {
        Assert.Equal(
            "Активного набору немає — у списку всі люди бази, включно з постійним складом.",
            GenerationViewModel.BuildRosterScopeHint(null, false));
        Assert.Equal(
            "У списку — Набір №5, без постійного складу.",
            GenerationViewModel.BuildRosterScopeHint("Набір №5", false));
        Assert.Equal(
            "У списку — Набір №5 і постійний склад.",
            GenerationViewModel.BuildRosterScopeHint("Набір №5", true));
    }
}
