namespace GenDoc.Services.Duty;

/// <summary>
/// Людина в черзі нарядів: ключ для переносу балансу і її позначки на період.
/// <see cref="BusyDays"/> - дні, коли людина зайнята поза чергою (столова): у наряд її в ці дні не ставлять
/// (вони є і в <see cref="ExemptDays"/>), а поруч із ними - лише якщо інакше не виходить, як і після наряду.
/// </summary>
public sealed record DutyEngineMember(
    string Key,
    bool ExemptWholePeriod,
    IReadOnlySet<DateOnly> ExemptDays,
    IReadOnlySet<DateOnly> ManualDuties,
    IReadOnlySet<DateOnly>? BusyDays = null);

/// <summary>Хто стоїть у наряді цього дня (індекси в списку) і кому зміну проставлено вручну.</summary>
public sealed record DutyShiftDay(DateOnly Date, IReadOnlyList<int> OnDuty, IReadOnlyDictionary<int, int> ManualShifts);

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

        // Відпочинок рахується і від наряду, і від столової - до неї й після неї.
        int RestPenalty(int index, DateOnly day)
        {
            var gap = states[index].LastDuty is DateOnly last ? day.DayNumber - last.DayNumber : int.MaxValue;
            if (members[index].BusyDays is { } busy)
            {
                foreach (var other in busy)
                    gap = Math.Min(gap, Math.Abs(other.DayNumber - day.DayNumber));
            }
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
        IEnumerable<DateOnly>? exemptDays = null, IEnumerable<DateOnly>? manualDuties = null,
        IEnumerable<DateOnly>? canteenDays = null)
    {
        var busy = canteenDays?.ToHashSet();
        var exempt = (exemptDays ?? Enumerable.Empty<DateOnly>()).Concat(busy ?? Enumerable.Empty<DateOnly>()).ToHashSet();
        return new(key, exemptWholePeriod, exempt, manualDuties is null ? Empty() : manualDuties.ToHashSet(), busy);
    }

    /// <summary>Ціна повтору середньої зміни: ІІ після ІІ.</summary>
    private const double MiddleRepeatPenalty = 1;

    /// <summary>Після середньої зміни ІІІ трохи дорожча за І: вибір між ними випадковий, але частіше І.</summary>
    private const double MiddleLean = 0.15;

    /// <summary>Випадкова добавка до ціни зміни: розводить рівні варіанти без огляду на місце в списку.</summary>
    private const double ShiftNoise = 0.4;

    /// <summary>Більше ніж стільки людей без ручної зміни в один день - номери за порядком у списку (перебір завеликий).</summary>
    private const int MaxRotatedPerDay = 12;

    private sealed class ShiftHistory
    {
        public int? Last;
        public readonly Dictionary<int, int> Counts = new();

        public int Count(int shift) => Counts.GetValueOrDefault(shift);

        public void Add(int shift)
        {
            Last = shift;
            Counts[shift] = Count(shift) + 1;
        }
    }

    /// <summary>
    /// Зміни по черзі на весь період, день за днем. Хто минулого разу був на останній зміні (ІІІ), той тепер
    /// на першій, хто був на І - на ІІІ, після середньої (ІІ) - випадково, але частіше на І. На добу лише одна
    /// І і одна ІІІ, тож коли бажаних змін не вистачає всім, решта розводиться так, щоб кожен мав порівну
    /// І, ІІ і ІІІ: зміна, яку людина вже мала частіше за інші, дорожча. Розподіл дня - найдешевший з усіх
    /// можливих. Місце в списку не важить нічого - рівні варіанти розводить випадковий ключ від Seed, як і в
    /// черзі нарядів. Зміна, проставлена вручну, лишається за людиною й теж іде в історію; решта займає вільні номери.
    /// </summary>
    public static IReadOnlyDictionary<DateOnly, IReadOnlyDictionary<int, int>> RotateShifts(
        IReadOnlyList<string> memberKeys, IEnumerable<DutyShiftDay> days, int shiftsPerDay, int seed)
    {
        var history = memberKeys.Select(_ => new ShiftHistory()).ToList();
        var result = new Dictionary<DateOnly, IReadOnlyDictionary<int, int>>();

        foreach (var day in days.OrderBy(d => d.Date))
        {
            var ordered = day.OnDuty.OrderBy(i => i).ToList();
            var shifts = new Dictionary<int, int>();
            foreach (var index in ordered)
                if (day.ManualShifts.TryGetValue(index, out var manual)) shifts[index] = manual;

            var free = ordered.Where(i => !shifts.ContainsKey(i)).ToList();
            var taken = shifts.Values.ToHashSet();
            var slots = new List<int>();
            for (var next = 1; slots.Count < free.Count; next++)
                if (!taken.Contains(next)) slots.Add(next);

            if (free.Count > MaxRotatedPerDay)
            {
                for (var k = 0; k < free.Count; k++) shifts[free[k]] = slots[k];
            }
            else
            {
                var cost = free
                    .Select(i => slots.Select(s => ShiftCost(history[i], s, shiftsPerDay)
                                                   + ShiftNoise * RandomKey(seed, $"{memberKeys[i]}|{s}", day.Date.DayNumber)).ToArray())
                    .ToArray();
                var assigned = CheapestAssignment(cost);
                for (var k = 0; k < free.Count; k++) shifts[free[k]] = slots[assigned[k]];
            }

            foreach (var index in ordered) history[index].Add(shifts[index]);
            result[day.Date] = shifts;
        }

        return result;
    }

    /// <summary>
    /// Ціна зміни для людини з такою історією - без випадкової добавки. Бажана зміна - дзеркальна до минулої
    /// (ІІІ → І, І → ІІІ), ціна росте з відстанню від неї, а повтор - найдорожчий. Хто був на ІІІ, важить удвічі
    /// більше за того, хто був на І: коли обом не вистачає, першим своє отримує той, хто був на ІІІ.
    /// Плюс по одиниці за кожен раз, що людина мала цю зміну частіше за свою найрідшу.
    /// </summary>
    private static double ShiftCost(ShiftHistory history, int shift, int shiftsPerDay)
    {
        var cost = 0.0;
        if (history.Last is int previous && shiftsPerDay > 1)
        {
            var last = Math.Clamp(previous, 1, shiftsPerDay);
            var mirror = shiftsPerDay + 1 - last;
            if (mirror == last)
                cost += shift == 1 ? 0 : shift == last ? MiddleRepeatPenalty : MiddleLean;
            else
            {
                var weight = 1 + (last - 1) / (double)(shiftsPerDay - 1);
                var distance = shift == last ? shiftsPerDay - 1 : Math.Abs(shift - mirror);
                cost += distance * weight;
            }
        }

        if (shift <= shiftsPerDay)
        {
            var fewest = Enumerable.Range(1, shiftsPerDay).Min(history.Count);
            cost += history.Count(shift) - fewest;
        }
        return cost;
    }

    /// <summary>
    /// Найдешевший розподіл: людина k → слот result[k], кожен слот рівно один раз.
    /// Перебір підмножин (2^n · n) - на добу кілька людей, тож миттєво.
    /// </summary>
    private static int[] CheapestAssignment(double[][] cost)
    {
        var n = cost.Length;
        var full = (1 << n) - 1;
        var best = Enumerable.Repeat(double.PositiveInfinity, full + 1).ToArray();
        var choice = new int[full + 1];
        best[0] = 0;

        for (var mask = 0; mask < full; mask++)
        {
            if (double.IsPositiveInfinity(best[mask])) continue;
            var person = System.Numerics.BitOperations.PopCount((uint)mask);
            for (var slot = 0; slot < n; slot++)
            {
                if ((mask & (1 << slot)) != 0) continue;
                var next = mask | (1 << slot);
                var total = best[mask] + cost[person][slot];
                if (total < best[next])
                {
                    best[next] = total;
                    choice[next] = slot;
                }
            }
        }

        var result = new int[n];
        for (var mask = full; mask != 0; mask ^= 1 << choice[mask])
            result[System.Numerics.BitOperations.PopCount((uint)mask) - 1] = choice[mask];
        return result;
    }

    /// <summary>
    /// Зміна = порядок у списку цього дня: хто вище - І, наступний - ІІ, ще нижчий - ІІІ.
    /// Зміна, проставлена вручну, лишається за людиною; решта займає вільні номери за порядком у списку.
    /// Так рахуються графіки, складені до черги змін (<see cref="RotateShifts"/>), - щоб друкувались як були.
    /// </summary>
    public static IReadOnlyDictionary<int, int> ShiftsOfDay(IEnumerable<int> dutyMemberIndexes,
        IReadOnlyDictionary<int, int>? manualShifts = null)
    {
        var ordered = dutyMemberIndexes.OrderBy(i => i).ToList();
        var shifts = new Dictionary<int, int>();
        if (manualShifts is not null)
        {
            foreach (var index in ordered)
                if (manualShifts.TryGetValue(index, out var manual)) shifts[index] = manual;
        }

        var taken = shifts.Values.ToHashSet();
        var next = 1;
        foreach (var index in ordered.Where(i => !shifts.ContainsKey(i)))
        {
            while (taken.Contains(next)) next++;
            shifts[index] = next++;
        }
        return shifts;
    }

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
