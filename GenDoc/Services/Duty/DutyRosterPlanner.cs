using GenDoc.Models.Enums;

namespace GenDoc.Services.Duty;

/// <summary>Міст між збереженим графіком і двигуном: ручне й звільнення - на вхід, авто «н» - назад у графік.</summary>
public static class DutyRosterPlanner
{
    public static DutyEngineInput ToEngineInput(DutyRosterData roster, DutyQueueState? state, bool fillAutomatically = true)
    {
        var members = roster.Members.Select(m => new DutyEngineMember(
            m.Key,
            m.ExemptWholePeriod,
            m.Exemptions.Keys.Concat(m.Blocked).Where(d => d >= roster.DateFrom && d <= roster.DateTo).ToHashSet(),
            m.Duties
                .Where(d => d.Key >= roster.DateFrom && d.Key <= roster.DateTo
                            && (!fillAutomatically || d.Value == DutyCellSource.Manual))
                .Select(d => d.Key)
                .ToHashSet())).ToList();

        return new DutyEngineInput(roster.DateFrom, roster.DateTo, members, roster.ExemptDays,
            roster.PeoplePerDay, roster.Seed, state, fillAutomatically);
    }

    /// <summary>Перераховує авто-наряди: ручні лишаються, авто замінюються новим розподілом.</summary>
    public static DutyEngineResult Replan(DutyRosterData roster, DutyQueueState? state)
    {
        var result = DutyRosterEngine.Run(ToEngineInput(roster, state));

        for (var i = 0; i < roster.Members.Count; i++)
        {
            var duties = roster.Members[i].Duties;
            foreach (var day in duties.Where(d => d.Value == DutyCellSource.Auto).Select(d => d.Key).ToList())
                duties.Remove(day);
            foreach (var day in result.AutoDuties[i])
                duties[day] = DutyCellSource.Auto;
        }

        return result;
    }

    /// <summary>Попередження поточного стану графіка без перерахунку (як він збережений).</summary>
    public static IReadOnlyList<DutyWarning> Check(DutyRosterData roster)
    {
        var input = ToEngineInput(roster, null, fillAutomatically: false);
        var none = roster.Members.Select(_ => (IReadOnlySet<DateOnly>)new HashSet<DateOnly>()).ToList();
        return DutyRosterEngine.FindWarnings(input, none);
    }

    /// <summary>Стан черги після попередніх графіків: кожен відтворюється до початку наступного.</summary>
    public static DutyQueueState? Replay(IReadOnlyList<DutyRosterData> previousInOrder, DateOnly newRosterStart)
    {
        DutyQueueState? state = null;

        for (var i = 0; i < previousInOrder.Count; i++)
        {
            var roster = previousInOrder[i];
            var nextStart = i + 1 < previousInOrder.Count ? previousInOrder[i + 1].DateFrom : newRosterStart;
            var end = roster.DateTo < nextStart ? roster.DateTo : nextStart.AddDays(-1);
            if (end < roster.DateFrom) continue;

            var input = ToEngineInput(roster, state, fillAutomatically: false) with { DateTo = end };
            state = DutyRosterEngine.Run(input).FinalState;
        }

        return state;
    }

    public static bool IsWeekend(DateOnly day) => day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    /// <summary>
    /// «Вихідні - зеленим»: увімкнено - усі суботи й неділі періоду стають зеленими днями;
    /// вимкнено - зелене з вихідних прибирається.
    /// </summary>
    public static void ApplyWeekends(DutyRosterData roster)
    {
        foreach (var day in roster.Days.Where(IsWeekend))
        {
            if (roster.WeekendsExempt) roster.ExemptDays.Add(day);
            else roster.ExemptDays.Remove(day);
        }
    }

    public static IReadOnlyDictionary<int, int> ShiftsOfDay(DutyRosterData roster, DateOnly day)
        => DutyRosterEngine.ShiftsOfDay(Enumerable.Range(0, roster.Members.Count).Where(i => roster.IsDuty(i, day)));

    /// <summary>Що стоїть у клітинці наряду: номер зміни римськими або «н», якщо так обрано.</summary>
    public static string DutyText(DutyRosterData roster, IReadOnlyDictionary<int, int> shiftsOfDay, int memberIndex)
        => roster.DutyMarkOnly || !shiftsOfDay.TryGetValue(memberIndex, out var shift)
            ? DutyRosterXlsxExporter.DutyMark
            : DutyRosterEngine.RomanShift(shift);
}
