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

    private static string Snapshot(DutyRosterData roster)
        => string.Join(";", roster.Members.Select(m => string.Join(",", m.Duties.Keys.OrderBy(d => d))));
}
