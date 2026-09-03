using GenDoc.Models.Enums;
using GenDoc.Services;

namespace GenDoc.Tests.Services;

public class PluralHelperTests
{
    private static string Rooms(int count)
        => PluralHelper.Pluralize(count, "кімната", "кімнати", "кімнат");

    [Theory]
    [InlineData(1, "кімната")]
    [InlineData(21, "кімната")]
    [InlineData(101, "кімната")]
    [InlineData(1001, "кімната")]
    public void OneForm(int count, string expected) => Assert.Equal(expected, Rooms(count));

    [Theory]
    [InlineData(2, "кімнати")]
    [InlineData(3, "кімнати")]
    [InlineData(4, "кімнати")]
    [InlineData(22, "кімнати")]
    [InlineData(104, "кімнати")]
    public void FewForm(int count, string expected) => Assert.Equal(expected, Rooms(count));

    [Theory]
    [InlineData(0, "кімнат")]
    [InlineData(5, "кімнат")]
    [InlineData(10, "кімнат")]
    [InlineData(20, "кімнат")]
    [InlineData(100, "кімнат")]
    public void ManyForm(int count, string expected) => Assert.Equal(expected, Rooms(count));

    [Theory]
    [InlineData(11, "кімнат")]
    [InlineData(12, "кімнат")]
    [InlineData(13, "кімнат")]
    [InlineData(14, "кімнат")]
    [InlineData(111, "кімнат")]
    [InlineData(112, "кімнат")]
    public void TeensAlwaysTakeTheManyForm(int count, string expected)
        => Assert.Equal(expected, Rooms(count));

    [Theory]
    [InlineData(-1, "кімната")]
    [InlineData(-3, "кімнати")]
    [InlineData(-11, "кімнат")]
    public void NegativeCountsUseTheSameRules(int count, string expected)
        => Assert.Equal(expected, Rooms(count));
}

public class FitnessCategoryHelperTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("придатний")]
    public void BlankOrPlainCategoryIsRegular(string? category)
        => Assert.True(FitnessCategoryHelper.IsRegular(category));

    [Theory]
    [InlineData("Придатний")]
    [InlineData("ПРИДАТНИЙ")]
    public void CaseDoesNotChangeTheAnswer(string category)
        => Assert.True(FitnessCategoryHelper.IsRegular(category));

    [Theory]
    [InlineData("обмежено придатний")]
    [InlineData("непридатний")]
    public void LimitedAndUnfitAreNotRegular(string category)
        => Assert.False(FitnessCategoryHelper.IsRegular(category));

    [Fact]
    public void UnfitIsNotMistakenForRegular()
        => Assert.False(FitnessCategoryHelper.IsRegular("непридатний"));

    [Theory]
    [InlineData(FitnessFilter.All, "придатний", true)]
    [InlineData(FitnessFilter.All, "непридатний", true)]
    [InlineData(FitnessFilter.All, null, true)]
    [InlineData(FitnessFilter.RegularOnly, "придатний", true)]
    [InlineData(FitnessFilter.RegularOnly, null, true)]
    [InlineData(FitnessFilter.RegularOnly, "обмежено придатний", false)]
    [InlineData(FitnessFilter.LimitedOnly, "обмежено придатний", true)]
    [InlineData(FitnessFilter.LimitedOnly, "придатний", false)]
    [InlineData(FitnessFilter.LimitedOnly, null, false)]
    public void FilterMatches(FitnessFilter filter, string? category, bool expected)
        => Assert.Equal(expected, FitnessCategoryHelper.Matches(filter, category));

    [Theory]
    [InlineData("придатний")]
    [InlineData("обмежено придатний")]
    [InlineData("непридатний")]
    [InlineData(null)]
    public void EveryPersonFallsIntoExactlyOneOfTheTwoNarrowFilters(string? category)
    {
        var regular = FitnessCategoryHelper.Matches(FitnessFilter.RegularOnly, category);
        var limited = FitnessCategoryHelper.Matches(FitnessFilter.LimitedOnly, category);

        Assert.NotEqual(regular, limited);
    }
}

public class HeaderNormalizationTests
{
    [Fact]
    public void TrimsAndLowercases()
        => Assert.Equal("прізвище", HeaderNormalization.Normalize("  ПРІЗВИЩЕ  "));

    [Fact]
    public void CollapsesRunsOfWhitespace()
        => Assert.Equal("особовий номер", HeaderNormalization.Normalize("Особовий    номер"));

    [Fact]
    public void TurnsLineBreaksIntoSpaces()
        => Assert.Equal("дата народження", HeaderNormalization.Normalize("Дата\r\nнародження"));

    [Theory]
    [InlineData("Ім'я")]
    [InlineData("Ім’я")]
    public void DropsBothApostropheShapes(string header)
        => Assert.Equal("імя", HeaderNormalization.Normalize(header));

    [Fact]
    public void EmptyHeaderStaysEmpty()
        => Assert.Equal(string.Empty, HeaderNormalization.Normalize("   "));

    [Fact]
    public void HyphenBecomesASpace()
        => Assert.Equal(
            HeaderNormalization.Normalize("По батькові"),
            HeaderNormalization.Normalize("По-батькові"));

    [Fact]
    public void HyphenAtTheEdgesDoesNotLeaveStraySpaces()
        => Assert.Equal("прізвище", HeaderNormalization.Normalize("-Прізвище-"));
}
