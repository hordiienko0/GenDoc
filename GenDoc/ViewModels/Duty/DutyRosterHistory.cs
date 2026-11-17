using GenDoc.Services.Duty;

namespace GenDoc.ViewModels.Duty;

/// <summary>
/// «Назад» / «Вперед» у відкритому графіку: знімки графіка до кожної дії. Дія, що нічого не змінила,
/// не записується. Шапку й підвал листа (звичайні текстові поля) не повертаємо - їх порівняння не бачить.
/// </summary>
public sealed class DutyRosterHistory
{
    /// <summary>Стільки кроків «Назад» пам'ятаємо; найстаріші забуваються.</summary>
    public const int Limit = 100;

    private readonly List<DutyRosterData> _undo = new();
    private readonly Stack<DutyRosterData> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    /// <summary>Дія завершилась: <paramref name="before"/> - знімок до неї, <paramref name="after"/> - графік зараз.</summary>
    public void Record(DutyRosterData before, DutyRosterData after)
    {
        if (before.Id != after.Id || SameState(before, after)) return;
        _undo.Add(before);
        if (_undo.Count > Limit) _undo.RemoveAt(0);
        _redo.Clear();
    }

    /// <summary>Попередній стан (його можна брати як є - в історії його вже немає); поточний іде у «Вперед».</summary>
    public DutyRosterData? Undo(DutyRosterData current)
    {
        if (_undo.Count == 0) return null;
        var state = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Push(current.Clone());
        return state;
    }

    public DutyRosterData? Redo(DutyRosterData current)
    {
        if (_redo.Count == 0) return null;
        _undo.Add(current.Clone());
        return _redo.Pop();
    }

    private static bool SameState(DutyRosterData a, DutyRosterData b)
        => a.DateFrom == b.DateFrom
           && a.DateTo == b.DateTo
           && a.PeoplePerDay == b.PeoplePerDay
           && a.Seed == b.Seed
           && a.UseEventExemptions == b.UseEventExemptions
           && a.CarryOverBalance == b.CarryOverBalance
           && a.DutyMarkOnly == b.DutyMarkOnly
           && a.WeekendsGreen == b.WeekendsGreen
           && a.ShiftRotation == b.ShiftRotation
           && a.ExemptDays.SetEquals(b.ExemptDays)
           && a.ExcludedRecipientIds.SetEquals(b.ExcludedRecipientIds)
           && a.Members.Count == b.Members.Count
           && a.Members.Zip(b.Members).All(pair => SameMember(pair.First, pair.Second));

    private static bool SameMember(DutyRosterMemberData a, DutyRosterMemberData b)
        => a.RecipientId == b.RecipientId
           && a.DisplayName == b.DisplayName
           && a.ExemptWholePeriod == b.ExemptWholePeriod
           && a.AddedByHand == b.AddedByHand
           && SameMap(a.Duties, b.Duties)
           && SameMap(a.Exemptions, b.Exemptions)
           && SameMap(a.Shifts, b.Shifts)
           && a.Blocked.SetEquals(b.Blocked)
           && a.Canteen.SetEquals(b.Canteen);

    private static bool SameMap<T>(Dictionary<DateOnly, T> a, Dictionary<DateOnly, T> b)
        => a.Count == b.Count
           && a.All(pair => b.TryGetValue(pair.Key, out var other) && EqualityComparer<T>.Default.Equals(pair.Value, other));
}
