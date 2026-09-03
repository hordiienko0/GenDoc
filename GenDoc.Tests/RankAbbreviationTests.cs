using GenDoc.Services;

namespace GenDoc.Tests;

public class RankAbbreviationTests
{
    [Theory]
    [InlineData("молодший лейтенант", "мол. лейтенант")]
    [InlineData("старший лейтенант", "ст. лейтенант")]
    [InlineData("молодший сержант", "мол. сержант")]
    [InlineData("старший солдат", "ст. солдат")]
    public void Short_AbbreviatesTheLeadingDegreeWord(string rank, string expected)
    {
        Assert.Equal(expected, RankAbbreviation.Short(rank));
    }

    [Theory]
    [InlineData("капітан")]
    [InlineData("майор")]
    [InlineData("підполковник")]
    [InlineData("солдат")]
    public void Short_LeavesRanksWithoutADegreeWordAlone(string rank)
    {
        Assert.Equal(rank, RankAbbreviation.Short(rank));
    }

    [Fact]
    public void Short_DoesNotTouchStarshyna()
    {
        Assert.Equal("старшина", RankAbbreviation.Short("старшина"));
    }

    [Fact]
    public void Short_IsCaseInsensitiveAndKeepsTheRest()
    {
        Assert.Equal("ст. Лейтенант", RankAbbreviation.Short("Старший Лейтенант"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Short_HandlesMissingRank(string? rank)
    {
        Assert.Equal(string.Empty, RankAbbreviation.Short(rank));
    }
}
