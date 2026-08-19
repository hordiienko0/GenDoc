using GenDoc.Services;

namespace GenDoc.Tests;

// Звання - найдовше, що стоїть у списках поруч із ПІБ і посадою, і саме воно
// з'їдало ширину: «молодший лейтенант» - 18 символів. Скорочення звільняє
// місце прізвищу, яке обрізати найгірше з усього.
//
// Скорочується ЛИШЕ перше слово-ступінь («молодший»/«старший») - решта звання
// лишається як є. Документи й картка особи беруть повне звання: це підпис
// для списку, не заміна даних.
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

    // Ступінь скорочується лише як ОКРЕМЕ перше слово. «Старшина» починається
    // на ті самі літери, але це самостійне звання - калічити його не можна.
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
