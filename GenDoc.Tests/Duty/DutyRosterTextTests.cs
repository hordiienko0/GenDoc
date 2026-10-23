using GenDoc.Services.Duty;

namespace GenDoc.Tests.Duty;

public class DutyRosterTextTests
{
    [Theory]
    [InlineData(2026, 10, 1, 2026, 10, 31, "на жовтень 2026 р.")]
    [InlineData(2026, 9, 19, 2026, 10, 22, "на вересень-жовтень 2026 р.")]
    [InlineData(2026, 12, 19, 2027, 1, 22, "на грудень 2026 - січень 2027 р.")]
    public void PeriodLineNamesMonthsOfThePeriod(int y1, int m1, int d1, int y2, int m2, int d2, string expected)
        => Assert.Equal(expected, DutyRosterText.PeriodLine(new DateOnly(y1, m1, d1), new DateOnly(y2, m2, d2)));

    [Fact]
    public void TitleIsHeadingLinesPlusPeriod()
    {
        var lines = DutyRosterText.TitleLines(DutyRosterText.DefaultHeading + "\r\n", new DateOnly(2026, 9, 19), new DateOnly(2026, 10, 22));

        Assert.Equal(new[]
        {
            "Графік чергувань особового складу",
            "курсів перепідготовки та підвищення кваліфікації ХНУПС",
            "на вересень-жовтень 2026 р."
        }, lines);
    }

    [Theory]
    [InlineData("Бурла", "Віктор", "Миколайович", "БУРЛА В.М.")]
    [InlineData("Васько", "євген", "Іванович", "ВАСЬКО Є.І.")]
    [InlineData("Лубянко", "Марко", null, "ЛУБЯНКО М.")]
    [InlineData("Сакал", "", "", "САКАЛ")]
    public void DisplayNameIsUppercaseSurnameWithInitials(string last, string? first, string? middle, string expected)
        => Assert.Equal(expected, DutyRosterText.DisplayName(last, first, middle));
}
