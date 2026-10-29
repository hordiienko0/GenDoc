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
            m.Exemptions.Keys.Concat(m.Blocked).Concat(m.Canteen).Where(d => d >= roster.DateFrom && d <= roster.DateTo).ToHashSet(),
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

            var shifts = roster.Members[i].Shifts;
            foreach (var day in shifts.Keys.Where(d => !duties.ContainsKey(d)).ToList())
                shifts.Remove(day);
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

    /// <summary>
    /// Звільнені на весь період (зелені рядки) стоять у самому низу, як на паперовому графіку.
    /// Порядок решти не змінюється, тож і їхні зміни лишаються ті самі. Повертає, чи щось переставлено.
    /// </summary>
    public static bool MoveExemptToBottom(DutyRosterData roster)
    {
        var ordered = roster.Members.OrderBy(m => m.ExemptWholePeriod ? 1 : 0).ToList();
        if (ordered.SequenceEqual(roster.Members)) return false;

        roster.Members.Clear();
        roster.Members.AddRange(ordered);
        return true;
    }

    public static bool IsWeekend(DateOnly day) => day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    /// <summary>
    /// Субота чи неділя без жодного наряду, хоча хтось у цей день вільний. Так виглядає графік, збережений,
    /// коли вихідні ще були днями без нарядів; його треба перерахувати, щоб вихідні теж заповнились.
    /// </summary>
    public static bool HasEmptyWeekends(DutyRosterData roster)
    {
        var members = Enumerable.Range(0, roster.Members.Count).ToList();
        return roster.Days.Any(day => IsWeekend(day)
                                      && !roster.ExemptDays.Contains(day)
                                      && !members.Any(i => roster.IsDuty(i, day))
                                      && members.Any(i => !roster.IsExempt(i, day)
                                                          && !roster.IsCanteen(i, day)
                                                          && !roster.Members[i].Blocked.Contains(day)));
    }

    public static IReadOnlyDictionary<int, int> ShiftsOfDay(DutyRosterData roster, DateOnly day)
    {
        var onDuty = Enumerable.Range(0, roster.Members.Count).Where(i => roster.IsDuty(i, day)).ToList();
        var manual = onDuty
            .Where(i => roster.Members[i].Shifts.ContainsKey(day))
            .ToDictionary(i => i, i => roster.Members[i].Shifts[day]);
        return DutyRosterEngine.ShiftsOfDay(onDuty, manual);
    }

    /// <summary>Хто в цей день стоїть на зміні, яку вручну проставили ще комусь (дві «ІІ» в один день).</summary>
    public static IReadOnlySet<int> ShiftClashes(DutyRosterData roster, DateOnly day)
        => Enumerable.Range(0, roster.Members.Count)
            .Where(i => roster.IsDuty(i, day) && roster.Members[i].Shifts.ContainsKey(day))
            .GroupBy(i => roster.Members[i].Shifts[day])
            .Where(g => g.Count() > 1)
            .SelectMany(g => g)
            .ToHashSet();

    /// <summary>Що стоїть у клітинці наряду: номер зміни римськими або «н», якщо так обрано.</summary>
    public static string DutyText(DutyRosterData roster, IReadOnlyDictionary<int, int> shiftsOfDay, int memberIndex)
        => roster.DutyMarkOnly || !shiftsOfDay.TryGetValue(memberIndex, out var shift)
            ? DutyRosterXlsxExporter.DutyMark
            : DutyRosterEngine.RomanShift(shift);
}
