using System.Collections.ObjectModel;
using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Generation;

namespace GenDoc.Tests.Generation;

/// <summary>
/// Дні стрільб вводяться один раз: {{період}} (аркуші відомостей) і {{дати_стрільб}} (текст наказів і заявок)
/// беруться з одного рядка форми.
/// </summary>
public class ShootingDaysTests
{
    private const int UserId = 1;
    private const string ShootingDates = "{{дати_стрільб}}";

    private static ManualTagFormBuilder Builder(TestDb db)
    {
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Users.Add(new UserProfile { FullName = "Курсовий", PasswordHash = "x", CreatedAt = DateTime.Now });
            ctx.SaveChanges();
        }
        return TestServices.ManualTagForm(
            db, TestServices.Staff(db, UserId), new FakeCurrentUser(), new FakeIntakeAccessor(), UserId);
    }

    [Fact]
    public void DayList_OneMonth_ListsDaysThenMonthAndYear()
    {
        var days = new[] { new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 4), new DateOnly(2026, 8, 5), new DateOnly(2026, 8, 6) };

        Assert.Equal("04, 05, 06, 10 серпня 2026 року", UkrainianDate.DayList(days));
    }

    [Fact]
    public void DayList_AcrossMonthsAndYears_NamesEachMonthAndYear()
    {
        Assert.Equal("30, 31 липня, 01, 02 серпня 2026 року", UkrainianDate.DayList(new[]
        {
            new DateOnly(2026, 7, 30), new DateOnly(2026, 7, 31), new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 2)
        }));

        Assert.Equal("31 грудня 2026 року, 02 січня 2027 року", UkrainianDate.DayList(new[]
        {
            new DateOnly(2027, 1, 2), new DateOnly(2026, 12, 31)
        }));
    }

    [Fact]
    public void FormatPeriodValue_ShootingDatesInWords_PeriodAsIs_UnknownTextUntouched()
    {
        const string raw = "04.08.2026-06.08.2026, 10.08.2026";

        Assert.Equal(ManualTagKind.Period, ManualTagClassifier.Classify(ShootingDates));
        Assert.Equal("04, 05, 06, 10 серпня 2026 року", ManualTagClassifier.FormatPeriodValue(ShootingDates, raw));
        Assert.Equal(raw, ManualTagClassifier.FormatPeriodValue(XlsxGenerationService.PeriodTag, raw));
        Assert.Equal("04-06 серпня", ManualTagClassifier.FormatPeriodValue(ShootingDates, "04-06 серпня"));
    }

    [Fact]
    public async Task Form_BothTags_AreOneRow_AndEachGetsItsOwnFormat()
    {
        using var db = new TestDb();
        var builder = Builder(db);

        var form = await builder.BuildAsync(
            new[] { ShootingDates, "{{калібр}}", XlsxGenerationService.PeriodTag }, "pkg:1");

        var days = Assert.Single(form.Rows, r => r.Kind == ManualTagKind.Period);
        Assert.Equal(2, form.Rows.Count);
        Assert.StartsWith("Дні стрільб", days.Label);

        days.PeriodFrom = new DateTime(2026, 8, 4);
        days.PeriodTo = new DateTime(2026, 8, 6);
        var values = form.GetValues();

        Assert.Equal("04.08.2026-06.08.2026", values[XlsxGenerationService.PeriodTag]);
        Assert.Equal("04, 05, 06 серпня 2026 року", values[ShootingDates]);
    }

    [Fact]
    public async Task Form_OnlyShootingDates_GetsPickersAndWords()
    {
        using var db = new TestDb();
        var form = await Builder(db).BuildAsync(new[] { ShootingDates }, "pkg:1");

        var row = Assert.Single(form.Rows);
        row.Value = "04.08.2026-05.08.2026, 10.08.2026";

        Assert.Equal("04, 05, 10 серпня 2026 року", form.GetValues()[ShootingDates]);
    }

    [Fact]
    public async Task Form_EnteredDays_AreRememberedForBothTags()
    {
        using var db = new TestDb();
        var builder = Builder(db);

        var form = await builder.BuildAsync(new[] { ShootingDates, XlsxGenerationService.PeriodTag }, "pkg:1");
        form.Rows.Single().Value = "04.08.2026-06.08.2026";
        await builder.SaveAsync("pkg:1", form);

        // Наступний пакет має лише {{період}} (самі відомості) - дні підхоплюються звідти ж.
        var next = await builder.BuildAsync(new[] { XlsxGenerationService.PeriodTag }, "pkg:2");

        Assert.Equal("04.08.2026-06.08.2026", Assert.Single(next.Rows).Value);
    }

    [Fact]
    public void DocumentDate_TypedInTextBox_ReachesTheDrivenTags()
    {
        var row = ManualTagRowViewModel.DocumentDate(new[] { "{{дата_рапорту}}" }, new DateOnly(2026, 8, 6));
        var form = new ManualTagFormViewModel(new ObservableCollection<ManualTagRowViewModel> { row }, signer: null);

        row.Value = "15.09.2026";

        Assert.Equal("15.09.2026", form.GetValues()["{{дата_рапорту}}"]);
    }
}
