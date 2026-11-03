using GenDoc.Models.Enums;

namespace GenDoc.Services.Duty;

public sealed record DutyRosterListItem(int Id, DateOnly DateFrom, DateOnly DateTo, string? FoldersLabel)
{
    public string Label => FoldersLabel is null
        ? DutyRosterText.PeriodLabel(DateFrom, DateTo)
        : $"{DutyRosterText.PeriodLabel(DateFrom, DateTo)} · {FoldersLabel}";
}

/// <summary>
/// Папка дерева для вибору складу графіка. <see cref="PeopleIds"/> - хто піде в графік з цієї папки:
/// люди її і вкладених папок, а коренева папка набору бере й людей набору без папки.
/// </summary>
public sealed record DutyFolderOption(int Id, int? ParentId, string Name, int? IntakeId, bool IsIntakeRoot, IReadOnlyList<int> PeopleIds);

public sealed record DutyPersonOption(int RecipientId, string DisplayName, string Hint)
{
    public string Label => string.IsNullOrEmpty(Hint) ? DisplayName : $"{DisplayName} · {Hint}";
}

/// <summary>Новий графік з людей вибраних папок (разом із вкладеними); без папок - людей вписують вручну.</summary>
public sealed record DutyRosterCreateRequest(
    DateOnly DateFrom, DateOnly DateTo, IReadOnlyCollection<int> FolderIds, bool UseEventExemptions, bool WeekendsGreen = true);

/// <summary>Налаштування графіка, що запам'ятовуються окремо для кожного курсового (профілю).</summary>
public sealed class DutyRosterPrefs
{
    public bool WeekendsGreen { get; set; } = true;
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

    /// <summary>Доданий кнопкою «Додати», а не з папок графіка - наступний графік бере його теж.</summary>
    public bool AddedByHand { get; set; }

    public Dictionary<DateOnly, DutyCellSource> Duties { get; } = new();
    public Dictionary<DateOnly, DutyCellSource> Exemptions { get; } = new();

    /// <summary>Дні «×»: знятий вручну авто-наряд - програма сюди людину не ставить, на листі клітинка порожня.</summary>
    public HashSet<DateOnly> Blocked { get; } = new();

    /// <summary>Жовта «к» - наряд по столовій: ставиться лише вручну, у чергу нарядів не рахується.</summary>
    public HashSet<DateOnly> Canteen { get; } = new();

    /// <summary>Номер зміни, проставлений вручну для дня наряду; решта в цей день - за порядком у списку.</summary>
    public Dictionary<DateOnly, int> Shifts { get; } = new();

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
    /// <summary>Набір, якщо всі папки графіка з одного набору - тоді «Додати» пропонує його людей і постійний склад.</summary>
    public int? IntakeId { get; set; }

    /// <summary>Папки, чиї люди в графіку (без вкладених у вже вибрані); порожньо - людей вписано вручну.</summary>
    public List<int> FolderIds { get; } = new();

    /// <summary>Назви папок графіка для списку й підпису: «Набір №29, Командировані».</summary>
    public string? FoldersLabel { get; set; }
    public int PeoplePerDay { get; set; } = 3;
    public int Seed { get; set; }
    public string Heading { get; set; } = DutyRosterText.DefaultHeading;
    public DutyRosterFooter Footer { get; set; } = new();
    public HashSet<DateOnly> ExemptDays { get; } = new();

    /// <summary>Кого прибрали з графіка - наступний графік з тих самих папок їх не бере.</summary>
    public HashSet<int> ExcludedRecipientIds { get; } = new();

    public bool UseEventExemptions { get; set; } = true;
    public bool CarryOverBalance { get; set; } = true;

    /// <summary>Лише «н»; типово в клітинці номер зміни римськими.</summary>
    public bool DutyMarkOnly { get; set; }

    /// <summary>
    /// Субота й неділя зафарбовані зеленим лише як позначка вихідного - наряди на них ставляться.
    /// День без нарядів - це звільнений день (<see cref="ExemptDays"/>, клік по даті).
    /// </summary>
    public bool WeekendsGreen { get; set; } = true;

    /// <summary>
    /// Зміни по черзі (після ІІІ - І, після І - ІІІ, кожному порівну). Вимкнено в графіках, що закінчились до
    /// черги змін (v33): там зміна - порядок у списку дня, щоб графік друкувався так, як його друкували.
    /// </summary>
    public bool ShiftRotation { get; set; } = true;

    public List<DutyRosterMemberData> Members { get; } = new();

    public IEnumerable<DateOnly> Days => DutyRosterEngine.Days(DateFrom, DateTo);

    public bool IsGreenWeekend(DateOnly day) => WeekendsGreen && DutyRosterPlanner.IsWeekend(day);

    public bool IsDuty(int memberIndex, DateOnly day) => Members[memberIndex].Duties.ContainsKey(day);

    public bool IsCanteen(int memberIndex, DateOnly day) => Members[memberIndex].Canteen.Contains(day);

    public bool IsExempt(int memberIndex, DateOnly day)
        => Members[memberIndex].ExemptWholePeriod
           || ExemptDays.Contains(day)
           || Members[memberIndex].Exemptions.ContainsKey(day);
}
