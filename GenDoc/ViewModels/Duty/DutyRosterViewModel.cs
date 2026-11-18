using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Duty;
using Microsoft.Win32;

namespace GenDoc.ViewModels.Duty;

/// <summary>
/// Вкладка «Графік чергувань»: період, люди, пензлі «н» / звільнений / гумка,
/// живий справедливий розподіл решти нарядів і експорт листа як на паперовому графіку.
/// Кожна правка одразу зберігається.
/// </summary>
public partial class DutyRosterViewModel : ObservableObject
{
    private const string Caption = "Графік чергувань";
    private const int ListedWarnings = 6;

    private readonly IDutyRosterService _service;
    private readonly ActiveIntakeState _activeIntake;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly HashSet<DutyCellViewModel> _painted = new();
    private readonly DutyRosterHistory _history = new();

    private DutyRosterData? _roster;
    private DutyRosterData? _strokeBefore;
    private DutyCarryOver? _carryOver;
    private PaintAction? _paint;
    private int _brushShift = 1;
    private int _paintShift;
    private DutyCellViewModel? _lastPainted;
    private bool _loading;
    private bool _suppressOpen;
    private bool _initialized;

    private enum PaintAction
    {
        SetDuty,
        SetShift,
        ClearDuty,
        SetCanteen,
        ClearCanteen,
        SetExempt,
        ClearExempt,
        Erase
    }

    public DutyRosterViewModel(IDutyRosterService service, ActiveIntakeState activeIntake)
    {
        _service = service;
        _activeIntake = activeIntake;
    }

    public ObservableCollection<DutyRosterListItem> Rosters { get; } = new();
    /// <summary>Дерево папок нового графіка: чиї люди підуть у графік.</summary>
    public ObservableCollection<DutyFolderViewModel> Folders { get; } = new();
    public ObservableCollection<DutyDayViewModel> Days { get; } = new();
    public ObservableCollection<DutyRowViewModel> Rows { get; } = new();
    public ObservableCollection<DutyShiftEditViewModel> Shifts { get; } = new();

    /// <summary>Пензлі «І», «ІІ», «ІІІ» - стільки, скільки людей на добу.</summary>
    public ObservableCollection<DutyShiftBrushViewModel> ShiftBrushes { get; } = new();
    public ObservableCollection<DutyPersonOption> PeopleToAdd { get; } = new();
    public IReadOnlyList<int> PeoplePerDayOptions { get; } = new[] { 1, 2, 3, 4, 5, 6 };

    /// <summary>Панель «Хто чергує першого дня»: сама відкривається після створення графіка.</summary>
    public ObservableCollection<DutyFirstDayOption> FirstDayPeople { get; } = new();
    [ObservableProperty] private bool _isFirstDayOpen;
    [ObservableProperty] private string _firstDayCaption = string.Empty;
    [ObservableProperty] private string _firstDayStatus = string.Empty;

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(UndoCommand))] private bool _canUndo;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(RedoCommand))] private bool _canRedo;

    [ObservableProperty] private DutyRosterListItem? _selectedRoster;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowEmptyHint), nameof(ShowGridArea), nameof(ShowGrid), nameof(ShowSettings))] private bool _hasRoster;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowEmptyHint), nameof(ShowGridArea))] private bool _isCreating;
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty] private DateTime? _newDateFrom;
    [ObservableProperty] private DateTime? _newDateTo;
    [ObservableProperty] private string _newPeopleSummary = string.Empty;
    [ObservableProperty] private bool _newUseEventExemptions = true;
    [ObservableProperty] private bool _newWeekendsGreen = true;
    [ObservableProperty] private string? _createError;

    [ObservableProperty] private DateTime? _dateFrom;
    [ObservableProperty] private DateTime? _dateTo;
    [ObservableProperty] private string? _periodError;
    [ObservableProperty] private int _peoplePerDay = 3;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsDutyBrush), nameof(IsCanteenBrush), nameof(IsExemptBrush), nameof(IsEraseBrush))]
    private DutyBrush _brush = DutyBrush.Duty;
    [ObservableProperty] private bool _useEventExemptions;
    [ObservableProperty] private bool _carryOverBalance;

    /// <summary>Лише «н» замість номерів змін (І, ІІ, ІІІ за порядком у списку дня).</summary>
    [ObservableProperty] private bool _dutyMarkOnly;

    /// <summary>Субота й неділя зафарбовані зеленим як вихідні; наряди на них ставляться.</summary>
    [ObservableProperty] private bool _weekendsGreen;
    [ObservableProperty] private string _heading = string.Empty;
    [ObservableProperty] private string _changeover = string.Empty;
    [ObservableProperty] private string _cleaning = string.Empty;
    [ObservableProperty] private string _titlePreview = string.Empty;
    [ObservableProperty] private string _rosterCaption = string.Empty;

    [ObservableProperty] private string _statsText = string.Empty;
    [ObservableProperty] private string _warningsText = string.Empty;
    [ObservableProperty] private bool _hasWarnings;
    [ObservableProperty] private string _carryOverText = string.Empty;
    [ObservableProperty] private string _saveStatus = string.Empty;

    [ObservableProperty] private DutyPersonOption? _personToAdd;
    [ObservableProperty] private string _manualName = string.Empty;

    /// <summary>Вкладка «Склад, шапка і підвал листа»: сама відкривається, коли в графіку ще нікого немає.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowGrid), nameof(ShowSettings))] private bool _isSettingsOpen;

    public bool ShowEmptyHint => !HasRoster && !IsCreating;
    public bool ShowGridArea => HasRoster || !IsCreating;
    public bool ShowGrid => HasRoster && !IsSettingsOpen;
    public bool ShowSettings => HasRoster && IsSettingsOpen;

    public bool IsDutyBrush
    {
        get => Brush == DutyBrush.Duty;
        set { if (value) Brush = DutyBrush.Duty; }
    }

    partial void OnBrushChanged(DutyBrush value) => SyncShiftBrushes();

    private void SelectShiftBrush(DutyShiftBrushViewModel brush)
    {
        _brushShift = brush.Shift;
        Brush = DutyBrush.Shift;
        SyncShiftBrushes();
    }

    private void SyncShiftBrushes()
    {
        foreach (var brush in ShiftBrushes)
            brush.IsSelected = Brush == DutyBrush.Shift && brush.Shift == _brushShift;
    }

    public bool IsCanteenBrush
    {
        get => Brush == DutyBrush.Canteen;
        set { if (value) Brush = DutyBrush.Canteen; }
    }

    public bool IsExemptBrush
    {
        get => Brush == DutyBrush.Exempt;
        set { if (value) Brush = DutyBrush.Exempt; }
    }

    public bool IsEraseBrush
    {
        get => Brush == DutyBrush.Erase;
        set { if (value) Brush = DutyBrush.Erase; }
    }

    /// <summary>Поточний графік (для тестів і експорту).</summary>
    public DutyRosterData? Roster => _roster;

    [RelayCommand]
    private async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        await ReloadListAsync(null);
        if (Rosters.Count == 0) await StartCreateAsync();
    }

    private async Task ReloadListAsync(int? selectId)
    {
        var list = await _service.GetListAsync();

        _suppressOpen = true;
        try
        {
            Rosters.Clear();
            foreach (var item in list) Rosters.Add(item);
            SelectedRoster = Rosters.FirstOrDefault(r => r.Id == selectId) ?? Rosters.FirstOrDefault();
        }
        finally
        {
            _suppressOpen = false;
        }

        await OpenRosterAsync(SelectedRoster?.Id);
    }

    /// <summary>Підписи в списку змінюються з періодом - оновлюємо список, не перевідкриваючи графік.</summary>
    private async Task RefreshListLabelsAsync()
    {
        if (_roster is null) return;
        var list = await _service.GetListAsync();

        _suppressOpen = true;
        try
        {
            Rosters.Clear();
            foreach (var item in list) Rosters.Add(item);
            SelectedRoster = Rosters.FirstOrDefault(r => r.Id == _roster.Id);
        }
        finally
        {
            _suppressOpen = false;
        }
    }

    partial void OnSelectedRosterChanged(DutyRosterListItem? value)
    {
        if (_suppressOpen) return;
        Track(() => OpenRosterAsync(value?.Id));
    }

    public async Task OpenRosterAsync(int? id)
    {
        _history.Clear();
        SyncHistory();
        IsFirstDayOpen = false;
        if (id is not int rosterId)
        {
            _roster = null;
            Days.Clear();
            Rows.Clear();
            HasRoster = false;
            return;
        }

        IsBusy = true;
        try
        {
            var roster = await _service.LoadAsync(rosterId);
            if (roster is null) return;

            _roster = roster;
            _carryOver = await _service.GetCarryOverAsync(roster);
            LoadEditorFields();
            var reordered = DutyRosterPlanner.MoveExemptToBottom(roster);
            BuildGrid();
            if (DutyRosterPlanner.HasEmptyWeekends(roster))
            {
                await ReplanAndSaveAsync();
            }
            else
            {
                ApplyToGrid(DutyRosterPlanner.Check(roster));
                if (reordered) await SaveAsync();
            }
            await LoadPeopleToAddAsync();

            IsSettingsOpen = roster.Members.Count == 0;
            IsCreating = false;
            HasRoster = true;
            SaveStatus = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task StartCreateAsync()
    {
        CreateError = null;
        var start = Rosters.Count > 0
            ? Rosters.Max(r => r.DateTo).AddDays(1)
            : DateOnly.FromDateTime(DateTime.Today);
        NewDateFrom = start.ToDateTime(TimeOnly.MinValue);
        NewDateTo = start.AddMonths(1).AddDays(-1).ToDateTime(TimeOnly.MinValue);

        var folders = await _service.GetFoldersAsync();
        BuildFolders(folders, _roster is { FolderIds.Count: > 0 } ? _roster.FolderIds : DefaultFolderIds(folders));
        var prefs = await _service.GetUserPrefsAsync();
        NewUseEventExemptions = prefs.UseEventExemptions;
        NewWeekendsGreen = prefs.WeekendsGreen;
        IsCreating = true;
    }

    [RelayCommand]
    private void CancelCreate()
    {
        IsCreating = false;
        CreateError = null;
    }

    /// <summary>Без відкритого графіка - активний набір, а як його немає, то останній створений.</summary>
    private IReadOnlyList<int> DefaultFolderIds(IReadOnlyList<DutyFolderOption> folders)
    {
        var roots = folders.Where(f => f.IsIntakeRoot).ToList();
        var root = roots.FirstOrDefault(f => f.Id == _activeIntake.Current?.RootOrgNodeId)
                   ?? roots.MaxBy(f => f.IntakeId);
        return root is null ? Array.Empty<int>() : new[] { root.Id };
    }

    /// <summary>Дерево з відміченими папками; гілки до відмічених розгорнуті, щоб їх було видно одразу.</summary>
    private void BuildFolders(IReadOnlyList<DutyFolderOption> options, IReadOnlyCollection<int> checkedIds)
    {
        Folders.Clear();
        var byId = new Dictionary<int, DutyFolderViewModel>();
        foreach (var option in options)
        {
            var parent = option.ParentId is int parentId ? byId.GetValueOrDefault(parentId) : null;
            var folder = new DutyFolderViewModel(option, parent, UpdatePeopleSummary);
            byId[option.Id] = folder;
            (parent?.Children ?? Folders).Add(folder);
        }

        foreach (var folder in Folders) folder.IsExpanded = true;
        foreach (var id in checkedIds)
        {
            if (!byId.TryGetValue(id, out var folder)) continue;
            folder.IsChecked = true;
            for (var up = folder.Parent; up is not null; up = up.Parent) up.IsExpanded = true;
        }
        UpdatePeopleSummary();
    }

    private IEnumerable<DutyFolderViewModel> AllFolders() => Folders.SelectMany(f => f.SelfAndDescendants());

    /// <summary>Відмічені папки без вкладених у вже відмічені.</summary>
    public IReadOnlyList<int> PickedFolderIds => AllFolders().Where(f => f.IsPicked).Select(f => f.Id).ToList();

    private void UpdatePeopleSummary()
    {
        var picked = AllFolders().Where(f => f.IsPicked).ToList();
        if (picked.Count == 0)
        {
            NewPeopleSummary = "Папок не відмічено - графік буде порожній, людей впишете вручну на вкладці «Склад».";
            return;
        }

        var people = picked.SelectMany(f => f.Option.PeopleIds).Distinct().Count();
        NewPeopleSummary = $"Разом {people} {PluralHelper.Pluralize(people, "особа", "особи", "осіб")} "
                           + $"з {picked.Count} {PluralHelper.Pluralize(picked.Count, "папки", "папок", "папок")}, вкладені папки входять.";
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (NewDateFrom is not DateTime from || NewDateTo is not DateTime to)
        {
            CreateError = "Вкажіть дати «з» і «по».";
            return;
        }

        IsBusy = true;
        try
        {
            var roster = await _service.CreateAsync(new DutyRosterCreateRequest(
                DateOnly.FromDateTime(from), DateOnly.FromDateTime(to),
                PickedFolderIds, NewUseEventExemptions, NewWeekendsGreen));
            CreateError = null;
            IsCreating = false;
            await ReloadListAsync(roster.Id);
            OpenFirstDay();
        }
        catch (ArgumentException ex)
        {
            CreateError = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteRosterAsync()
    {
        if (_roster is null) return;
        var label = DutyRosterText.PeriodLabel(_roster.DateFrom, _roster.DateTo);
        if (MessageBox.Show($"Видалити графік {label}?", Caption, MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes) return;

        await _service.DeleteAsync(_roster.Id);
        _roster = null;
        await ReloadListAsync(null);
        if (Rosters.Count == 0) await StartCreateAsync();
    }

    private void LoadEditorFields()
    {
        if (_roster is null) return;
        _loading = true;
        try
        {
            DateFrom = _roster.DateFrom.ToDateTime(TimeOnly.MinValue);
            DateTo = _roster.DateTo.ToDateTime(TimeOnly.MinValue);
            PeriodError = null;
            PeoplePerDay = _roster.PeoplePerDay;
            UseEventExemptions = _roster.UseEventExemptions;
            CarryOverBalance = _roster.CarryOverBalance;
            DutyMarkOnly = _roster.DutyMarkOnly;
            WeekendsGreen = _roster.WeekendsGreen;
            Heading = _roster.Heading;
            Changeover = _roster.Footer.Changeover;
            Cleaning = _roster.Footer.Cleaning;
            RebuildShifts();
            UpdateCaptions();
        }
        finally
        {
            _loading = false;
        }
    }

    private void RebuildShifts()
    {
        if (_roster is null) return;
        _roster.Footer.FitShifts(_roster.PeoplePerDay);
        Shifts.Clear();
        for (var i = 0; i < _roster.Footer.Shifts.Count; i++)
            Shifts.Add(new DutyShiftEditViewModel(i + 1, _roster.Footer.Shifts[i], () => Track(SaveSettingsAsync)));

        ShiftBrushes.Clear();
        for (var shift = 1; shift <= _roster.PeoplePerDay; shift++)
            ShiftBrushes.Add(new DutyShiftBrushViewModel(shift, SelectShiftBrush));
        if (Brush == DutyBrush.Shift && _brushShift > _roster.PeoplePerDay) Brush = DutyBrush.Duty;
        SyncShiftBrushes();
    }

    private void UpdateCaptions()
    {
        if (_roster is null) return;
        TitlePreview = string.Join("\n", DutyRosterText.TitleLines(_roster.Heading, _roster.DateFrom, _roster.DateTo));
        RosterCaption = _roster.FoldersLabel is null
            ? DutyRosterText.PeriodLabel(_roster.DateFrom, _roster.DateTo)
            : $"{DutyRosterText.PeriodLabel(_roster.DateFrom, _roster.DateTo)} · {_roster.FoldersLabel}";
        CarryOverText = !_roster.CarryOverBalance
            ? "Баланс з минулого графіка вимкнено - черга починається з нуля."
            : _carryOver is null
                ? "Попереднього графіка для цього складу немає - черга починається з нуля."
                : $"Черга продовжує графік {_carryOver.FromLabel}: хто не встиг відстояти, стоїть першим.";
    }

    private void BuildGrid()
    {
        Days.Clear();
        Rows.Clear();
        if (_roster is null) return;

        foreach (var day in _roster.Days) Days.Add(new DutyDayViewModel(day));
        foreach (var member in _roster.Members)
        {
            var row = new DutyRowViewModel(member);
            foreach (var day in _roster.Days) row.Cells.Add(new DutyCellViewModel(row, day));
            Rows.Add(row);
        }

        // Склад чи перший день змінились - відкрита панель першого дня показує поточних людей.
        if (IsFirstDayOpen) OpenFirstDay();
    }

    /// <summary>
    /// Під час малювання мишею зведення (статистика, панель попереджень) не оновлюється:
    /// інакше панель з'являється чи міняє висоту й сітка зсувається під курсором.
    /// </summary>
    private void ApplyToGrid(IReadOnlyList<DutyWarning> warnings, bool refreshSummary = true)
    {
        if (_roster is null) return;

        var flagged = warnings
            .Where(w => w.MemberIndex is not null && w.Kind != DutyWarningKind.ShortDay)
            .Select(w => (w.MemberIndex!.Value, w.Date))
            .ToHashSet();
        var shifts = DutyRosterPlanner.ShiftsByDay(_roster);
        var clashes = Days.ToDictionary(d => d.Date, d => DutyRosterPlanner.ShiftClashes(_roster, d.Date));

        foreach (var day in Days)
        {
            day.IsExempt = _roster.ExemptDays.Contains(day.Date);
            day.IsWeekendGreen = _roster.IsGreenWeekend(day.Date);
            day.Count = shifts[day.Date].Count;
            day.IsShort = !day.IsExempt && day.Count < _roster.PeoplePerDay;
        }

        var counts = new List<int>();
        for (var i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i];
            var member = row.Member;
            row.Number = i + 1;
            row.IsExemptWholePeriod = member.ExemptWholePeriod;

            var total = 0;
            foreach (var cell in row.Cells)
            {
                var isDuty = member.Duties.TryGetValue(cell.Date, out var source);
                var isEvent = member.Exemptions.TryGetValue(cell.Date, out var exemptSource) && exemptSource == DutyCellSource.Event;
                var isCanteen = !isDuty && member.Canteen.Contains(cell.Date);
                cell.IsDuty = isDuty;
                cell.IsAuto = isDuty && source == DutyCellSource.Auto;
                cell.IsCanteen = isCanteen;
                cell.IsExempt = _roster.IsExempt(i, cell.Date);
                cell.IsWeekendGreen = _roster.IsGreenWeekend(cell.Date);
                cell.IsBlocked = !isDuty && !isCanteen && member.Blocked.Contains(cell.Date);
                cell.HasWarning = flagged.Contains((i, cell.Date)) || (isCanteen && cell.IsExempt) || clashes[cell.Date].Contains(i);

                var shiftsOfDay = shifts[cell.Date];
                cell.Text = isDuty ? DutyRosterPlanner.DutyText(_roster, shiftsOfDay, i)
                    : isCanteen ? DutyRosterXlsxExporter.CanteenMark
                    : cell.IsBlocked ? "×" : string.Empty;
                cell.Hint = isDuty
                    ? (cell.IsAuto ? "Поставила програма" : "Поставлено вручну")
                      + (shiftsOfDay.TryGetValue(i, out var shift)
                          ? $", {DutyRosterEngine.RomanShift(shift)} зміна ({(member.Shifts.ContainsKey(cell.Date) ? "проставлена вручну" : _roster.ShiftRotation ? "по черзі змін" : "порядок у списку дня")})"
                          : string.Empty)
                    : isCanteen ? "Столова (жовта «к», лише вручну)" + (cell.IsExempt ? " - але людина в цей день звільнена" : string.Empty)
                    : isEvent ? "Звільнений: відпустка, шпиталь або відрядження"
                    : cell.IsExempt ? "Звільнений від наряду"
                    : cell.IsBlocked ? "Не ставити в цей день (у друк не йде)"
                    : null;
                if (isDuty) total++;
            }

            row.Total = total;
            if (!member.ExemptWholePeriod) counts.Add(total);
        }

        if (!refreshSummary) return;

        var shortDays = Days.Count(d => d.IsShort);
        StatsText = counts.Count == 0
            ? $"Людей у черзі немає · варіант №{_roster.Seed}"
            : $"Нарядів на людину: {counts.Min()} - {counts.Max()} · неповних днів: {shortDays} · варіант №{_roster.Seed}";

        WarningsText = DescribeWarnings(warnings);
        HasWarnings = WarningsText.Length > 0;
    }

    private string DescribeWarnings(IReadOnlyList<DutyWarning> warnings)
    {
        if (_roster is null) return string.Empty;
        var lines = new List<string>();

        var shortDays = warnings.Where(w => w.Kind == DutyWarningKind.ShortDay).ToList();
        if (shortDays.Count > 0)
            lines.Add("Неповні дні (бракує людей): " + Listed(shortDays.Select(w => $"{w.Date:dd.MM} ({w.Count} з {_roster.PeoplePerDay})")));

        var onExempt = warnings.Where(w => w.Kind == DutyWarningKind.DutyOnExemptDay && w.MemberIndex is not null).ToList();
        if (onExempt.Count > 0)
            lines.Add("Наряд у звільнений день: " + Listed(onExempt.Select(w => $"{Name(w)} {w.Date:dd.MM}")));

        var consecutive = warnings.Where(w => w.Kind == DutyWarningKind.ConsecutiveDays && w.MemberIndex is not null).ToList();
        if (consecutive.Count > 0)
            lines.Add("Два дні поспіль: " + Listed(consecutive.Select(w => $"{Name(w)} {w.Date.AddDays(-1):dd}-{w.Date:dd.MM}")));

        var clashDays = _roster.Days.Where(d => DutyRosterPlanner.ShiftClashes(_roster, d).Count > 0).ToList();
        if (clashDays.Count > 0)
            lines.Add("Одна зміна вручну двом людям: " + Listed(clashDays.Select(d => $"{d:dd.MM}")));

        var days = _roster.DateTo.DayNumber - _roster.DateFrom.DayNumber + 1;
        var scale = DutyRosterXlsxExporter.PrintScale(_roster);
        if (scale < DutyRosterXlsxExporter.ReadableScale)
            lines.Add($"Аркуш A4: {days} {PluralHelper.Pluralize(days, "день", "дні", "днів")}, {_roster.Members.Count} {PluralHelper.Pluralize(_roster.Members.Count, "особа", "особи", "осіб")} - друк дрібний (≈ {scale:P0}). Краще до 40 днів або два графіки.");

        return string.Join("\n", lines);

        string Name(DutyWarning w) => _roster.Members[w.MemberIndex!.Value].DisplayName;

        static string Listed(IEnumerable<string> items)
        {
            var all = items.ToList();
            var shown = string.Join(", ", all.Take(ListedWarnings));
            return all.Count > ListedWarnings ? $"{shown} і ще {all.Count - ListedWarnings}" : shown;
        }
    }

    private async Task ReplanAndSaveAsync()
    {
        if (_roster is null) return;
        var result = DutyRosterPlanner.Replan(_roster, _carryOver?.State);
        ApplyToGrid(result.Warnings);
        await SaveAsync();
    }

    /// <summary>Зміна налаштування: зберегти графік і запам'ятати налаштування за поточним курсовим.</summary>
    private async Task SaveSettingsAsync()
    {
        if (_roster is null || _loading) return;
        await SaveAsync();
        await _service.RememberPrefsAsync(_roster);
    }

    private async Task SaveAsync()
    {
        if (_roster is null || _loading) return;

        await _saveLock.WaitAsync();
        try
        {
            await _service.SaveAsync(_roster);
            SaveStatus = $"Збережено о {DateTime.Now:HH:mm}";
        }
        catch (Exception ex)
        {
            SaveStatus = "Не вдалося зберегти: " + ex.Message;
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>Остання фонова дія (правка поля → перерахунок і збереження); тести чекають на неї.</summary>
    public Task PendingWork { get; private set; } = Task.CompletedTask;

    private void Track(Func<Task> action) => PendingWork = RunAsync(action);

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            SaveStatus = "Помилка: " + ex.Message;
        }
    }

    // ---- Назад / Вперед ----

    /// <summary>Дія з графіком, яку можна скасувати: знімок до неї, а як щось змінилось - крок в історію.</summary>
    private async Task ChangeAsync(Func<DutyRosterData, Task> action)
    {
        if (_roster is null) return;
        var before = _roster.Clone();
        await action(_roster);
        Remember(before);
    }

    private void Remember(DutyRosterData before)
    {
        if (_roster is null) return;
        _history.Record(before, _roster);
        SyncHistory();
    }

    private void SyncHistory()
    {
        CanUndo = _history.CanUndo;
        CanRedo = _history.CanRedo;
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoAsync()
    {
        if (_roster is null || _history.Undo(_roster) is not { } state) return;
        await RestoreAsync(state);
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private async Task RedoAsync()
    {
        if (_roster is null || _history.Redo(_roster) is not { } state) return;
        await RestoreAsync(state);
    }

    /// <summary>
    /// Повертає графік до знімка і зберігає. Шапка й підвал - поточні: це текст, а не дія з графіком.
    /// Лише години змін, які зникли, коли зменшили «На добу», беруться зі знімка.
    /// </summary>
    private async Task RestoreAsync(DutyRosterData state)
    {
        if (_roster is null) return;
        var current = _roster;
        var footer = current.Footer;
        for (var i = footer.Shifts.Count; i < state.Footer.Shifts.Count; i++) footer.Shifts.Add(state.Footer.Shifts[i]);
        state.Heading = current.Heading;
        state.Footer = footer;
        var periodChanged = state.DateFrom != current.DateFrom || state.DateTo != current.DateTo;

        _roster = state;
        SyncHistory();
        _carryOver = await _service.GetCarryOverAsync(state);
        LoadEditorFields();
        BuildGrid();
        ApplyToGrid(DutyRosterPlanner.Check(state));
        await SaveAsync();
        await _service.RememberPrefsAsync(state);
        await LoadPeopleToAddAsync();
        if (periodChanged) await RefreshListLabelsAsync();
    }

    // ---- Хто чергує першого дня ----

    /// <summary>
    /// Панель з усіма, хто в черзі; відмічені ті, кого вже поставлено на перший день.
    /// Звільнених цього дня і тих, хто в столовій, вибрати не можна, звільнених на весь період немає зовсім.
    /// </summary>
    [RelayCommand]
    private void OpenFirstDay()
    {
        FirstDayPeople.Clear();
        if (_roster is null || _roster.Members.Count == 0)
        {
            IsFirstDayOpen = false;
            return;
        }

        var day = _roster.DateFrom;
        for (var i = 0; i < _roster.Members.Count; i++)
        {
            var member = _roster.Members[i];
            if (member.ExemptWholePeriod) continue;
            var unavailable = _roster.IsExempt(i, day) ? "Звільнений цього дня"
                : _roster.IsCanteen(i, day) ? "Цього дня в столовій"
                : null;
            FirstDayPeople.Add(new DutyFirstDayOption(member, unavailable, unavailable is null && _roster.IsDuty(i, day), SyncFirstDay));
        }

        var weekDay = day.ToString("dddd", System.Globalization.CultureInfo.GetCultureInfo("uk-UA"));
        FirstDayCaption = $"Хто чергує першого дня - {day:dd.MM} ({weekDay})?";
        SyncFirstDay();
        IsFirstDayOpen = true;
    }

    /// <summary>Вибрати можна не більше, ніж людей на добу; лічильник під списком.</summary>
    private void SyncFirstDay()
    {
        var need = _roster?.PeoplePerDay ?? 0;
        var picked = FirstDayPeople.Count(p => p.IsPicked);
        foreach (var option in FirstDayPeople)
            option.CanPick = option.Unavailable is null && (option.IsPicked || picked < need);

        FirstDayStatus = picked == 0
            ? $"Відмітьте до {need} {PluralHelper.Pluralize(need, "особи", "осіб", "осіб")}. Нікого не відмітите - усіх поставить програма."
            : picked < need
                ? $"Вибрано {picked} з {need} - решту поставить програма."
                : $"Вибрано {picked} з {need}.";
    }

    /// <summary>
    /// Відмічені стають на перший день ручним «н» (так «Рандом» їх не переставить), з решти перший день знімається.
    /// Далі програма розподіляє як завжди: перший наряд зараховується людям у чергу.
    /// </summary>
    [RelayCommand]
    private async Task ApplyFirstDayAsync()
    {
        if (_roster is null) return;
        IsFirstDayOpen = false;
        var options = FirstDayPeople.Where(p => p.Unavailable is null).ToList();

        await ChangeAsync(async roster =>
        {
            var day = roster.DateFrom;
            foreach (var option in options)
            {
                var member = option.Member;
                if (option.IsPicked)
                {
                    member.Blocked.Remove(day);
                    member.Duties[day] = DutyCellSource.Manual;
                }
                else
                {
                    member.Duties.Remove(day);
                    member.Shifts.Remove(day);
                }
            }
            await ReplanAndSaveAsync();
        });
    }

    [RelayCommand]
    private void CloseFirstDay() => IsFirstDayOpen = false;

    // ---- Малювання пензлем ----

    public void BeginPaint(DutyCellViewModel cell)
    {
        if (_roster is null) return;
        var member = cell.Row.Member;

        _paint = Brush switch
        {
            DutyBrush.Duty => member.Duties.ContainsKey(cell.Date) ? PaintAction.ClearDuty : PaintAction.SetDuty,
            DutyBrush.Shift => member.Duties.ContainsKey(cell.Date) && member.Shifts.TryGetValue(cell.Date, out var shift) && shift == _brushShift
                ? PaintAction.ClearDuty
                : PaintAction.SetShift,
            DutyBrush.Canteen => member.Canteen.Contains(cell.Date) ? PaintAction.ClearCanteen : PaintAction.SetCanteen,
            DutyBrush.Exempt => member.Exemptions.ContainsKey(cell.Date) ? PaintAction.ClearExempt : PaintAction.SetExempt,
            _ => PaintAction.Erase
        };
        _paintShift = _brushShift;
        _painted.Clear();
        _lastPainted = null;
        _strokeBefore = _roster.Clone();
        ContinuePaint(cell);
    }

    /// <summary>
    /// Мишу тягнуть швидше, ніж WPF надсилає MouseMove: між двома подіями клітинки пропускались.
    /// Тож фарбуються всі клітинки на прямій від попередньої до поточної.
    /// </summary>
    public void ContinuePaint(DutyCellViewModel cell)
    {
        if (_roster is null || _paint is not PaintAction action) return;

        var changed = false;
        foreach (var step in CellsBetween(_lastPainted, cell))
        {
            if (_painted.Add(step))
            {
                Paint(action, step, _paintShift);
                changed = true;
            }
        }
        _lastPainted = cell;

        if (changed) ApplyToGrid(DutyRosterPlanner.Check(_roster), refreshSummary: false);
    }

    private IEnumerable<DutyCellViewModel> CellsBetween(DutyCellViewModel? from, DutyCellViewModel to)
    {
        var toRow = Rows.IndexOf(to.Row);
        var toDay = to.Row.Cells.IndexOf(to);
        if (from is null || toRow < 0 || toDay < 0)
        {
            yield return to;
            yield break;
        }

        var fromRow = Rows.IndexOf(from.Row);
        var fromDay = from.Row.Cells.IndexOf(from);
        if (fromRow < 0 || fromDay < 0)
        {
            yield return to;
            yield break;
        }

        var steps = Math.Max(Math.Abs(toRow - fromRow), Math.Abs(toDay - fromDay));
        for (var k = 1; k <= steps; k++)
        {
            var row = fromRow + (int)Math.Round((toRow - fromRow) * (double)k / steps);
            var day = fromDay + (int)Math.Round((toDay - fromDay) * (double)k / steps);
            yield return Rows[row].Cells[day];
        }
        if (steps == 0) yield return to;
    }

    private static void Paint(PaintAction action, DutyCellViewModel cell, int shift)
    {
        var member = cell.Row.Member;
        var day = cell.Date;
        switch (action)
        {
            case PaintAction.SetDuty:
            case PaintAction.SetShift:
                member.Blocked.Remove(day);
                member.Canteen.Remove(day);
                if (member.Exemptions.TryGetValue(day, out var exempt) && exempt == DutyCellSource.Manual)
                    member.Exemptions.Remove(day);
                member.Duties[day] = DutyCellSource.Manual;
                if (action == PaintAction.SetShift) member.Shifts[day] = shift;
                else member.Shifts.Remove(day);
                break;
            case PaintAction.ClearDuty:
                ClearDuty(member, day);
                break;
            case PaintAction.SetCanteen:
                member.Duties.Remove(day);
                member.Blocked.Remove(day);
                if (member.Exemptions.TryGetValue(day, out var exemptForCanteen) && exemptForCanteen == DutyCellSource.Manual)
                    member.Exemptions.Remove(day);
                member.Canteen.Add(day);
                break;
            case PaintAction.ClearCanteen:
                member.Canteen.Remove(day);
                break;
            case PaintAction.SetExempt:
                member.Duties.Remove(day);
                member.Blocked.Remove(day);
                member.Canteen.Remove(day);
                member.Exemptions[day] = DutyCellSource.Manual;
                break;
            case PaintAction.ClearExempt:
                member.Exemptions.Remove(day);
                break;
            case PaintAction.Erase:
                if (member.Canteen.Remove(day))
                    break;
                if (member.Duties.ContainsKey(day))
                    ClearDuty(member, day);
                else
                {
                    member.Exemptions.Remove(day);
                    member.Blocked.Remove(day);
                }
                break;
        }
    }

    /// <summary>Ручне «н» просто знімається; авто «н» стає «×», інакше програма поставить людину назад.</summary>
    private static void ClearDuty(DutyRosterMemberData member, DateOnly day)
    {
        member.Shifts.Remove(day);
        if (!member.Duties.Remove(day, out var source)) return;
        if (source == DutyCellSource.Auto) member.Blocked.Add(day);
    }

    public async Task EndPaintAsync()
    {
        if (_paint is null) return;
        _paint = null;
        _painted.Clear();
        await ReplanAndSaveAsync();
        if (_strokeBefore is { } before) Remember(before);
        _strokeBefore = null;
    }

    [RelayCommand]
    private async Task ToggleRowExemptAsync(DutyRowViewModel? row)
    {
        if (_roster is null || row is null) return;
        await ChangeAsync(async roster =>
        {
            row.Member.ExemptWholePeriod = !row.Member.ExemptWholePeriod;
            if (DutyRosterPlanner.MoveExemptToBottom(roster)) BuildGrid();
            await ReplanAndSaveAsync();
        });
    }

    [RelayCommand]
    private async Task ToggleDayExemptAsync(DutyDayViewModel? day)
    {
        if (_roster is null || day is null) return;
        await ChangeAsync(async roster =>
        {
            if (!roster.ExemptDays.Remove(day.Date)) roster.ExemptDays.Add(day.Date);
            await ReplanAndSaveAsync();
        });
    }

    [RelayCommand]
    private async Task MoveUpAsync(DutyRowViewModel? row) => await MoveAsync(row, -1);

    [RelayCommand]
    private async Task MoveDownAsync(DutyRowViewModel? row) => await MoveAsync(row, +1);

    private async Task MoveAsync(DutyRowViewModel? row, int delta)
    {
        if (_roster is null || row is null) return;
        var index = Rows.IndexOf(row);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Rows.Count) return;

        await ChangeAsync(async roster =>
        {
            Rows.Move(index, target);
            var member = roster.Members[index];
            roster.Members.RemoveAt(index);
            roster.Members.Insert(target, member);
            await ReplanAndSaveAsync();
        });
    }

    [RelayCommand]
    private async Task RemoveMemberAsync(DutyRowViewModel? row)
    {
        if (_roster is null || row is null) return;
        if (MessageBox.Show($"Видалити {row.DisplayName} з графіка?\n\n"
                            + "Рядок зникне з листа - ні в друк, ні в Excel він не піде. Наступні графіки з тих самих папок його теж не братимуть.\n\n"
                            + "Повернути: «← Назад» або «Додати» на вкладці «Склад, шапка і підвал листа».", Caption,
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        await RemoveMemberCoreAsync(row);
    }

    public async Task RemoveMemberCoreAsync(DutyRowViewModel row)
    {
        if (_roster is null) return;
        await ChangeAsync(async roster =>
        {
            if (row.Member.RecipientId is int removedId) roster.ExcludedRecipientIds.Add(removedId);
            roster.Members.Remove(row.Member);
            Rows.Remove(row);
            if (IsFirstDayOpen) OpenFirstDay();
            await ReplanAndSaveAsync();
        });
        await LoadPeopleToAddAsync();
    }

    [RelayCommand]
    private async Task AddPersonAsync()
    {
        if (_roster is null || PersonToAdd is not { } person) return;
        await AddMemberAsync(new DutyRosterMemberData { RecipientId = person.RecipientId, DisplayName = person.DisplayName, AddedByHand = true });
        PersonToAdd = null;
    }

    [RelayCommand]
    private async Task AddManualPersonAsync()
    {
        var name = ManualName.Trim();
        if (_roster is null || name.Length == 0) return;
        await AddMemberAsync(new DutyRosterMemberData { DisplayName = name });
        ManualName = string.Empty;
    }

    private async Task AddMemberAsync(DutyRosterMemberData member)
    {
        if (_roster is null) return;
        await ChangeAsync(async roster =>
        {
            if (member.RecipientId is int addedId) roster.ExcludedRecipientIds.Remove(addedId);
            roster.Members.Add(member);
            DutyRosterPlanner.MoveExemptToBottom(roster);
            if (roster.UseEventExemptions && member.RecipientId is not null)
                await _service.ApplyEventExemptionsAsync(roster);
            BuildGrid();
            await ReplanAndSaveAsync();
        });
        await LoadPeopleToAddAsync();
    }

    private async Task LoadPeopleToAddAsync()
    {
        PeopleToAdd.Clear();
        if (_roster is null) return;
        var inRoster = _roster.Members.Where(m => m.RecipientId is not null).Select(m => m.RecipientId!.Value).ToList();
        foreach (var person in await _service.GetPeopleToAddAsync(_roster.IntakeId, inRoster))
            PeopleToAdd.Add(person);
    }

    [RelayCommand]
    private async Task RandomizeAsync()
    {
        if (_roster is null) return;
        await ChangeAsync(async roster =>
        {
            var seed = roster.Seed;
            while (seed == roster.Seed) seed = Random.Shared.Next(1, 1_000_000);
            roster.Seed = seed;
            await ReplanAndSaveAsync();
        });
    }

    [RelayCommand]
    private async Task FreezeAsync()
    {
        if (_roster is null) return;
        await ChangeAsync(async roster =>
        {
            foreach (var member in roster.Members)
            {
                foreach (var day in member.Duties.Where(d => d.Value == DutyCellSource.Auto).Select(d => d.Key).ToList())
                    member.Duties[day] = DutyCellSource.Manual;
            }
            ApplyToGrid(DutyRosterPlanner.Check(roster));
            await SaveAsync();
        });
    }

    [RelayCommand]
    private async Task ClearManualAsync()
    {
        if (_roster is null) return;
        if (MessageBox.Show("Зняти всі ручні «н» і «×»? Звільнення і жовті «к» (столова) лишаться, решту програма розставить заново.", Caption,
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        await ClearManualCoreAsync();
    }

    public async Task ClearManualCoreAsync()
    {
        if (_roster is null) return;
        await ChangeAsync(async roster =>
        {
            foreach (var member in roster.Members)
            {
                foreach (var day in member.Duties.Where(d => d.Value == DutyCellSource.Manual).Select(d => d.Key).ToList())
                    member.Duties.Remove(day);
                member.Blocked.Clear();
            }
            await ReplanAndSaveAsync();
        });
    }

    [RelayCommand]
    private async Task RefreshEventsAsync()
    {
        if (_roster is null) return;
        await ChangeAsync(async roster =>
        {
            await _service.ApplyEventExemptionsAsync(roster);
            await ReplanAndSaveAsync();
        });
    }

    [RelayCommand]
    private void Export()
    {
        if (_roster is null) return;

        var dialog = new SaveFileDialog
        {
            FileName = $"Графік чергувань {_roster.DateFrom:dd.MM}-{_roster.DateTo:dd.MM.yyyy}.xlsx",
            Filter = "Excel файли (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx"
        };
        if (dialog.ShowDialog() != true || ViewModels.Common.CloudFolderGuard.Blocks(dialog.FileName)) return;

        try
        {
            DutyRosterXlsxExporter.Save(_roster, dialog.FileName);
            _service.LogExport(_roster, dialog.FileName);
        }
        catch (IOException ex)
        {
            MessageBox.Show($"Не вдалося зберегти файл:\n{ex.Message}\n\nМожливо, він зараз відкритий в Excel.", Caption,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show($"Графік збережено:\n{dialog.FileName}\n\nВідкрити файл?", Caption,
                MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
        {
            Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
    }

    /// <summary>
    /// Той самий лист, що й в Excel, - одразу на принтер. Без підключеного принтера у вікні друку обрано
    /// «Microsoft Print to PDF»: Windows спитає, куди зберегти PDF.
    /// </summary>
    [RelayCommand]
    private void Print()
    {
        if (_roster is null) return;

        try
        {
            var printer = DutyRosterPrinter.Print(_roster, $"Графік чергувань {DutyRosterText.PeriodLabel(_roster.DateFrom, _roster.DateTo)}");
            if (printer is not null) _service.LogExport(_roster, printer);
        }
        catch (Exception ex) when (ex is System.Printing.PrintSystemException or InvalidOperationException)
        {
            MessageBox.Show($"Не вдалося надрукувати:\n{ex.Message}", Caption, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---- Поля редактора ----

    partial void OnDateFromChanged(DateTime? value) => OnPeriodEdited();
    partial void OnDateToChanged(DateTime? value) => OnPeriodEdited();

    private void OnPeriodEdited()
    {
        if (_loading || _roster is null) return;
        Track(ApplyPeriodAsync);
    }

    public async Task ApplyPeriodAsync()
    {
        if (_roster is null || DateFrom is not DateTime fromValue || DateTo is not DateTime toValue) return;

        var from = DateOnly.FromDateTime(fromValue);
        var to = DateOnly.FromDateTime(toValue);
        if (to < from)
        {
            PeriodError = "Дата «по» раніша за дату «з».";
            return;
        }
        if (to.DayNumber - from.DayNumber + 1 > DutyRosterService.MaxDays)
        {
            PeriodError = $"Період довший за {DutyRosterService.MaxDays} днів.";
            return;
        }
        PeriodError = null;
        if (from == _roster.DateFrom && to == _roster.DateTo) return;

        await ChangeAsync(async roster =>
        {
            roster.DateFrom = from;
            roster.DateTo = to;
            _carryOver = await _service.GetCarryOverAsync(roster);
            if (roster.UseEventExemptions) await _service.ApplyEventExemptionsAsync(roster);

            UpdateCaptions();
            BuildGrid();
            await ReplanAndSaveAsync();
        });
        await RefreshListLabelsAsync();
    }

    partial void OnPeoplePerDayChanged(int value)
    {
        if (_loading || _roster is null || value < 1) return;
        var roster = _roster;
        var before = roster.Clone();
        roster.PeoplePerDay = value;
        RebuildShifts();
        if (IsFirstDayOpen) OpenFirstDay();
        Track(async () =>
        {
            await ReplanAndSaveAsync();
            Remember(before);
            await _service.RememberPrefsAsync(roster);
        });
    }

    partial void OnUseEventExemptionsChanged(bool value)
    {
        if (_loading || _roster is null) return;
        var roster = _roster;
        var before = roster.Clone();
        roster.UseEventExemptions = value;
        Track(async () =>
        {
            await _service.ApplyEventExemptionsAsync(roster);
            await ReplanAndSaveAsync();
            Remember(before);
            await _service.RememberPrefsAsync(roster);
        });
    }

    partial void OnDutyMarkOnlyChanged(bool value)
    {
        if (_loading || _roster is null) return;
        var before = _roster.Clone();
        _roster.DutyMarkOnly = value;
        ApplyToGrid(DutyRosterPlanner.Check(_roster));
        Remember(before);
        Track(SaveSettingsAsync);
    }

    /// <summary>Лише колір вихідних - на розподіл нарядів не впливає.</summary>
    partial void OnWeekendsGreenChanged(bool value)
    {
        if (_loading || _roster is null) return;
        var before = _roster.Clone();
        _roster.WeekendsGreen = value;
        ApplyToGrid(DutyRosterPlanner.Check(_roster));
        Remember(before);
        Track(SaveSettingsAsync);
    }

    partial void OnCarryOverBalanceChanged(bool value)
    {
        if (_loading || _roster is null) return;
        var roster = _roster;
        var before = roster.Clone();
        roster.CarryOverBalance = value;
        Track(async () =>
        {
            _carryOver = await _service.GetCarryOverAsync(roster);
            UpdateCaptions();
            await ReplanAndSaveAsync();
            Remember(before);
            await _service.RememberPrefsAsync(roster);
        });
    }

    partial void OnHeadingChanged(string value)
    {
        if (_loading || _roster is null) return;
        _roster.Heading = value;
        UpdateCaptions();
        Track(SaveSettingsAsync);
    }

    partial void OnChangeoverChanged(string value)
    {
        if (_loading || _roster is null) return;
        _roster.Footer.Changeover = value;
        Track(SaveSettingsAsync);
    }

    partial void OnCleaningChanged(string value)
    {
        if (_loading || _roster is null) return;
        _roster.Footer.Cleaning = value;
        Track(SaveSettingsAsync);
    }
}
