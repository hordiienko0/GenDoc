using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Services;
using GenDoc.Services.Import;
using GenDoc.Services.Intakes;
using GenDoc.Services.OrgTree;
using GenDoc.ViewModels.Personnel;
using GenDoc.ViewModels.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace GenDoc.ViewModels.Import;

public record IntakeOption(int Id, int RootOrgNodeId, string Display);

public partial class BranchOptionViewModel : ObservableObject
{
    public BranchOptionViewModel(int id, string name, int depth, int peopleCount)
    {
        Id = id;
        Name = name;
        Depth = depth;
        PeopleCount = peopleCount;
    }

    public int Id { get; }
    public string Name { get; }
    public int Depth { get; }
    public int PeopleCount { get; }

    public Thickness Indent => new(12 + Depth * 18, 0, 0, 0);

    [ObservableProperty]
    private bool isCurrent;
}

public partial class ImportViewModel : ObservableObject, IGuardedSection
{
    private readonly IImportService _importService;
    private readonly IIntakeService _intakeService;
    private readonly IOrgTreeService _orgTreeService;
    private readonly IDialogService _dialogService;
    private readonly IServiceProvider _serviceProvider;
    private ImportParseResult? _parsed;

    public ImportViewModel(
        IImportService importService,
        IIntakeService intakeService,
        IOrgTreeService orgTreeService,
        IDialogService dialogService,
        IServiceProvider serviceProvider)
    {
        _importService = importService;
        _intakeService = intakeService;
        _orgTreeService = orgTreeService;
        _dialogService = dialogService;
        _serviceProvider = serviceProvider;
        Reset();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoFile))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private bool hasFile;

    public bool NoFile => !HasFile;

    [ObservableProperty]
    private string fileName = string.Empty;

    [ObservableProperty]
    private int totalRows;

    [ObservableProperty]
    private int readyCount;

    [ObservableProperty]
    private int issueCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStep1))]
    [NotifyPropertyChangedFor(nameof(IsStep2))]
    [NotifyPropertyChangedFor(nameof(IsStep3))]
    [NotifyPropertyChangedFor(nameof(IsStep4))]
    [NotifyPropertyChangedFor(nameof(IsNotStep4))]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(Step1State), nameof(Step2State), nameof(Step3State), nameof(Step4State))]
    [NotifyPropertyChangedFor(nameof(Step1Fill), nameof(Step2Fill), nameof(Step3Fill), nameof(Step4Fill))]
    [NotifyPropertyChangedFor(nameof(Step1Label), nameof(Step2Label), nameof(Step3Label), nameof(Step4Label))]
    private int currentStep = 1;

    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    public bool IsStep3 => CurrentStep == 3;
    public bool IsStep4 => CurrentStep == 4;
    public bool IsNotStep4 => CurrentStep != 4;

    public string Step1State => StateOf(1);
    public string Step2State => StateOf(2);
    public string Step3State => StateOf(3);
    public string Step4State => StateOf(4);

    public string Step1Fill => FillOf(1);
    public string Step2Fill => FillOf(2);
    public string Step3Fill => FillOf(3);
    public string Step4Fill => FillOf(4);

    public string Step1Label => LabelOf(1);
    public string Step2Label => LabelOf(2);
    public string Step3Label => LabelOf(3);
    public string Step4Label => LabelOf(4);

    private string StateOf(int step) => step < CurrentStep ? "done" : step == CurrentStep ? "current" : string.Empty;

    private string FillOf(int step) => step <= CurrentStep ? "filled" : string.Empty;

    private string LabelOf(int step) => step == CurrentStep ? "active" : string.Empty;

    // «Увага» в таблиці - рядок імпортується з попередженням; пропускаються лише помилки й дублі.
    // Раніше тут стояло «потребують уваги 0» поруч із рядками «УВАГА» - одне одному суперечило.
    public string RowStatsText =>
        $"Рядків: {TotalRows} · буде імпортовано {NewCount}"
        + (WarningCount > 0 ? $" (з них з попередженням: {WarningCount})" : string.Empty)
        + (UpdateCount > 0 ? $" · актуалізовано {UpdateCount}" : string.Empty)
        + (MoveCount > 0 ? $" · перенесено {MoveCount}" : string.Empty)
        + $" · буде пропущено {SkipCount}";

    private int WarningCount => _validatedRows.Count(r => r.Status == ImportRowStatus.Warning);

    /// <summary>Чого немає в усьому файлі - одним рядком над таблицею, а не попередженням на кожній людині.</summary>
    public string? MissingColumnsNote
    {
        get
        {
            if (_parsed is null) return null;
            var notes = new List<string>();
            if (!ImportService.HasColumn(_parsed, ImportTargetField.RoomNumber))
                notes.Add("колонки «Кімната» немає - людей буде додано без розміщення (розселити можна в розділі «Кімнати»)");
            if (!ImportService.HasColumn(_parsed, ImportTargetField.ServiceNumber))
                notes.Add("колонки особового номера немає - повторний імпорт тих самих людей розпізнається за ПІБ і датою народження");
            return notes.Count == 0 ? null : "У файлі " + string.Join("; ", notes) + ".";
        }
    }

    public bool CanGoBack => CurrentStep > 1;

    public bool HasPendingWork => HasFile;

    public Task<bool> TryLeaveAsync()
    {
        if (!HasPendingWork) return Task.FromResult(true);

        var result = MessageBox.Show(
            $"Імпорт файлу «{FileName}» не завершено. Вийти й скинути зіставлення колонок?",
            "Незавершений імпорт", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return Task.FromResult(false);

        Reset();
        return Task.FromResult(true);
    }

    public bool CanGoNext => CurrentStep switch
    {
        1 => HasFile,
        2 => TargetPermanentStaff || TargetNewIntake || (TargetExistingIntake && SelectedIntake is not null),
        3 => true,
        _ => false
    };

    [RelayCommand]
    private async Task GoNextAsync()
    {
        if (!CanGoNext) return;
        if (CurrentStep == 2 && TargetNewIntake && !await CreateNewIntakeAsync()) return;
        CurrentStep = Math.Min(CurrentStep + 1, 4);
        RecalculateValidation();
    }

    private async Task<bool> CreateNewIntakeAsync()
    {
        var tree = _serviceProvider.GetRequiredService<OrgTreeViewModel>();
        await tree.EnsureLoadedAsync();

        var wizard = _serviceProvider.GetRequiredService<IntakeWizardViewModel>();
        await wizard.InitializeAsync();
        if (_dialogService.ShowDialog(wizard, Application.Current?.MainWindow) != true
            || wizard.CreatedIntake is not { } created)
            return false;

        await tree.ReloadAsync();
        WeakReferenceMessenger.Default.Send(new CountsChangedMessage());

        await LoadIntakesAsync();
        SelectedIntake = Intakes.FirstOrDefault(i => i.Id == created.Id);
        if (SelectedIntake is null) return false;

        TargetNewIntake = false;
        TargetExistingIntake = true;
        return true;
    }

    [RelayCommand]
    private void GoBack()
    {
        if (!CanGoBack) return;
        CurrentStep--;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(IsIntakeChoiceVisible))]
    private bool targetExistingIntake = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(IsIntakeChoiceVisible))]
    private bool targetNewIntake;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(IsIntakeChoiceVisible))]
    private bool targetPermanentStaff;

    public bool IsIntakeChoiceVisible => TargetExistingIntake;

    public ObservableCollection<IntakeOption> Intakes { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private IntakeOption? selectedIntake;

    public ObservableCollection<BranchOptionViewModel> Branches { get; } = new();

    [ObservableProperty]
    private BranchOptionViewModel? selectedBranch;

    [ObservableProperty]
    private string newIntakeHint =
        "Після «Далі» відкриється майстер створення набору - список буде імпортовано в цей новий набір.";

    partial void OnSelectedIntakeChanged(IntakeOption? value) => LoadBranchesCommand.Execute(null);

    partial void OnSelectedBranchChanged(BranchOptionViewModel? value)
    {
        foreach (var branch in Branches) branch.IsCurrent = ReferenceEquals(branch, value);
    }

    [RelayCommand]
    private void SelectBranch(BranchOptionViewModel? branch)
    {
        if (branch is not null) SelectedBranch = branch;
    }

    [RelayCommand]
    private async Task LoadIntakesAsync()
    {
        Intakes.Clear();

        foreach (var overview in await _intakeService.GetOverviewsAsync())
        {
            Intakes.Add(new IntakeOption(
                overview.Id,
                overview.RootOrgNodeId,
                $"{overview.DisplayNumber} · {overview.PeopleCount} осіб"));
        }

        SelectedIntake ??= Intakes.FirstOrDefault();
    }

    [RelayCommand]
    private async Task LoadBranchesAsync()
    {
        Branches.Clear();
        SelectedBranch = null;

        if (SelectedIntake is not { } intake) return;

        var all = await _orgTreeService.GetAllAsync();
        var root = all.FirstOrDefault(n => n.Id == intake.RootOrgNodeId);
        if (root is null) return;

        var subtree = all
            .Where(n => n.Path.StartsWith(root.Path, StringComparison.Ordinal))
            .OrderBy(n => n.Path, StringComparer.Ordinal)
            .ToList();

        foreach (var node in subtree)
        {
            Branches.Add(new BranchOptionViewModel(
                node.Id, node.Name, node.Depth - root.Depth,
                await _orgTreeService.CountPeopleInBranchAsync(node.Id)));
        }

        SelectedBranch = Branches.FirstOrDefault();
    }

    [ObservableProperty]
    private ObservableCollection<ImportColumnViewModel> columns = new();

    [ObservableProperty]
    private ObservableCollection<ImportPreviewRowViewModel> preview = new();

    private List<ImportRowPreview> _validatedRows = new();

    /// <summary>Скільки людей з файлу вже є в особовому складі (у будь-якому наборі чи в постійному складі).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExistingPeople))]
    private int existingCount;

    public bool HasExistingPeople => ExistingCount > 0;

    [ObservableProperty]
    private string existingSummaryText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMovableDuplicates))]
    private int movableDuplicateCount;

    public bool HasMovableDuplicates => MovableDuplicateCount > 0;

    [ObservableProperty]
    private string moveAllText = string.Empty;

    [ObservableProperty]
    private bool moveAllDuplicates;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdatableDuplicates))]
    private int updatableDuplicateCount;

    public bool HasUpdatableDuplicates => UpdatableDuplicateCount > 0;

    [ObservableProperty]
    private string updateAllText = string.Empty;

    [ObservableProperty]
    private bool updateAllDuplicates;

    partial void OnMoveAllDuplicatesChanged(bool value) => ApplyBulkActions();

    partial void OnUpdateAllDuplicatesChanged(bool value) => ApplyBulkActions();

    // Обидві галочки разом: кого можна перенести - переносимо (перенесення й так оновлює дані),
    // решту з відмінностями - актуалізуємо; зняли галочку - рядок знову «пропустити».
    private void ApplyBulkActions()
    {
        foreach (var row in _validatedRows.Where(r => r.ExistingRecipientId is not null))
        {
            row.Action = MoveAllDuplicates && row.CanMove ? ImportExistingAction.Move
                : UpdateAllDuplicates && row.CanUpdate ? ImportExistingAction.Update
                : row.Action == ImportExistingAction.AddAsNew ? ImportExistingAction.AddAsNew
                : ImportExistingAction.Skip;
        }

        SyncPreviewFromModel();
        UpdateImportButton();
    }

    private void SyncPreviewFromModel()
    {
        foreach (var visible in Preview) visible.SyncFromModel();
    }

    [ObservableProperty]
    private string previewHeaderText = string.Empty;

    [ObservableProperty]
    private string importButtonText = "Імпортувати 0 записів";

    [ObservableProperty]
    private string skippedNoteText = string.Empty;

    [ObservableProperty]
    private bool hasSkippedRows;

    internal ImportTarget BuildTarget()
    {
        if (TargetPermanentStaff) return new ImportTarget(ImportTargetKind.PermanentStaff);

        if (TargetExistingIntake && SelectedIntake is { } intake)
            return new ImportTarget(ImportTargetKind.Intake, intake.Id, SelectedBranch?.Id);

        if (TargetNewIntake) return new ImportTarget(ImportTargetKind.Intake);

        return ImportTarget.FromFile;
    }

    [RelayCommand]
    private void PickFile()
    {
        var dialog = new OpenFileDialog { Filter = "Excel файли (*.xlsx)|*.xlsx" };
        if (dialog.ShowDialog() != true) return;

        LoadFile(dialog.FileName);
    }

    [RelayCommand]
    private void PickAnotherFile() => PickFile();

    /// <summary>Вікна повідомлень імпорту; у тестах підміняється, щоб не відкривати їх.</summary>
    internal Func<string, string, MessageBoxButton, MessageBoxImage, MessageBoxResult> ShowMessage { get; set; } =
        MessageBox.Show;

    [RelayCommand(CanExecute = nameof(CanRunImport))]
    private void RunImport()
    {
        if (_parsed is null) return;

        // Люди вже є в базі, у файлі про них інші дані, а дію не обрано - питаємо, чи актуалізувати.
        var pending = _validatedRows
            .Where(r => r.CanUpdate && r.Action == ImportExistingAction.Skip)
            .ToList();

        if (pending.Count > 0)
        {
            var answer = ShowMessage(BuildPendingUpdateQuestion(pending), "Люди вже є в базі",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) return;

            if (answer == MessageBoxResult.Yes)
            {
                foreach (var row in pending) row.Action = ImportExistingAction.Update;
                SyncPreviewFromModel();
                UpdateImportButton();
            }

            if (NewCount + UpdateCount + MoveCount == 0)
            {
                ShowMessage("Нових людей у файлі немає, наявні картки залишено як є.", "Імпорт",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }
        else
        {
            var confirm = ShowMessage(BuildConfirmQuestion(), "Підтвердження імпорту",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
        }

        var rowActions = _validatedRows
            .Where(r => r.ExistingRecipientId is not null && r.Action != ImportExistingAction.Skip)
            .ToDictionary(r => r.RowNumber, r => r.Action);

        var summary = _importService.Import(_parsed, BuildTarget(), rowActions);

        var message = $"Імпортовано {summary.Imported}, актуалізовано {summary.Updated}, перенесено {summary.Moved}, "
                      + $"пропущено {summary.Skipped}, помилок {summary.Errors}";
        if (summary.ErrorMessages.Count > 0)
        {
            message += "\n\nПричини:\n" + string.Join("\n", summary.ErrorMessages.Take(3));
            if (summary.ErrorMessages.Count > 3)
                message += $"\n…та ще {summary.ErrorMessages.Count - 3}";
        }

        ShowMessage(message, "Імпорт завершено", MessageBoxButton.OK,
            summary.Errors > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

        Reset();
    }

    private string BuildConfirmQuestion()
    {
        var parts = new List<string> { $"додати нових: {NewCount}" };
        if (UpdateCount > 0) parts.Add($"актуалізувати картки: {UpdateCount}");
        if (MoveCount > 0) parts.Add($"перенести сюди: {MoveCount}");

        var question = $"Імпортувати - {string.Join(", ", parts)}?";
        if (UpdateCount + MoveCount > 0)
            question += "\n\nНаявні картки отримають дані з файлу (порожні клітинки нічого не стирають).";
        return question;
    }

    private const int PendingNamesShown = 6;

    internal string BuildPendingUpdateQuestion(IReadOnlyList<ImportRowPreview> pending)
    {
        var lines = pending.Take(PendingNamesShown).Select(r =>
        {
            var fields = string.Join(", ", r.Differences.Take(3).Select(d => d.Field));
            if (r.Differences.Count > 3) fields += $" і ще {r.Differences.Count - 3}";
            return $"• {r.FullNameDisplay} ({r.ExistingLocation}): {fields}";
        }).ToList();
        if (pending.Count > PendingNamesShown) lines.Add($"…та ще {pending.Count - PendingNamesShown}");

        var people = $"{pending.Count} {PluralHelper.Pluralize(pending.Count, "людина", "людини", "людей")}";
        var others = new List<string>();
        if (NewCount > 0) others.Add($"нових буде додано: {NewCount}");
        if (UpdateCount > 0) others.Add($"актуалізовано: {UpdateCount}");
        if (MoveCount > 0) others.Add($"перенесено: {MoveCount}");

        return $"{people} з файлу вже є в особовому складі, але у файлі про них інші дані:\n\n"
               + string.Join("\n", lines)
               + "\n\nАктуалізувати їхні картки даними з файлу?"
               + (others.Count > 0
                   ? $"\n(Крім того, {string.Join(", ", others)}.)"
                     + "\n\nТак - імпортувати й актуалізувати; Ні - імпортувати, а ці картки залишити як є; "
                   : "\n\nТак - актуалізувати; Ні - залишити картки як є; ")
               + "Скасувати - повернутися до перевірки.";
    }

    private bool CanRunImport() => HasFile && (NewCount > 0 || UpdateCount > 0 || MoveCount > 0 || UpdatableDuplicateCount > 0);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFileError))]
    private string? fileError;

    public bool HasFileError => !string.IsNullOrEmpty(FileError);

    internal void LoadFile(string filePath)
    {
        FileError = null;

        ImportParseResult parsed;
        try
        {
            parsed = _importService.ParseFile(filePath);
        }
        catch (Exception ex)
        {
            FileError = DescribeFileError(filePath, ex);
            return;
        }

        Load(parsed, Path.GetFileName(filePath));
    }

    internal static string DescribeFileError(string filePath, Exception ex) => ex switch
    {
        FileNotFoundException => $"Файл не знайдено: {filePath}",
        DirectoryNotFoundException => $"Теку не знайдено: {Path.GetDirectoryName(filePath)}",
        UnauthorizedAccessException => "Немає прав на читання цього файлу.",
        IOException => $"Не вдалося прочитати файл «{Path.GetFileName(filePath)}»: він зайнятий іншою програмою. "
                       + $"Закрийте файл в Excel і спробуйте ще раз. Технічна причина: {ex.Message}",
        _ => "Файл не є книгою Excel (.xlsx). Якщо це старий формат .xls або таблиця з іншої програми - "
             + $"відкрийте її в Excel і збережіть як .xlsx. Технічна причина: {ex.Message}"
    };

    internal void Load(ImportParseResult parsed, string fileName)
    {
        _parsed = parsed;
        FileName = fileName;
        HasFile = true;
        CurrentStep = 2;
        LoadIntakesCommand.Execute(null);

        foreach (var existing in Columns)
            existing.MappingChanged -= OnColumnMappingChanged;

        var newColumns = new ObservableCollection<ImportColumnViewModel>();
        foreach (var column in _parsed.Columns)
        {
            var vm = new ImportColumnViewModel(column);
            vm.MappingChanged += OnColumnMappingChanged;
            newColumns.Add(vm);
        }
        Columns = newColumns;

        RecalculateValidation();
    }

    private void OnColumnMappingChanged(object? sender, EventArgs e) => RecalculateValidation();

    private void RecalculateValidation()
    {
        if (_parsed is null) return;

        var rows = _importService.Validate(_parsed, BuildTarget());
        // Рядки без жодного імпортованого значення Validate не повертає - їх і не рахуємо.
        TotalRows = rows.Count;
        ReadyCount = rows.Count(r => r.Status is ImportRowStatus.Ok or ImportRowStatus.Warning);
        IssueCount = rows.Count(r => r.Status is ImportRowStatus.Error or ImportRowStatus.Duplicate);

        _validatedRows = rows;
        MoveAllDuplicates = false;
        UpdateAllDuplicates = false;

        var existing = rows.Where(r => r.ExistingRecipientId is not null).ToList();
        ExistingCount = existing.Count;
        var unchanged = existing.Count(r => !r.CanUpdate);
        ExistingSummaryText = ExistingCount == 0
            ? string.Empty
            : $"{ExistingCount} {PluralHelper.Pluralize(ExistingCount, "людина", "людини", "людей")} з файлу "
              + "вже є в особовому складі - другої картки не буде, якщо не обрати інше. "
              + (ExistingCount - unchanged > 0 ? $"З іншими даними у файлі: {ExistingCount - unchanged}. " : string.Empty)
              + (unchanged > 0 ? $"Без змін: {unchanged}. " : string.Empty)
              + "Де саме кожна людина - у примітці; що зробити - у колонці «Дія».";

        UpdatableDuplicateCount = existing.Count(r => r.CanUpdate);
        UpdateAllText = UpdatableDuplicateCount == 1
            ? "Актуалізувати дані 1 людини, у якої у файлі інші дані"
            : $"Актуалізувати дані {UpdatableDuplicateCount} людей, у яких у файлі інші дані";

        MovableDuplicateCount = existing.Count(r => r.CanMove);
        var destination = TargetPermanentStaff ? "у постійний склад" : "у цей набір";
        MoveAllText = MovableDuplicateCount == 1
            ? $"Перенести 1 людину з іншого набору {destination}"
            : $"Перенести {MovableDuplicateCount} людей з інших наборів {destination}";

        Preview = new ObservableCollection<ImportPreviewRowViewModel>(
            PreviewRows(rows).Select(r => new ImportPreviewRowViewModel(r)));
        foreach (var visible in Preview) visible.PropertyChanged += OnPreviewRowChanged;

        PreviewHeaderText = IssueCount > 0
            ? $"Попередній перегляд ({Preview.Count} з {TotalRows} рядків) · показано всі {IssueCount} рядків, що потребують уваги"
            : $"Попередній перегляд ({Preview.Count} з {TotalRows} рядків)";

        OnPropertyChanged(nameof(RowStatsText));
        OnPropertyChanged(nameof(MissingColumnsNote));

        UpdateImportButton();
    }

    partial void OnCurrentStepChanged(int value) => UpdateSkippedNote();

    // Таблиця з причинами є лише на кроці «Перевірка»; на «Наборі» і «Зіставленні» вище нічого не видно.
    private void UpdateSkippedNote()
    {
        var skipped = SkipCount;
        HasSkippedRows = skipped > 0;
        if (skipped <= 0)
        {
            SkippedNoteText = string.Empty;
            return;
        }

        var rows = $"{skipped} {PluralHelper.Pluralize(skipped, "рядок", "рядки", "рядків")} буде пропущено";
        SkippedNoteText = CurrentStep == 4
            ? $"{rows} - причини в таблиці вище"
            : $"{rows} - причини на кроці «Перевірка»";
    }

    internal const int PreviewSampleSize = 8;

    internal static IEnumerable<ImportRowPreview> PreviewRows(IReadOnlyList<ImportRowPreview> rows)
    {
        static bool NeedsAttention(ImportRowPreview row)
            => row.Status is ImportRowStatus.Error or ImportRowStatus.Duplicate;

        var sample = rows.Where(r => !NeedsAttention(r)).Take(PreviewSampleSize);
        return rows.Where(NeedsAttention).Concat(sample).OrderBy(r => r.RowNumber);
    }

    private void OnPreviewRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImportPreviewRowViewModel.Action)) UpdateImportButton();
    }

    private int MoveCount => _validatedRows.Count(r => r.Action == ImportExistingAction.Move);

    private int UpdateCount => _validatedRows.Count(r => r.Action == ImportExistingAction.Update);

    /// <summary>Нові картки: готові рядки й ті, кого (збіг лише за ПІБ) вирішили додати як іншу людину.</summary>
    private int NewCount => ReadyCount + _validatedRows.Count(r => r.Action == ImportExistingAction.AddAsNew);

    private int SkipCount => IssueCount - _validatedRows.Count(r => r.Action != ImportExistingAction.Skip);

    private void UpdateImportButton()
    {
        var parts = new List<string>();
        if (UpdateCount > 0) parts.Add($"актуалізувати {UpdateCount}");
        if (MoveCount > 0) parts.Add($"перенести {MoveCount}");

        ImportButtonText = parts.Count == 0
            ? $"Імпортувати {NewCount} записів"
            : $"Імпортувати {NewCount}, {string.Join(", ", parts)}";

        OnPropertyChanged(nameof(RowStatsText));
        UpdateSkippedNote();
        RunImportCommand.NotifyCanExecuteChanged();
    }

    private void Reset()
    {
        _parsed = null;
        HasFile = false;
        CurrentStep = 1;
        FileName = string.Empty;
        TotalRows = 0;
        ReadyCount = 0;
        IssueCount = 0;
        Columns = new ObservableCollection<ImportColumnViewModel>();
        Preview = new ObservableCollection<ImportPreviewRowViewModel>();
        _validatedRows = new List<ImportRowPreview>();
        ExistingCount = 0;
        ExistingSummaryText = string.Empty;
        MovableDuplicateCount = 0;
        MoveAllDuplicates = false;
        MoveAllText = string.Empty;
        UpdatableDuplicateCount = 0;
        UpdateAllDuplicates = false;
        UpdateAllText = string.Empty;
        PreviewHeaderText = string.Empty;
        ImportButtonText = "Імпортувати 0 записів";
        SkippedNoteText = string.Empty;
        HasSkippedRows = false;

        TargetExistingIntake = true;
        TargetNewIntake = false;
        TargetPermanentStaff = false;
        Intakes.Clear();
        Branches.Clear();
        SelectedIntake = null;
        SelectedBranch = null;

        RunImportCommand.NotifyCanExecuteChanged();
    }
}
