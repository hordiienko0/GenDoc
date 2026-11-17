using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using GenDoc.Services.Duty;

namespace GenDoc.ViewModels.Duty;

public enum DutyBrush
{
    Duty,

    /// <summary>Наряд із номером зміни, проставленим вручну (пензлі «І», «ІІ», «ІІІ»).</summary>
    Shift,
    Canteen,
    Exempt,
    Erase
}

/// <summary>Клітинка «людина × день»: «н», жовта «к» (столова), зелене, «×» або порожньо.</summary>
public partial class DutyCellViewModel : ObservableObject
{
    public DutyCellViewModel(DutyRowViewModel row, DateOnly date)
    {
        Row = row;
        Date = date;
    }

    public DutyRowViewModel Row { get; }
    public DateOnly Date { get; }

    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private bool _isDuty;
    [ObservableProperty] private bool _isAuto;
    [ObservableProperty] private bool _isExempt;
    [ObservableProperty] private bool _isWeekendGreen;
    [ObservableProperty] private bool _isCanteen;
    [ObservableProperty] private bool _isBlocked;
    [ObservableProperty] private bool _hasWarning;
    [ObservableProperty] private string? _hint;
}

public partial class DutyRowViewModel : ObservableObject
{
    public DutyRowViewModel(DutyRosterMemberData member)
    {
        Member = member;
    }

    public DutyRosterMemberData Member { get; }
    public string DisplayName => Member.DisplayName;
    public ObservableCollection<DutyCellViewModel> Cells { get; } = new();

    [ObservableProperty] private int _number;
    [ObservableProperty] private bool _isExemptWholePeriod;
    [ObservableProperty] private int _total;

    public string ExemptMenuHeader => IsExemptWholePeriod ? "Повернути в графік" : "Звільнити на весь період";

    partial void OnIsExemptWholePeriodChanged(bool value) => OnPropertyChanged(nameof(ExemptMenuHeader));
}

public partial class DutyDayViewModel : ObservableObject
{
    private static readonly CultureInfo Ukrainian = CultureInfo.GetCultureInfo("uk-UA");

    /// <summary>ICU в .NET 8 дає однолітерні «П», «С» - неоднозначно (понеділок/п'ятниця), тож назви задано явно.</summary>
    private static readonly string[] WeekDays = { "нд", "пн", "вт", "ср", "чт", "пт", "сб" };

    public DutyDayViewModel(DateOnly date)
    {
        Date = date;
    }

    public DateOnly Date { get; }
    public string DayNumber => Date.Day.ToString(CultureInfo.InvariantCulture);
    public string WeekDay => WeekDays[(int)Date.DayOfWeek];
    public bool IsWeekend => Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    public string Hint => Date.ToString("dddd, dd.MM.yyyy", Ukrainian) + " - клік: звільнити всіх у цей день";

    [ObservableProperty] private bool _isExempt;

    /// <summary>Субота чи неділя з галочкою «Вихідні - зеленим»: лише колір, наряди ставляться.</summary>
    [ObservableProperty] private bool _isWeekendGreen;
    [ObservableProperty] private int _count;
    [ObservableProperty] private bool _isShort;
}

/// <summary>Людина в панелі «Хто чергує першого дня»: відмічені стануть у наряд 1-го числа.</summary>
public partial class DutyFirstDayOption : ObservableObject
{
    private readonly Action _changed;

    public DutyFirstDayOption(DutyRosterMemberData member, string? unavailable, bool isPicked, Action changed)
    {
        Member = member;
        Unavailable = unavailable;
        _isPicked = isPicked;
        _canPick = unavailable is null;
        _changed = changed;
    }

    public DutyRosterMemberData Member { get; }
    public string DisplayName => Member.DisplayName;

    /// <summary>Чому людину цього дня не можна поставити (звільнений, столова); null - можна.</summary>
    public string? Unavailable { get; }

    [ObservableProperty] private bool _isPicked;
    [ObservableProperty] private bool _canPick;

    partial void OnIsPickedChanged(bool value) => _changed();
}

/// <summary>Пензель «І», «ІІ», «ІІІ»: наряд на цій зміні, проставлений вручну.</summary>
public partial class DutyShiftBrushViewModel : ObservableObject
{
    private readonly Action<DutyShiftBrushViewModel> _selected;

    public DutyShiftBrushViewModel(int shift, Action<DutyShiftBrushViewModel> selected)
    {
        Shift = shift;
        _selected = selected;
    }

    public int Shift { get; }
    public string Label => DutyRosterEngine.RomanShift(Shift);
    public string Hint => $"Наряд на {Label} зміні, вручну. Решта в цей день займає вільні зміни за порядком у списку. Повторний клік знімає.";

    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value) _selected(this);
    }
}

public partial class DutyShiftEditViewModel : ObservableObject
{
    private readonly Action _changed;

    public DutyShiftEditViewModel(int number, DutyShiftTimes times, Action changed)
    {
        Label = $"{DutyRosterEngine.RomanShift(number)} зміна";
        Times = times;
        _day = times.Day;
        _night = times.Night;
        _changed = changed;
    }

    public string Label { get; }
    public DutyShiftTimes Times { get; }

    [ObservableProperty] private string _day;
    [ObservableProperty] private string _night;

    partial void OnDayChanged(string value)
    {
        Times.Day = value;
        _changed();
    }

    partial void OnNightChanged(string value)
    {
        Times.Night = value;
        _changed();
    }
}
