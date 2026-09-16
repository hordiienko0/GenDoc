using GenDoc.ViewModels.Common;

namespace GenDoc.Tests.Generation;

public class DateTextTests
{
    [Theory]
    [InlineData("01.08.2026")]
    [InlineData("1.8.2026")]
    [InlineData("01/08/2026")]
    [InlineData("01-08-2026")]
    [InlineData("01082026")]
    [InlineData("  01.08.2026  ")]
    public void AcceptsTheWaysAnOfficerTypesADate(string text)
    {
        Assert.Equal(new DateTime(2026, 8, 1), DateText.Parse(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("серпень")]
    [InlineData("32.08.2026")]
    [InlineData("2026-08-01")]
    public void RefusesWhatIsNotADate(string text)
    {
        Assert.Null(DateText.Parse(text));
    }

    [Fact]
    public void FormatsBackToTheSameMask()
    {
        Assert.Equal("01.08.2026", DateText.Format(new DateTime(2026, 8, 1)));
        Assert.Equal(string.Empty, DateText.Format((DateTime?)null));
    }

    [Fact]
    public void EmptyIsAllowed_ButGarbageIsNot()
    {
        Assert.True(DateText.IsValidOrEmpty(""));
        Assert.True(DateText.IsValidOrEmpty("01.08.2026"));
        Assert.False(DateText.IsValidOrEmpty("коли буде"));
    }
}
