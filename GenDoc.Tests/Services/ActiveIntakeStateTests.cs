using GenDoc.Models;
using GenDoc.Services;

namespace GenDoc.Tests.Services;

// Активний набір ОДИН на всю базу й визначається датами, а не вибором профілю:
// «Зробити моїм» прибрано (рішення користувача 2026-08-31), разом із ним пішло
// й правило Pick(мій, глобальний). Лишився підрахунок днів, який показує
// статус-рядок, - його й закріплюємо.
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
        // Обидва краї рахуються: з 1 вересня по 30 листопада - 91 день.
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

    // Набір, що ще не почався або вже скінчився, не має давати від'ємний день
    // чи день більший за тривалість - у статус-рядку це виглядало б як помилка.
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
