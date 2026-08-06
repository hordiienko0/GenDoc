using GenDoc.Services.Generation;

namespace GenDoc.Tests;

public class DateFormatCatalogTests
{
    [Fact]
    public void Format_DdMmYyyyKey_ReturnsShortDotted()
    {
        var date = new DateOnly(2026, 8, 6);

        var result = DateFormatCatalog.Format(date, DateFormatCatalog.DdMmYyyy, DateFormatCatalog.DdMmYyyy);

        Assert.Equal("06.08.2026", result);
    }

    [Fact]
    public void Format_YyyyMmDdKey_ReturnsIsoDashed()
    {
        var date = new DateOnly(2026, 8, 6);

        var result = DateFormatCatalog.Format(date, DateFormatCatalog.YyyyMmDd, DateFormatCatalog.DdMmYyyy);

        Assert.Equal("2026-08-06", result);
    }

    [Fact]
    public void Format_LongKey_ReturnsUkrainianGenitiveForm()
    {
        var date = new DateOnly(2026, 8, 6);

        var result = DateFormatCatalog.Format(date, DateFormatCatalog.Long, DateFormatCatalog.DdMmYyyy);

        Assert.Equal("6 серпня 2026 року", result);
    }

    [Fact]
    public void Format_NullFormatKey_FallsBackToFallbackKey()
    {
        var date = new DateOnly(2026, 8, 6);

        var result = DateFormatCatalog.Format(date, null, DateFormatCatalog.Long);

        Assert.Equal("6 серпня 2026 року", result);
    }

    [Fact]
    public void Options_ContainsAllThreeFormatsInOrder()
    {
        var keys = DateFormatCatalog.Options.Select(o => o.Key).ToList();

        Assert.Equal(new[] { DateFormatCatalog.DdMmYyyy, DateFormatCatalog.Long, DateFormatCatalog.YyyyMmDd }, keys);
    }
}
