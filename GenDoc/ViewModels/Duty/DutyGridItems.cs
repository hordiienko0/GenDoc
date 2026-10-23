using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using GenDoc.Services.Duty;

namespace GenDoc.ViewModels.Duty;

public enum DutyBrush
{
    Duty,
    Exempt,
    Erase
}

/// <summary>Клітинка «людина × день»: «н», зелене, «×» або порожньо.</summary>
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
    [ObservableProperty] private int _count;
    [ObservableProperty] private bool _isShort;
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
