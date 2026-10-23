using GenDoc.Services.Duty;

namespace GenDoc.Tests.Duty;

public class DutyRosterEngineTests
{
    private static readonly DateOnly From = new(2026, 9, 19);
    private static readonly DateOnly To = new(2026, 10, 22);
    private static readonly IReadOnlySet<DateOnly> NoDays = new HashSet<DateOnly>();

    private static List<DutyEngineMember> People(int count)
        => Enumerable.Range(1, count).Select(i => DutyRosterEngine.Member($"p{i}")).ToList();

    private static DutyEngineInput Input(IReadOnlyList<DutyEngineMember> members, int seed = 7,
        IReadOnlySet<DateOnly>? exemptDays = null, DutyQueueState? state = null,
        DateOnly? from = null, DateOnly? to = null)
        => new(from ?? From, to ?? To, members, exemptDays ?? NoDays, 3, seed, state);

    private static int[] Counts(DutyEngineInput input, DutyEngineResult result)
        => input.Members.Select((m, i) => m.ManualDuties.Count + result.AutoDuties[i].Count).ToArray();

    private static IEnumerable<int> OnDuty(DutyEngineInput input, DutyEngineResult result, DateOnly day)
        => Enumerable.Range(0, input.Members.Count)
            .Where(i => input.Members[i].ManualDuties.Contains(day) || result.AutoDuties[i].Contains(day));

    [Fact]
    public void WithoutExemptions_EveryDayHasThreePeopleAndCountsDifferByAtMostOne()
    {
        var input = Input(People(18));
        var result = DutyRosterEngine.Run(input);

        foreach (var day in DutyRosterEngine.Days(From, To))
            Assert.Equal(3, OnDuty(input, result, day).Count());

        var counts = Counts(input, result);
        Assert.True(counts.Max() - counts.Min() <= 1, string.Join(",", counts));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void ExemptCellsWholeRowsAndWholeDaysNeverGetAutoDuty()
    {
        var members = People(18);
        members[0] = DutyRosterEngine.Member("p1", exemptWholePeriod: true);
        var leave = DutyRosterEngine.Days(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 8)).ToList();
        members[5] = DutyRosterEngine.Member("p6", exemptDays: leave);
        var closedDay = new DateOnly(2026, 9, 24);

        var input = Input(members, exemptDays: new HashSet<DateOnly> { closedDay });
        var result = DutyRosterEngine.Run(input);

        Assert.Empty(result.AutoDuties[0]);
        Assert.DoesNotContain(result.AutoDuties[5], leave.Contains);
        Assert.Empty(OnDuty(input, result, closedDay));
        Assert.DoesNotContain(result.Warnings, w => w.Date == closedDay);
    }

    [Fact]
    public void ManualDutiesStayAndExtraOnesAreBalancedOut()
    {
        var members = People(18);
        var forced = new[] { From, From.AddDays(2), From.AddDays(4) };
        members[3] = DutyRosterEngine.Member("p4", manualDuties: forced);

        var input = Input(members);
        var result = DutyRosterEngine.Run(input);

        foreach (var day in forced)
        {
            Assert.Contains(3, OnDuty(input, result, day));
            Assert.Equal(3, OnDuty(input, result, day).Count());
        }

        var counts = Counts(input, result);
        Assert.True(counts.Max() - counts.Min() <= 1, string.Join(",", counts));
    }

    [Fact]
    public void SameSeedGivesSameRosterAndDifferentSeedGivesAnotherOne()
    {
        var first = DutyRosterEngine.Run(Input(People(18), seed: 11));
        var again = DutyRosterEngine.Run(Input(People(18), seed: 11));
        var other = DutyRosterEngine.Run(Input(People(18), seed: 12));

        Assert.Equal(first.AutoDuties.Select(s => s.OrderBy(d => d)), again.AutoDuties.Select(s => s.OrderBy(d => d)));
        Assert.NotEqual(first.AutoDuties.Select(s => string.Join(",", s.OrderBy(d => d))),
            other.AutoDuties.Select(s => string.Join(",", s.OrderBy(d => d))));
    }

    [Fact]
    public void ReorderingRowsKeepsWhoStandsWhichDay()
    {
        var members = People(18);
        var straight = Input(members);
        var reversed = Input(members.AsEnumerable().Reverse().ToList());

        var a = DutyRosterEngine.Run(straight);
        var b = DutyRosterEngine.Run(reversed);

        string ByKey(DutyEngineInput input, DutyEngineResult result) => string.Join(";", input.Members
            .Select((m, i) => (m.Key, Days: string.Join(",", result.AutoDuties[i].OrderBy(d => d))))
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => $"{x.Key}:{x.Days}"));

        Assert.Equal(ByKey(straight, a), ByKey(reversed, b));
    }

    [Fact]
    public void RosterIsScatteredLikeAHandMadeOneNotAStaircase()
    {
        var input = Input(People(18));
        var result = DutyRosterEngine.Run(input);

        var firstDay = OnDuty(input, result, From).ToList();
        Assert.NotEqual(new[] { 0, 1, 2 }, firstDay);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void WithEnoughPeopleNobodyStandsTwoDaysInARow(int seed)
    {
        var input = Input(People(19), seed);
        var result = DutyRosterEngine.Run(input);

        Assert.DoesNotContain(result.Warnings, w => w.Kind == DutyWarningKind.ConsecutiveDays);
    }

    [Fact]
    public void PeopleSkippedBecauseOfOthersManualDutiesCatchUp()
    {
        var members = People(19);
        var handMade = new Dictionary<int, int[]>
        {
            [0] = new[] { 4, 8, 12 }, [1] = new[] { 8, 12, 15 }, [2] = new[] { 4, 17, 18 }, [3] = new[] { 9, 11, 14 },
            [4] = new[] { 3, 10, 15 }, [5] = new[] { 0, 1, 2 }, [6] = new[] { 5, 6, 7 }, [7] = new[] { 4, 12, 14 },
            [8] = new[] { 5, 8, 17 }, [9] = new[] { 3, 11, 18 }
        };
        var manual = Enumerable.Range(0, 19)
            .Select(i => handMade.Where(d => d.Value.Contains(i)).Select(d => From.AddDays(d.Key)).ToList())
            .ToList();
        members = members.Select((m, i) => DutyRosterEngine.Member(m.Key, manualDuties: manual[i])).ToList();

        var input = Input(members);
        var result = DutyRosterEngine.Run(input);

        var counts = Counts(input, result);
        Assert.Empty(manual[13]);
        Assert.True(counts.Max() - counts.Min() <= 1, string.Join(",", counts));
    }

    [Fact]
    public void ReturningFromLeaveWithTwoDebtsStillRestsBetweenDuties()
    {
        var members = People(19);
        var leave = DutyRosterEngine.Days(From.AddDays(12), From.AddDays(19)).ToList();
        members[0] = DutyRosterEngine.Member("p1", exemptDays: leave);

        foreach (var seed in Enumerable.Range(1, 20))
        {
            var result = DutyRosterEngine.Run(Input(members, seed));
            Assert.DoesNotContain(result.Warnings, w => w.Kind == DutyWarningKind.ConsecutiveDays);
        }
    }

    [Fact]
    public void TooFewPeopleLeaveShortDaysWithWarnings()
    {
        var input = Input(People(2), to: From.AddDays(2));
        var result = DutyRosterEngine.Run(input);

        var shortDays = result.Warnings.Where(w => w.Kind == DutyWarningKind.ShortDay).ToList();
        Assert.Equal(3, shortDays.Count);
        Assert.All(shortDays, w => Assert.Equal(2, w.Count));
    }

    [Fact]
    public void ManualDutyOnExemptDayIsReported()
    {
        var members = People(6);
        members[0] = DutyRosterEngine.Member("p1", exemptDays: new[] { From }, manualDuties: new[] { From });

        var result = DutyRosterEngine.Run(Input(members, to: From));

        Assert.Contains(result.Warnings, w => w.Kind == DutyWarningKind.DutyOnExemptDay && w.MemberIndex == 0);
    }

    [Fact]
    public void AfterLeavePersonOwesOneDutyNotEveryMissedRound()
    {
        var members = People(9);
        var leave = DutyRosterEngine.Days(From, From.AddDays(17)).ToList();
        members[0] = DutyRosterEngine.Member("p1", exemptDays: leave);

        var input = Input(members, to: From.AddDays(29));
        var result = DutyRosterEngine.Run(input);

        var afterLeave = result.AutoDuties[0].Where(d => d > leave[^1]).OrderBy(d => d).ToList();
        Assert.Equal(leave[^1].AddDays(1), afterLeave.First());

        var counts = Counts(input, result);
        var others = counts.Skip(1).ToArray();
        Assert.True(counts[0] < others.Min(), string.Join(",", counts));
        Assert.True(counts[0] >= 3, string.Join(",", counts));
    }

    [Fact]
    public void ShiftIsTheOrderInTheListOfThatDay()
    {
        var shifts = DutyRosterEngine.ShiftsOfDay(new[] { 18, 5, 19 });

        Assert.Equal(1, shifts[5]);
        Assert.Equal(2, shifts[18]);
        Assert.Equal(3, shifts[19]);
    }

    [Fact]
    public void LegendNumbersShiftsInRomanNumerals()
    {
        Assert.Equal("І", DutyRosterEngine.RomanShift(1));
        Assert.Equal("ІІ", DutyRosterEngine.RomanShift(2));
        Assert.Equal("ІІІ", DutyRosterEngine.RomanShift(3));
    }

    [Fact]
    public void QueueContinuesFromPreviousRoster()
    {
        var members = People(9);
        var previous = Input(members, from: From, to: From.AddDays(3));
        var replay = DutyRosterEngine.Run(previous);

        var manual = members.Select((m, i) => DutyRosterEngine.Member(m.Key, manualDuties: replay.AutoDuties[i])).ToList();
        var state = DutyRosterEngine.Run(new DutyEngineInput(From, From.AddDays(3), manual, NoDays, 3, 1,
            FillAutomatically: false)).FinalState;

        var servedOnce = Enumerable.Range(0, 9).Where(i => replay.AutoDuties[i].Count == 2).ToList();
        var waiting = Enumerable.Range(0, 9).Where(i => replay.AutoDuties[i].Count == 1).ToList();
        Assert.Equal(3, servedOnce.Count);

        var next = Input(members, seed: 99, state: state, from: From.AddDays(4), to: From.AddDays(4));
        var nextResult = DutyRosterEngine.Run(next);

        var firstDay = OnDuty(next, nextResult, From.AddDays(4)).ToList();
        Assert.Equal(3, firstDay.Count);
        Assert.All(firstDay, i => Assert.Contains(i, waiting));
    }

    [Fact]
    public void LastDayOfPreviousRosterIsNotFollowedByFirstDayOfTheNewOne()
    {
        var members = People(9);
        var lastDayCrew = new[] { 0, 1, 2 };
        var manual = members.Select((m, i) => DutyRosterEngine.Member(m.Key,
            manualDuties: lastDayCrew.Contains(i) ? new[] { From } : null)).ToList();
        var state = DutyRosterEngine.Run(new DutyEngineInput(From, From, manual, NoDays, 3, 1,
            FillAutomatically: false)).FinalState;

        var next = Input(members, seed: 5, state: state, from: From.AddDays(1), to: From.AddDays(1));
        var result = DutyRosterEngine.Run(next);

        Assert.DoesNotContain(OnDuty(next, result, From.AddDays(1)), lastDayCrew.Contains);
    }
}
