using GenDoc.Models.Enums;
using GenDoc.Services.Duty;

namespace GenDoc.Tests.Duty;

public class DutyRosterPlannerTests
{
    private static readonly DateOnly From = new(2026, 9, 19);

    private static DutyRosterData Roster(int people, int days = 10)
    {
        var roster = new DutyRosterData { DateFrom = From, DateTo = From.AddDays(days - 1), Seed = 3 };
        for (var i = 1; i <= people; i++)
            roster.Members.Add(new DutyRosterMemberData { DisplayName = $"ЛЮДИНА{i:00} А.Б." });
        return roster;
    }

    [Fact]
    public void ReplanKeepsManualReplacesAutoAndRespectsBlockedDays()
    {
        var roster = Roster(9);
        roster.Members[0].Duties[From] = DutyCellSource.Manual;
        roster.Members[1].Duties[From.AddDays(5)] = DutyCellSource.Auto;
        foreach (var day in roster.Days) roster.Members[2].Blocked.Add(day);

        DutyRosterPlanner.Replan(roster, null);

        Assert.Equal(DutyCellSource.Manual, roster.Members[0].Duties[From]);
        Assert.Empty(roster.Members[2].Duties);
        Assert.All(roster.Days, d => Assert.Equal(3, Enumerable.Range(0, 9).Count(i => roster.IsDuty(i, d))));
        Assert.False(roster.IsExempt(2, From));
    }

    [Fact]
    public void ReplanWithSameSeedIsStableAndNewSeedChangesAutoOnly()
    {
        var roster = Roster(12, days: 20);
        roster.Members[4].Duties[From.AddDays(3)] = DutyCellSource.Manual;

        DutyRosterPlanner.Replan(roster, null);
        var first = Snapshot(roster);
        DutyRosterPlanner.Replan(roster, null);
        Assert.Equal(first, Snapshot(roster));

        roster.Seed = 4;
        DutyRosterPlanner.Replan(roster, null);
        Assert.NotEqual(first, Snapshot(roster));
        Assert.Equal(DutyCellSource.Manual, roster.Members[4].Duties[From.AddDays(3)]);
    }

    [Fact]
    public void CheckReportsSavedProblemsWithoutReplanning()
    {
        var roster = Roster(3, days: 2);
        roster.Members[0].Exemptions[From] = DutyCellSource.Event;
        roster.Members[0].Duties[From] = DutyCellSource.Auto;

        var warnings = DutyRosterPlanner.Check(roster);

        Assert.Contains(warnings, w => w.Kind == DutyWarningKind.DutyOnExemptDay && w.MemberIndex == 0);
        Assert.Equal(2, warnings.Count(w => w.Kind == DutyWarningKind.ShortDay));
        Assert.Single(roster.Members[0].Duties);
    }

    [Fact]
    public void CanteenDayKeepsThePersonOutOfTheQueueThatDay()
    {
        var roster = Roster(4, days: 3);
        roster.Members[1].Canteen.Add(From.AddDays(1));

        DutyRosterPlanner.Replan(roster, null);

        Assert.False(roster.IsDuty(1, From.AddDays(1)));
        Assert.Equal(3, Enumerable.Range(0, 4).Count(i => roster.IsDuty(i, From.AddDays(1))));
    }

    [Fact]
    public void CanteenMovesTheDutyAwayAndTheRosterStaysFair()
    {
        var roster = Roster(12, days: 30);
        DutyRosterPlanner.Replan(roster, null);
        var day = From.AddDays(10);
        var moved = Enumerable.Range(0, 12).First(i => roster.IsDuty(i, day));
        roster.Members[moved].Canteen.Add(day);

        DutyRosterPlanner.Replan(roster, null);

        Assert.False(roster.IsDuty(moved, day));
        Assert.False(roster.IsDuty(moved, day.AddDays(-1)));
        Assert.False(roster.IsDuty(moved, day.AddDays(1)));
        var counts = roster.Members.Select(m => m.Duties.Count).ToList();
        Assert.True(counts.Max() - counts.Min() <= 1, string.Join(",", counts));

        var shifts = DutyRosterPlanner.ShiftsByDay(roster);
        Assert.All(roster.Days, d => Assert.Equal(new[] { 1, 2, 3 }, shifts[d].Values.Order()));
    }

    [Fact]
    public void RosterFromBeforeShiftRotationKeepsShiftsByListOrder()
    {
        var roster = Roster(9);
        roster.ShiftRotation = false;
        DutyRosterPlanner.Replan(roster, null);

        var shifts = DutyRosterPlanner.ShiftsByDay(roster);

        Assert.All(roster.Days, d => Assert.Equal(new[] { 1, 2, 3 }, shifts[d].OrderBy(s => s.Key).Select(s => s.Value)));
    }

    [Fact]
    public void ExemptWholePeriodGoToTheBottomInTheirOrder()
    {
        var roster = Roster(5);
        roster.Members[0].ExemptWholePeriod = true;
        roster.Members[2].ExemptWholePeriod = true;

        Assert.True(DutyRosterPlanner.MoveExemptToBottom(roster));
        Assert.Equal(new[] { "ЛЮДИНА02 А.Б.", "ЛЮДИНА04 А.Б.", "ЛЮДИНА05 А.Б.", "ЛЮДИНА01 А.Б.", "ЛЮДИНА03 А.Б." },
            roster.Members.Select(m => m.DisplayName));
        Assert.False(DutyRosterPlanner.MoveExemptToBottom(roster));
    }

    [Fact]
    public void EmptyWeekendIsFoundOnlyWhenSomeoneCouldStand()
    {
        var roster = Roster(3, days: 3);
        DutyRosterPlanner.Replan(roster, null);
        Assert.False(DutyRosterPlanner.HasEmptyWeekends(roster));

        foreach (var member in roster.Members) member.Duties.Remove(From);
        Assert.True(DutyRosterPlanner.HasEmptyWeekends(roster));

        roster.ExemptDays.Add(From);
        Assert.False(DutyRosterPlanner.HasEmptyWeekends(roster));
        roster.ExemptDays.Clear();

        foreach (var member in roster.Members) member.Blocked.Add(From);
        Assert.False(DutyRosterPlanner.HasEmptyWeekends(roster));
    }

    private static string Snapshot(DutyRosterData roster)
        => string.Join(";", roster.Members.Select(m => string.Join(",", m.Duties.Keys.OrderBy(d => d))));
}
