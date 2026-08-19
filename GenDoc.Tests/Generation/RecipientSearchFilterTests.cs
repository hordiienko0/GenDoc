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
}
