using GenDoc.Models.Enums;

namespace GenDoc.Services.Duty;

public sealed record DutyRosterListItem(int Id, DateOnly DateFrom, DateOnly DateTo, string? IntakeLabel)
{
    public string Label => IntakeLabel is null
        ? DutyRosterText.PeriodLabel(DateFrom, DateTo)
        : $"{DutyRosterText.PeriodLabel(DateFrom, DateTo)} · {IntakeLabel}";
}

public sealed record DutyIntakeOption(int Id, string Label, DateOnly DateStart, DateOnly DateEnd);

public sealed record DutyOrgNodeOption(int? Id, string Label);

public sealed record DutyPersonOption(int RecipientId, string DisplayName, string Hint)
{
    public string Label => string.IsNullOrEmpty(Hint) ? DisplayName : $"{DisplayName} · {Hint}";
}

public sealed record DutyRosterCreateRequest(
    DateOnly DateFrom, DateOnly DateTo, int? IntakeId, int? OrgNodeId, bool UseEventExemptions, bool WeekendsExempt = true);

/// <summary>Налаштування графіка, що запам'ятовуються окремо для кожного курсового (профілю).</summary>
public sealed class DutyRosterPrefs
{
    public bool WeekendsExempt { get; set; } = true;
    public bool DutyMarkOnly { get; set; }
    public bool UseEventExemptions { get; set; } = true;
    public bool CarryOverBalance { get; set; } = true;
    public int PeoplePerDay { get; set; } = 3;
    public string Heading { get; set; } = DutyRosterText.DefaultHeading;
    public DutyRosterFooter Footer { get; set; } = new();
}

/// <summary>Звідки взято баланс: стан черги після відтворення попередніх графіків того самого складу.</summary>
public sealed record DutyCarryOver(DutyQueueState State, string FromLabel);

public sealed class DutyShiftTimes
{
    public string Day { get; set; } = string.Empty;
    public string Night { get; set; } = string.Empty;
}

/// <summary>Підвал листа: перезмінка, прибирання і години змін (як на паперовому графіку).</summary>
public sealed class DutyRosterFooter
{
    public string Changeover { get; set; } = "08:00";
    public string Cleaning { get; set; } = "22:30 - 23:30";
    public List<DutyShiftTimes> Shifts { get; set; } = DefaultShifts();

    public static List<DutyShiftTimes> DefaultShifts() => new()
    {
        new() { Day = "8:00 - 12:00", Night = "20:00 - 00:00" },
        new() { Day = "12:00 - 16:00", Night = "0:00 - 04:00" },
        new() { Day = "16:00 - 20:00", Night = "04:00 - 08:00" }
    };

    /// <summary>Легенда має стільки змін, скільки людей на добу; нові зміни - з порожнім часом.</summary>
    public void FitShifts(int count)
    {
        while (Shifts.Count < count) Shifts.Add(new DutyShiftTimes());
        if (Shifts.Count > count) Shifts.RemoveRange(count, Shifts.Count - count);
    }
}

public sealed class DutyRosterMemberData
{
    public int? RecipientId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool ExemptWholePeriod { get; set; }

    public Dictionary<DateOnly, DutyCellSource> Duties { get; } = new();
    public Dictionary<DateOnly, DutyCellSource> Exemptions { get; } = new();

    /// <summary>Дні «×»: знятий вручну авто-наряд - програма сюди людину не ставить, на листі клітинка порожня.</summary>
    public HashSet<DateOnly> Blocked { get; } = new();

    /// <summary>Ключ для переносу балансу: людина з бази - за Id, вписана вручну - за ПІБ.</summary>
    public string Key => RecipientId is int id
        ? $"r{id}"
        : "n:" + DisplayName.Trim().ToUpperInvariant();
}

public sealed class DutyRosterData
{
    public int Id { get; set; }
    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }
    public int? IntakeId { get; set; }
    public int? OrgNodeId { get; set; }
    public string? IntakeLabel { get; set; }
    public int PeoplePerDay { get; set; } = 3;
    public int Seed { get; set; }
    public string Heading { get; set; } = DutyRosterText.DefaultHeading;
    public DutyRosterFooter Footer { get; set; } = new();
    public HashSet<DateOnly> ExemptDays { get; } = new();

    /// <summary>Кого прибрали з графіка - наступний графік цього набору їх не бере.</summary>
    public HashSet<int> ExcludedRecipientIds { get; } = new();

    public bool UseEventExemptions { get; set; } = true;
    public bool CarryOverBalance { get; set; } = true;

    /// <summary>Лише «н»; типово в клітинці номер зміни римськими за порядком у списку дня.</summary>
    public bool DutyMarkOnly { get; set; }

    /// <summary>Субота й неділя одразу зелені; окремий вихідний можна зняти кліком по даті.</summary>
    public bool WeekendsExempt { get; set; } = true;

    public List<DutyRosterMemberData> Members { get; } = new();

    public IEnumerable<DateOnly> Days => DutyRosterEngine.Days(DateFrom, DateTo);

    public bool IsDuty(int memberIndex, DateOnly day) => Members[memberIndex].Duties.ContainsKey(day);

    public bool IsExempt(int memberIndex, DateOnly day)
        => Members[memberIndex].ExemptWholePeriod
           || ExemptDays.Contains(day)
           || Members[memberIndex].Exemptions.ContainsKey(day);
}
