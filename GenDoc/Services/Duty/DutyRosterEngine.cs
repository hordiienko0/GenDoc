namespace GenDoc.Services.Duty;

/// <summary>Людина в черзі нарядів: ключ для переносу балансу і її позначки на період.</summary>
public sealed record DutyEngineMember(
    string Key,
    bool ExemptWholePeriod,
    IReadOnlySet<DateOnly> ExemptDays,
    IReadOnlySet<DateOnly> ManualDuties);

public sealed record DutyEngineInput(
    DateOnly DateFrom,
    DateOnly DateTo,
    IReadOnlyList<DutyEngineMember> Members,
    IReadOnlySet<DateOnly> ExemptDays,
    int PeoplePerDay,
    int Seed,
    DutyQueueState? StartState = null,
    bool FillAutomatically = true);

/// <summary>Стан черги однієї людини на кінець відтвореного періоду.</summary>
public sealed record DutyMemberQueueState(int Debt, int OwedSince, int Credit, DateOnly? LastDuty);

/// <summary>Стан черги «по колах» - з нього новий графік продовжує попередній.</summary>
public sealed record DutyQueueState(int Round, IReadOnlyDictionary<string, DutyMemberQueueState> Members);

public enum DutyWarningKind
{
    ShortDay,
    DutyOnExemptDay,
    ConsecutiveDays
}

public sealed record DutyWarning(DutyWarningKind Kind, DateOnly Date, int? MemberIndex = null, int Count = 0);

public sealed record DutyEngineResult(
    IReadOnlyList<IReadOnlySet<DateOnly>> AutoDuties,
    IReadOnlyList<DutyWarning> Warnings,
    DutyQueueState FinalState);

/// <summary>
/// «Черга по колах»: за одне коло кожен відстоює рівно один наряд, порядок усередині кола
/// випадковий (від Seed), тож на листі наряди розкидані, як у ручному графіку.
/// Ручні «н» не чіпаються: хто стояв понад чергу, отримує кредит і пропускає коло.
/// Кого обійшли (наприклад, через ручні «н» інших), той винен за кожне пропущене коло і стоїть першим.
/// Хто пропустив коло через звільнення, повертається з одним боргом, а не з усіма пропущеними.
/// </summary>
public static class DutyRosterEngine
{
    private sealed class MemberState
    {
        public int Debt;
        public int OwedSince;
        public int Credit;
        public DateOnly? LastDuty;
        public double RandomKey;

        /// <summary>Чи була людина вільна хоч один день поточного кола (інакше борг не накопичується).</summary>
        public bool SeenAvailable;
    }

    public static IEnumerable<DateOnly> Days(DateOnly from, DateOnly to)
    {
        for (var day = from; day <= to; day = day.AddDays(1))
            yield return day;
    }

    public static bool IsExempt(DutyEngineMember member, DateOnly day, IReadOnlySet<DateOnly> exemptDays)
        => member.ExemptWholePeriod || exemptDays.Contains(day) || member.ExemptDays.Contains(day);

    /// <summary>
    /// Випадковий ключ людини в колі залежить лише від Seed, її ключа і номера кола - не від місця в списку.
    /// Тож переставлений рядок чи доданий новенький не перемішують решту графіка.
    /// </summary>
    internal static double RandomKey(int seed, string key, int round)
    {
        unchecked
        {
            var hash = 14695981039346656037UL;
            foreach (var ch in key)
            {
                hash ^= ch;
                hash *= 1099511628211UL;
            }
            hash ^= (uint)seed;
            hash *= 1099511628211UL;
            hash ^= (uint)round;
            hash *= 1099511628211UL;

            hash ^= hash >> 30;
            hash *= 0xBF58476D1CE4E5B9UL;
            hash ^= hash >> 27;
            hash *= 0x94D049BB133111EBUL;
            hash ^= hash >> 31;
            return (hash >> 11) * (1.0 / (1UL << 53));
        }
    }

    public static DutyEngineResult Run(DutyEngineInput input)
    {
        var members = input.Members;
        var round = input.StartState?.Round ?? 0;

        var states = members.Select(m =>
        {
            var state = new MemberState { Debt = 1, OwedSince = round, RandomKey = RandomKey(input.Seed, m.Key, round) };
            if (input.StartState?.Members.TryGetValue(m.Key, out var saved) == true)
            {
                state.Debt = saved.Debt;
                state.OwedSince = saved.OwedSince;
                state.Credit = saved.Credit;
                state.LastDuty = saved.LastDuty;
            }
            return state;
        }).ToList();

        var auto = members.Select(_ => new HashSet<DateOnly>()).ToList();

        void Take(int index, DateOnly day)
        {
            var state = states[index];
            if (state.Debt > 0)
            {
                state.Debt--;
                if (state.Debt > 0) state.OwedSince = Math.Min(state.OwedSince + 1, round);
            }
            else state.Credit++;
            state.LastDuty = day;
        }

        void StartRound()
        {
            round++;
            for (var i = 0; i < states.Count; i++)
            {
                var state = states[i];
                var seen = state.SeenAvailable;
                state.SeenAvailable = false;
                state.RandomKey = RandomKey(input.Seed, members[i].Key, round);
                if (state.Credit > 0)
                {
                    state.Credit--;
                    continue;
                }

                if (state.Debt == 0) state.OwedSince = round;
                state.Debt = seen ? state.Debt + 1 : Math.Max(state.Debt, 1);
            }
        }

        int RestPenalty(int index, DateOnly day)
        {
            if (states[index].LastDuty is not DateOnly last) return 0;
            var gap = day.DayNumber - last.DayNumber;
            return gap <= 1 ? 2 : gap <= 2 ? 1 : 0;
        }

        foreach (var day in Days(input.DateFrom, input.DateTo))
        {
            var manual = Enumerable.Range(0, members.Count)
                .Where(i => members[i].ManualDuties.Contains(day))
                .ToList();

            if (input.ExemptDays.Contains(day))
            {
                foreach (var index in manual) Take(index, day);
                continue;
            }

            var available = Enumerable.Range(0, members.Count)
                .Where(i => !IsExempt(members[i], day, input.ExemptDays) || manual.Contains(i))
                .ToList();

            if (input.PeoplePerDay > 0 && available.Count(i => states[i].Debt > 0) < input.PeoplePerDay)
                StartRound();

            foreach (var index in available) states[index].SeenAvailable = true;
            foreach (var index in manual) Take(index, day);

            var need = input.PeoplePerDay - manual.Count;
            if (!input.FillAutomatically || need <= 0) continue;

            var picked = available
                .Where(i => !manual.Contains(i))
                .OrderBy(i => RestPenalty(i, day) >= 2 ? 1 : 0)
                .ThenByDescending(i => states[i].Debt)
                .ThenBy(i => states[i].Debt > 0 ? states[i].OwedSince : int.MaxValue)
                .ThenBy(i => RestPenalty(i, day))
                .ThenBy(i => states[i].RandomKey)
                .ThenBy(i => i)
                .Take(need)
                .ToList();

            foreach (var index in picked)
            {
                auto[index].Add(day);
                Take(index, day);
            }
        }

        var finalState = new DutyQueueState(round, members
            .Select((m, i) => (m.Key, State: states[i]))
            .GroupBy(x => x.Key)
            .ToDictionary(
                g => g.Key,
                g => new DutyMemberQueueState(g.First().State.Debt, g.First().State.OwedSince,
                    g.First().State.Credit, g.First().State.LastDuty)));

        return new DutyEngineResult(auto, FindWarnings(input, auto), finalState);
    }

    /// <summary>Неповні дні, «н» на звільненому дні і два дні поспіль - для підсвітки на екрані.</summary>
    public static IReadOnlyList<DutyWarning> FindWarnings(DutyEngineInput input, IReadOnlyList<IReadOnlySet<DateOnly>> auto)
    {
        var warnings = new List<DutyWarning>();
        var members = input.Members;

        bool IsDuty(int index, DateOnly day) => members[index].ManualDuties.Contains(day) || auto[index].Contains(day);

        foreach (var day in Days(input.DateFrom, input.DateTo))
        {
            var count = 0;
            for (var i = 0; i < members.Count; i++)
            {
                if (!IsDuty(i, day)) continue;
                count++;
                if (IsExempt(members[i], day, input.ExemptDays))
                    warnings.Add(new DutyWarning(DutyWarningKind.DutyOnExemptDay, day, i));
                if (day > input.DateFrom && IsDuty(i, day.AddDays(-1)))
                    warnings.Add(new DutyWarning(DutyWarningKind.ConsecutiveDays, day, i));
            }

            if (!input.ExemptDays.Contains(day) && count < input.PeoplePerDay)
                warnings.Add(new DutyWarning(DutyWarningKind.ShortDay, day, Count: count));
        }

        return warnings;
    }

    private static IReadOnlySet<DateOnly> Empty() => new HashSet<DateOnly>();

    /// <summary>Від'єднаний від БД «порожній» учасник - зручно для тестів і ручних ПІБ.</summary>
    public static DutyEngineMember Member(string key, bool exemptWholePeriod = false,
        IEnumerable<DateOnly>? exemptDays = null, IEnumerable<DateOnly>? manualDuties = null)
        => new(key, exemptWholePeriod,
            exemptDays is null ? Empty() : exemptDays.ToHashSet(),
            manualDuties is null ? Empty() : manualDuties.ToHashSet());

    /// <summary>Зміна = порядок у списку цього дня: хто вище - І, наступний - ІІ, ще нижчий - ІІІ.</summary>
    public static IReadOnlyDictionary<int, int> ShiftsOfDay(IEnumerable<int> dutyMemberIndexes)
        => dutyMemberIndexes
            .OrderBy(i => i)
            .Select((memberIndex, position) => (memberIndex, shift: position + 1))
            .ToDictionary(x => x.memberIndex, x => x.shift);

    /// <summary>Номер зміни римськими: у клітинках (якщо не «лише н») і в легенді підвалу («І Зміна»).</summary>
    public static string RomanShift(int shift) => shift switch
    {
        1 => "І",
        2 => "ІІ",
        3 => "ІІІ",
        4 => "IV",
        5 => "V",
        6 => "VI",
        _ => shift.ToString()
    };
}
