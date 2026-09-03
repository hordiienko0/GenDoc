using GenDoc.Models;
using GenDoc.Services;

namespace GenDoc.Tests.Services;

public class ActiveIntakeStateTests
{
    private static Intake Intake(string start, string end) => new()
    {
        Id = 1,
        Number = 1,
        DisplayNumber = "Набір №1",
        DateStart = DateOnly.Parse(start),
        DateEnd = DateOnly.Parse(end)
    };

    [Fact]
    public void TheFirstDayIsDayOne()
    {
        var (day, total) = ActiveIntakeState.DayOfTotal(
            Intake("2026-09-01", "2026-11-30"), new DateOnly(2026, 9, 1));

        Assert.Equal(1, day);
        Assert.Equal(91, total);
    }

    [Fact]
    public void TheLastDayIsTheTotal()
    {
        var (day, total) = ActiveIntakeState.DayOfTotal(
            Intake("2026-09-01", "2026-09-10"), new DateOnly(2026, 9, 10));

        Assert.Equal(10, day);
        Assert.Equal(10, total);
    }

    [Fact]
    public void ADateBeforeTheStartClampsToTheFirstDay()
    {
        var (day, _) = ActiveIntakeState.DayOfTotal(
            Intake("2026-09-01", "2026-09-10"), new DateOnly(2026, 8, 20));

        Assert.Equal(1, day);
    }

    [Fact]
    public void ADateAfterTheEndClampsToTheLastDay()
    {
        var (day, total) = ActiveIntakeState.DayOfTotal(
            Intake("2026-09-01", "2026-09-10"), new DateOnly(2026, 12, 31));

        Assert.Equal(total, day);
    }

    [Fact]
    public void AOneDayIntakeIsDayOneOfOne()
    {
        var (day, total) = ActiveIntakeState.DayOfTotal(
            Intake("2026-09-01", "2026-09-01"), new DateOnly(2026, 9, 1));

        Assert.Equal(1, day);
        Assert.Equal(1, total);
    }
}
