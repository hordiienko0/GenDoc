using GenDoc.Models;
using GenDoc.Services;

namespace GenDoc.Tests;

public class RankOrderAndCollationTests
{
    // Українська абетка: Ґ/Є/І/Ї/Й на своїх місцях. StringComparer.Ordinal (побайтове
    // порівняння UTF-16 код-пойнтів) розкидає ці літери по інших місцях — лишаємо
    // обидва твердження, щоб регресія одразу впала в очі, якщо хтось поверне Ordinal.
    [Fact]
    public void UkrainianCollation_OrdersUkrainianAlphabetCorrectly()
    {
        var names = new[]
        {
            "ЯЦЕНКО", "ЮЩУК", "КРАВЧЕНКО", "ЙОСИПЕНКО", "ЇЖАК",
            "ІВАНЕНКО", "ЗУБИК", "ЄЛАГІН", "ДУРАЧ", "ҐАЛАН"
        };

        var expected = new[]
        {
            "ҐАЛАН", "ДУРАЧ", "ЄЛАГІН", "ЗУБИК", "ІВАНЕНКО",
            "ЇЖАК", "ЙОСИПЕНКО", "КРАВЧЕНКО", "ЮЩУК", "ЯЦЕНКО"
        };

        var sortedUkrainian = names.OrderBy(n => n, UkrainianCollation.Surname).ToArray();
        Assert.Equal(expected, sortedUkrainian);

        var sortedOrdinal = names.OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.NotEqual(expected, sortedOrdinal);
    }

    [Theory]
    [InlineData("старший лейтенант", 8)]
    [InlineData("Старший лейтенант", 8)]
    [InlineData("ст. лейтенант", 8)]
    [InlineData("старший лейтенант медичної служби", 8)]
    [InlineData("майор", 6)]
    [InlineData("м-р", 6)]
    [InlineData("п/п-к", 5)]
    [InlineData("п-к", 4)]
    [InlineData("капітан 1 рангу", 4)]
    [InlineData("солдат", 20)]
    public void RankOrder_Seniority_IsStableAcrossVariants(string rank, int expectedSeniority)
    {
        Assert.Equal(expectedSeniority, RankOrder.Seniority(rank));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("—")]
    [InlineData("курсант")]
    public void RankOrder_UnrecognizedRank_SortsLastNotAsSoldier(string? rank)
    {
        Assert.Equal(int.MaxValue, RankOrder.Seniority(rank));
        Assert.Equal(RankCategory.Unknown, RankOrder.Category(rank));
    }

    [Fact]
    public void RosterOrdering_Apply_OrdersBySeniorityThenSurname()
    {
        var recipients = new List<Recipient>
        {
            Make("Юрченко", "Іван", "лейтенант"),
            Make("Бондаренко", "Петро", "лейтенант"),
            Make("Іваненко", "Сергій", "майор"),
            Make("Авраменко", "Олег", "майор"),
            Make("Дехто", "Хтось", "—"), // невідоме звання — останнє
            Make("Авраменко", "Андрій", "—"),
        };

        var ordered = RosterOrdering.Apply(recipients).ToList();

        var expectedOrder = new[]
        {
            "Авраменко Олег",   // майор
            "Іваненко Сергій",  // майор
            "Бондаренко Петро", // лейтенант
            "Юрченко Іван",     // лейтенант
            "Авраменко Андрій", // невідоме — за алфавітом
            "Дехто Хтось",
        };

        Assert.Equal(expectedOrder, ordered.Select(r => $"{r.LastName} {r.FirstName}"));
    }

    private static Recipient Make(string lastName, string firstName, string rank)
        => new() { LastName = lastName, FirstName = firstName, Rank = rank };
}
