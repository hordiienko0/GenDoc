using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Services.Import;
using GenDoc.Services.Intakes;
using GenDoc.Services.OrgTree;
using Microsoft.Win32;

namespace GenDoc.ViewModels.Import;

/// <summary>Набір у списку кроку «Набір і гілка».</summary>
public record IntakeOption(int Id, int RootOrgNodeId, string Display);

/// <summary>Гілка всередині набору. Дерево подається пласким списком із Depth —
/// рівнів тут одиниці, а пласким списком його простіше і малювати, і вибирати.</summary>
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

    /// <summary>Відступ за рівнем — те саме дерево, що в макеті.</summary>
    public Thickness Indent => new(12 + Depth * 18, 0, 0, 0);

    [ObservableProperty]
    private bool isCurrent;
}

public partial class ImportViewModel : ObservableObject
{
    private readonly IImportService _importService;
    private readonly IIntakeService _intakeService;
    private readonly IOrgTreeService _orgTreeService;
    private ImportParseResult? _parsed;

    public ImportViewModel(
        IImportService importService,
        IIntakeService intakeService,
        IOrgTreeService orgTreeService)
    {
        _importService = importService;
        _intakeService = intakeService;
        _orgTreeService = orgTreeService;
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

    // ─── Кроки майстра ───────────────────────────────────────────────────────
    // За макетом видно РІВНО ОДИН крок; смуга кроків угорі лише показує, де ми.

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

    // Стан кружків смуги кроків. Рахується тут, а не тригерами в XAML: чотири
    // кроки × три стани тригерами перетворюються на стіну розмітки.
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

    // Залитий кружок (пройдений або поточний) вимагає білої цифри.
    private string FillOf(int step) => step <= CurrentStep ? "filled" : string.Empty;

    private string LabelOf(int step) => step == CurrentStep ? "active" : string.Empty;

    /// <summary>«Рядків: 50 · готово 44 · конфліктів 2 · помилок 4» — рядок із
    /// макета над таблицею перевірки.</summary>
    public string RowStatsText =>
        $"Рядків: {TotalRows} · готово {ReadyCount} · потребують уваги {IssueCount}";

    public bool CanGoBack => CurrentStep > 1;

    /// <summary>Далі не пускаємо без файлу, а з кроку «Набір і гілка» — без
    /// обраного набору: інакше крок нічого не вирішує.</summary>
    public bool CanGoNext => CurrentStep switch
    {
        1 => HasFile,
        2 => TargetPermanentStaff || TargetNewIntake || SelectedIntake is not null,
        3 => true,
        _ => false
    };

    [RelayCommand]
    private void GoNext()
    {
        if (!CanGoNext) return;
        CurrentStep = Math.Min(CurrentStep + 1, 4);
        RecalculateValidation();
    }

    [RelayCommand]
    private void GoBack()
    {
        if (!CanGoBack) return;
        CurrentStep--;
    }

    // ─── Крок 2: набір і гілка ───────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(IsIntakeChoiceVisible))]
    private bool targetExistingIntake = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(IsIntakeChoiceVisible))]
    private bool targetNewIntake;

    /// <summary>Постійний склад у макеті не намальовано, але це наявна
    /// можливість застосунку — вона лишається третім вибором, а не зникає.</summary>
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
        "Новий набір створюється в розділі «Набори» — тут його ще немає в списку.";

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

    /// <summary>Гілки обраного набору. Піддерево беремо по materialized path
    /// кореня — так само, як це робить дерево підрозділів.</summary>
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
    private ObservableCollection<ImportRowPreview> preview = new();

    [ObservableProperty]
    private string previewHeaderText = string.Empty;

    [ObservableProperty]
    private string importButtonText = "Імпортувати 0 записів";

    [ObservableProperty]
    private string skippedNoteText = string.Empty;

    [ObservableProperty]
    private bool hasSkippedRows;

    /// <summary>Ціль імпорту з кроку «Набір і гілка». «Створити новий набір» поки
    /// що не створює його тут — набір заводиться в розділі «Набори», тож цей
    /// вибір лишає ціль виведеною з файлу, а не мовчки кладе людей не туди.</summary>
    private ImportTarget BuildTarget()
    {
        if (TargetPermanentStaff) return new ImportTarget(ImportTargetKind.PermanentStaff);

        if (TargetExistingIntake && SelectedIntake is { } intake)
            return new ImportTarget(ImportTargetKind.Intake, intake.Id, SelectedBranch?.Id);

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

    [RelayCommand(CanExecute = nameof(CanRunImport))]
    private void RunImport()
    {
        if (_parsed is null) return;

        var confirm = MessageBox.Show(
            $"Імпортувати {ReadyCount} записів?", "Підтвердження імпорту",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var summary = _importService.Import(_parsed, BuildTarget());

        var message = $"Імпортовано {summary.Imported}, пропущено {summary.Skipped}, помилок {summary.Errors}";
        if (summary.ErrorMessages.Count > 0)
        {
            message += "\n\nПричини:\n" + string.Join("\n", summary.ErrorMessages.Take(3));
            if (summary.ErrorMessages.Count > 3)
                message += $"\n…та ще {summary.ErrorMessages.Count - 3}";
        }

        MessageBox.Show(message, "Імпорт завершено", MessageBoxButton.OK,
            summary.Errors > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

        Reset();
    }

    private bool CanRunImport() => HasFile && ReadyCount > 0;

    private void LoadFile(string filePath)
    {
        _parsed = _importService.ParseFile(filePath);
        FileName = Path.GetFileName(filePath);
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
        TotalRows = _parsed.TotalRows;
        ReadyCount = rows.Count(r => r.Status is ImportRowStatus.Ok or ImportRowStatus.Warning);
        IssueCount = rows.Count(r => r.Status is ImportRowStatus.Error or ImportRowStatus.Duplicate);

        Preview = new ObservableCollection<ImportRowPreview>(rows.Take(8));
        PreviewHeaderText = $"Попередній перегляд ({Preview.Count} з {TotalRows} рядків)";

        OnPropertyChanged(nameof(RowStatsText));

        ImportButtonText = $"Імпортувати {ReadyCount} записів";
        HasSkippedRows = IssueCount > 0;
        SkippedNoteText = IssueCount > 0 ? $"{IssueCount} рядків буде пропущено — причини вказані вище" : string.Empty;

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
        Preview = new ObservableCollection<ImportRowPreview>();
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
