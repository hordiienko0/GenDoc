using GenDoc.ViewModels.Personnel;

namespace GenDoc.Tests.Personnel;

public class PersonCardHeaderTests
{
    [Fact]
    public void NewCard_SaysNewPerson()
        => Assert.Equal("Нова особа", PersonCardViewModel.BuildHeaderName(0, "ШЕВЧЕНКО", "Тарас Григорович"));

    [Fact]
    public void SavedCard_ShowsShortName()
        => Assert.Equal("ШЕВЧЕНКО Т.Г.", PersonCardViewModel.BuildHeaderName(42, "Шевченко", "Тарас Григорович"));

    [Fact]
    public void SavedCard_WithoutMiddleName_ShowsSingleInitial()
        => Assert.Equal("ФРАНКО І.", PersonCardViewModel.BuildHeaderName(7, "Франко", "Іван"));

    [Fact]
    public void SavedCard_WithoutGivenNames_ShowsSurnameOnly()
        => Assert.Equal("ФРАНКО", PersonCardViewModel.BuildHeaderName(7, "Франко", "   "));

    [Theory]
    [InlineData("майор", "командир роти", "майор · командир роти")]
    [InlineData("майор", "", "майор")]
    [InlineData("", "командир роти", "командир роти")]
    [InlineData(null, null, "")]
    public void HeaderSub_JoinsOnlyFilledParts(string? rank, string? position, string expected)
        => Assert.Equal(expected, PersonCardViewModel.BuildHeaderSub(rank, position));
}
