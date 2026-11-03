using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Services;
using GenDoc.Services.Duty;
using Microsoft.Win32;

namespace GenDoc.ViewModels.Duty;

/// <summary>
/// Вкладка «Вечірня перевірка»: період (типово два місяці від сьогодні), два блоки - набір і прикомандировані -
/// кожен зі своїми папками дерева (як у графіку чергувань) і галочками на людях, рядок підпису й експорт листа
/// як паперовий «СПИСОК вечірньої перевірки». Хто в папках прикомандированих, у перший блок не йде.
/// </summary>
public partial class EveningCheckViewModel : ObservableObject
{
    private const string Caption = "Вечірня перевірка";

    /// <summary>Типовий період списку - два місяці від сьогодні.</summary>
    public const int DefaultMonths = 2;

    private readonly IEveningCheckService _service;
    private readonly ActiveIntakeState _activeIntake;
    private bool _initialized;

    public EveningCheckViewModel(IEveningCheckService service, ActiveIntakeState activeIntake)
    {
        _service = service;
        _activeIntake = activeIntake;
        Course = new EveningCheckGroupViewModel(EveningCheckText.CourseTitle, "1. Набір - особовий склад курсів", UpdateSummaries, OnCompositionChanged);
        Seconded = new EveningCheckGroupViewModel(EveningCheckText.SecondedTitle, "2. Прикомандировані", UpdateSummaries, OnCompositionChanged,
            canSplitByFitness: true);
    }

    /// <summary>Блок «ОСОБОВИЙ СКЛАД КУРСІВ…» - типово корінь активного набору.</summary>
    public EveningCheckGroupViewModel Course { get; }

    /// <summary>Блок «ПРИКОМАНДИРОВАНІ» - типово папка «Прикомандировані» того ж набору.</summary>
    public EveningCheckGroupViewModel Seconded { get; }

    [ObservableProperty] private DateTime? _dateFrom;
    [ObservableProperty] private DateTime? _dateTo;
    [ObservableProperty] private string _signature = EveningCheckText.SignaturePosition;
    [ObservableProperty] private string _periodText = string.Empty;
    [ObservableProperty] private string? _periodError;
    [ObservableProperty] private string _totalText = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = string.Empty;

    /// <summary>Вкладка «Попередній перегляд» замість карток блоків.</summary>
    [ObservableProperty] private bool _isPreviewOpen;

    /// <summary>Зростає з кожною зміною того, що йде на лист, - вікно перебудовує перегляд.</summary>
    [ObservableProperty] private int _previewVersion;

    partial void OnSignatureChanged(string value) => PreviewVersion++;

    partial void OnDateFromChanged(DateTime? value) => UpdateSummaries();
    partial void OnDateToChanged(DateTime? value) => UpdateSummaries();

    /// <summary>Остання фонова дія (зміна папок → завантаження людей); тести чекають на неї.</summary>
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
            Status = "Помилка: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        IsBusy = true;
        try
        {
            ResetPeriod();
            Signature = await _service.GetDefaultSignatureAsync();

            var folders = await _service.GetFoldersAsync();
            var courseRoot = DefaultCourseRoot(folders);
            Course.BuildFolders(folders, courseRoot is null ? Array.Empty<int>() : new[] { courseRoot.Id });
            Seconded.BuildFolders(folders, DefaultSecondedFolders(folders, courseRoot?.Id));
            await RefreshPeopleAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Корінь активного набору, а як його немає, то останнього створеного.</summary>
    private DutyFolderOption? DefaultCourseRoot(IReadOnlyList<DutyFolderOption> folders)
    {
        var roots = folders.Where(f => f.IsIntakeRoot).ToList();
        return roots.FirstOrDefault(f => f.Id == _activeIntake.Current?.RootOrgNodeId)
               ?? roots.MaxBy(f => f.IntakeId);
    }

    /// <summary>
    /// Папка прикомандированих за назвою: спершу всередині вибраного набору, інакше в корені дерева,
    /// інакше будь-яка; жодної - блок порожній, папки відмічають вручну.
    /// </summary>
    internal static IReadOnlyList<int> DefaultSecondedFolders(IReadOnlyList<DutyFolderOption> folders, int? courseRootId)
    {
        var byId = folders.ToDictionary(f => f.Id);
        var candidates = folders.Where(f => EveningCheckText.IsSecondedFolderName(f.Name)).ToList();

        var pick = candidates.FirstOrDefault(f => courseRootId is int root && IsInside(f, root))
                   ?? candidates.FirstOrDefault(f => f.ParentId is null)
                   ?? candidates.FirstOrDefault();
        return pick is null ? Array.Empty<int>() : new[] { pick.Id };

        bool IsInside(DutyFolderOption folder, int rootId)
        {
            for (var up = folder.ParentId; up is int id; up = byId.GetValueOrDefault(id)?.ParentId)
                if (id == rootId) return true;
            return false;
        }
    }

    private void OnCompositionChanged() => Track(RefreshPeopleAsync);

    /// <summary>Люди обох блоків з їхніх папок; хто в папках прикомандированих, у перший блок не береться.</summary>
    private async Task RefreshPeopleAsync()
    {
        var secondedIds = Seconded.FolderPeopleIds;
        var courseAll = Course.FolderPeopleIds;
        var courseIds = Seconded.IsIncluded ? courseAll.Except(secondedIds).ToHashSet() : courseAll;
        Course.TakenByOtherBlock = courseAll.Count - courseIds.Count;

        Course.SetPeople(await _service.GetPeopleAsync(courseIds));
        Seconded.SetPeople(await _service.GetPeopleAsync(secondedIds));
        UpdateSummaries();
    }

    /// <summary>Період з сьогодні на два місяці: «03.11 - 02.01».</summary>
    [RelayCommand]
    private void ResetPeriod()
    {
        var today = DateTime.Today;
        DateFrom = today;
        DateTo = today.AddMonths(DefaultMonths).AddDays(-1);
    }

    /// <summary>Період списку; <paramref name="error"/> - чому його не можна взяти.</summary>
    private bool TryGetPeriod(out DateOnly from, out DateOnly to, out string? error)
    {
        from = default;
        to = default;
        if (DateFrom is not DateTime start || DateTo is not DateTime end)
        {
            error = "Вкажіть дати «з» і «по».";
            return false;
        }

        from = DateOnly.FromDateTime(start);
        to = DateOnly.FromDateTime(end);
        if (to < from)
        {
            error = "Дата «по» раніша за дату «з».";
            return false;
        }

        var days = to.DayNumber - from.DayNumber + 1;
        if (days > EveningCheckXlsxExporter.MaxDays)
        {
            error = $"Період довший за {EveningCheckXlsxExporter.MaxDays} днів - розбийте його на кілька списків.";
            return false;
        }

        error = null;
        return true;
    }

    private void UpdateSummaries()
    {
        PreviewVersion++;
        var pages = 0;
        if (TryGetPeriod(out var from, out var to, out var error))
        {
            var lengths = EveningCheckXlsxExporter.PageLengths(from, to);
            pages = lengths.Count;
            var days = to.DayNumber - from.DayNumber + 1;
            var split = pages == 1 ? string.Empty : $" ({string.Join(" + ", lengths)} днів)";
            PeriodText = $"{days} {PluralHelper.Pluralize(days, "день", "дні", "днів")}: "
                         + $"{pages} {PluralHelper.Pluralize(pages, "сторінка", "сторінки", "сторінок")} дат{split}";
        }
        else
        {
            PeriodText = string.Empty;
        }
        PeriodError = error;

        Course.UpdateSummary(pages);
        Seconded.UpdateSummary(pages);

        var included = IncludedGroups().ToList();
        if (pages == 0 || included.Count == 0)
        {
            TotalText = included.Count == 0 ? "Відмітьте хоча б один блок." : string.Empty;
            return;
        }

        var blocks = included.Sum(g => EveningCheckXlsxExporter.BlocksOf(g.ToGroup()));
        var sheets = pages * (blocks + 1);
        TotalText = $"Разом {sheets} {PluralHelper.Pluralize(sheets, "аркуш", "аркуші", "аркушів")} A4 альбомних "
                    + "(на кожну сторінку дат - блоки і «Облікові дані»).";
    }

    private IEnumerable<EveningCheckGroupViewModel> IncludedGroups()
        => new[] { Course, Seconded }.Where(g => g.IsIncluded);

    /// <summary>Дані листа з поточних полів; порожньо, коли період хибний.</summary>
    public EveningCheckData? BuildData()
    {
        if (!TryGetPeriod(out var from, out var to, out _)) return null;

        var data = new EveningCheckData
        {
            DateFrom = from,
            DateTo = to,
            Signature = string.IsNullOrWhiteSpace(Signature) ? EveningCheckText.SignaturePosition : Signature.Trim()
        };
        foreach (var group in IncludedGroups()) data.Groups.Add(group.ToGroup());
        return data;
    }

    /// <summary>Дані для файлу чи друку; порожньо, коли щось не так, - про це вже сказано користувачеві.</summary>
    private EveningCheckData? DataForOutput(string verb)
    {
        var data = BuildData();
        if (data is null)
        {
            MessageBox.Show(PeriodError ?? "Вкажіть період.", Caption, MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        if (data.Groups.Count == 0)
        {
            MessageBox.Show("Відмітьте хоча б один блок - набір або прикомандированих.", Caption, MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        if (data.PeopleCount == 0
            && MessageBox.Show($"У списку нікого немає - блоки будуть із самих порожніх пронумерованих рядків. {verb}?", Caption,
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return null;
        return data;
    }

    /// <summary>
    /// Ті самі сторінки, що в Excel, - одразу на принтер. Без підключеного принтера у вікні друку обрано
    /// «Microsoft Print to PDF»: Windows спитає, куди зберегти PDF.
    /// </summary>
    [RelayCommand]
    private void Print()
    {
        if (DataForOutput("Друкувати") is not { } data) return;

        try
        {
            var printer = DutyRosterPrinter.Print(EveningCheckPrintLayout.Document(data),
                $"Список вечірньої перевірки {DutyRosterText.PeriodLabel(data.DateFrom, data.DateTo)}");
            if (printer is null) return;
            _service.LogExport(data, printer);
            Status = $"Надіслано на друк о {DateTime.Now:HH:mm}: {printer}";
        }
        catch (Exception ex) when (ex is System.Printing.PrintSystemException or InvalidOperationException)
        {
            MessageBox.Show($"Не вдалося надрукувати:\n{ex.Message}", Caption, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void Export()
    {
        var data = DataForOutput("Зберегти");
        if (data is null) return;

        var dialog = new SaveFileDialog
        {
            Title = "Зберегти список вечірньої перевірки",
            Filter = "Книга Excel (*.xlsx)|*.xlsx",
            FileName = EveningCheckText.FileName(data.DateFrom, data.DateTo),
            AddExtension = true
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            EveningCheckXlsxExporter.Save(data, dialog.FileName);
            _service.LogExport(data, dialog.FileName);
            Status = $"Збережено о {DateTime.Now:HH:mm}: {System.IO.Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Не вдалося зберегти файл:\n{ex.Message}\n\nМожливо, він зараз відкритий в Excel.", Caption,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show($"Список збережено:\n{dialog.FileName}\n\nВідкрити файл?", Caption,
                MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
        {
            Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
    }
}
