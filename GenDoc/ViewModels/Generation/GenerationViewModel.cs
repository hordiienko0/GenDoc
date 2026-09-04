using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Services.Navigation;
using GenDoc.Services.Recipients;
using GenDoc.ViewModels.Completeness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace GenDoc.ViewModels.Generation;

public partial class GenerationViewModel : ObservableObject, INavigationTarget
{
    private readonly IGenerationService _generationService;
    private readonly IDialogService _dialogService;
    private readonly IServiceProvider _serviceProvider;
    private readonly Services.Completeness.ICompletenessService _completenessService;
    private readonly IRecipientService _recipientService;
    private readonly IManualTagFormBuilder _manualTagFormBuilder;
    private readonly IOutputFolderService _outputFolderService;
    private readonly IUserSettingsService _userSettings;
    private readonly Services.Completeness.IIntakeServiceAccessor _intakeAccessor;
    private readonly IMessenger _messenger;

    public GenerationViewModel(
        IGenerationService generationService,
        IDialogService dialogService,
        IServiceProvider serviceProvider,
        Services.Completeness.ICompletenessService completenessService,
        IRecipientService recipientService,
        IManualTagFormBuilder manualTagFormBuilder,
        IOutputFolderService outputFolderService,
        IUserSettingsService userSettings,
        Services.Completeness.IIntakeServiceAccessor intakeAccessor,
        IMessenger messenger)
    {
        _generationService = generationService;
        _dialogService = dialogService;
        _serviceProvider = serviceProvider;
        _completenessService = completenessService;
        _recipientService = recipientService;
        _manualTagFormBuilder = manualTagFormBuilder;
        _outputFolderService = outputFolderService;
        _userSettings = userSettings;
        _intakeAccessor = intakeAccessor;
        _messenger = messenger;
        _messenger.Register<GenerationViewModel, ActiveIntakeChangedMessage>(this,
            static (recipient, message) => recipient.OnActiveIntakeChanged());
        RefreshPackages();
        RefreshRecipientOptions();
        RefreshAllRecipientsCount();
        InitialLoad = LoadDefaultOutputFolderAsync();
    }

    internal Task InitialLoad { get; }

    public async Task ApplyNavigationPayloadAsync(object payload)
    {
        if (payload is not IntakeNavigationPayload nav) return;

        var packageId = nav.PackageId ?? await _completenessService.GetDefaultPackageIdAsync(nav.IntakeId);
        if (packageId is not int id) return;

        var item = Packages.FirstOrDefault(p => p.Id == id);
        if (item is not null) await RefreshForPackageAsync(item);
    }

    [ObservableProperty]
    private ObservableCollection<GenerationPackageListItemViewModel> packages = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPackage))]
    [NotifyPropertyChangedFor(nameof(NoSelectedPackage))]
    [NotifyCanExecuteChangedFor(nameof(GenerateAllCommand))]
    private GenerationPackageListItemViewModel? selectedPackage;

    public bool HasSelectedPackage => SelectedPackage is not null;
    public bool NoSelectedPackage => !HasSelectedPackage;

    [ObservableProperty]
    private ObservableCollection<PackageTemplateSummaryItemViewModel> packageTemplates = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GenerateButtonText))]
    private int recipientCount;

    [ObservableProperty]
    private ManualTagFormViewModel? manualTagForm;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotCreatingPackage))]
    private bool isCreatingPackage;

    public bool NotCreatingPackage => !IsCreatingPackage;

    [ObservableProperty]
    private string newPackageName = string.Empty;

    [ObservableProperty]
    private string newPackageDescription = string.Empty;

    [ObservableProperty]
    private ObservableCollection<GenerationTemplateCheckItemViewModel> templateCheckItems = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOutputFolder))]
    [NotifyCanExecuteChangedFor(nameof(GenerateAllCommand))]
    private string? outputFolder;

    public bool HasOutputFolder => !string.IsNullOrWhiteSpace(OutputFolder);

    private async Task LoadDefaultOutputFolderAsync()
        => OutputFolder = await _outputFolderService.GetDefaultAsync();

    [ObservableProperty]
    private GenerationResultViewModel? lastResult;

    [ObservableProperty]
    private bool regenerateExisting;

    [ObservableProperty]
    private DateTime? documentDate = DateTime.Today;

    partial void OnDocumentDateChanged(DateTime? value) => ManualTagForm?.SetDocumentDate(value);

    partial void OnManualTagFormChanged(ManualTagFormViewModel? value) => value?.SetDocumentDate(DocumentDate);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotBusy))]
    private bool isBusy;

    public bool NotBusy => !IsBusy;

    [ObservableProperty]
    private string progressText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseSelectedRecipients))]
    [NotifyPropertyChangedFor(nameof(ShowNoRecipientsSelectedHint))]
    [NotifyCanExecuteChangedFor(nameof(GenerateAllCommand))]
    [NotifyPropertyChangedFor(nameof(GenerateButtonText))]
    private bool useAllRecipients = true;

    public bool UseSelectedRecipients => !UseAllRecipients;

    public ObservableCollection<RecipientCheckRowViewModel> RecipientOptions { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedRecipientsCountLabel))]
    [NotifyPropertyChangedFor(nameof(ShowNoRecipientsSelectedHint))]
    [NotifyCanExecuteChangedFor(nameof(GenerateAllCommand))]
    [NotifyPropertyChangedFor(nameof(GenerateButtonText))]
    private int selectedRecipientsCount;

    public string SelectedRecipientsCountLabel => $"Обрано {SelectedRecipientsCount} з {RecipientOptions.Count(r => r.IsVisible)}";
    public bool ShowNoRecipientsSelectedHint => UseSelectedRecipients && SelectedRecipientsCount == 0;

    [ObservableProperty] private string recipientSearchText = string.Empty;

    partial void OnRecipientSearchTextChanged(string value)
    {
        foreach (var row in RecipientOptions) row.MatchesSearch = RecipientCheckRowViewModel.Matches(row.FullName, value);
    }

    public string GenerateButtonText => BuildGenerateButtonText(UseAllRecipients, RecipientCount, SelectedRecipientsCount, IntakeLabel);

    internal static string BuildGenerateButtonText(bool useAll, int all, int selected, string? intakeLabel = null)
    {
        if (!useAll) return $"Згенерувати обраним ({selected})";
        return intakeLabel is null ? $"Згенерувати всім ({all})" : $"Згенерувати всім ({intakeLabel}: {all})";
    }

    partial void OnUseAllRecipientsChanged(bool value) => RefreshPackageTemplateCounts();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RosterScopeHint))]
    private bool includePermanentStaff;

    partial void OnIncludePermanentStaffChanged(bool value) => RefreshRosterScope();

    private Models.Intake? ActiveIntake => _intakeAccessor.ActiveIntake;

    public bool HasActiveIntake => ActiveIntake is not null;

    private string? IntakeLabel => ActiveIntake is { } intake ? BuildIntakeLabel(intake.Number, intake.DisplayNumber) : null;

    internal static string BuildIntakeLabel(int number, string? displayNumber)
        => string.IsNullOrWhiteSpace(displayNumber) ? $"набір №{number}" : displayNumber.Trim();

    public string RosterScopeHint => BuildRosterScopeHint(IntakeLabel, IncludePermanentStaff);

    internal static string BuildRosterScopeHint(string? intakeLabel, bool includePermanentStaff)
    {
        if (intakeLabel is null) return "Активного набору немає — у списку всі люди бази, включно з постійним складом.";
        return includePermanentStaff
            ? $"У списку — {intakeLabel} і постійний склад."
            : $"У списку — {intakeLabel}, без постійного складу.";
    }

    private void OnActiveIntakeChanged()
    {
        OnPropertyChanged(nameof(HasActiveIntake));
        OnPropertyChanged(nameof(RosterScopeHint));
        RefreshRosterScope();
    }

    private void RefreshRosterScope()
    {
        RefreshRecipientOptions();
        RefreshAllRecipientsCount();
        OnPropertyChanged(nameof(GenerateButtonText));
    }

    private RosterSelection BuildRosterSelection() => new(
        UseAllRecipients,
        UseAllRecipients ? Array.Empty<int>() : RecipientOptions.Where(r => r.IsChecked && r.IsVisible).Select(r => r.Id).ToList(),
        FitnessFilter.All,
        false,
        Array.Empty<RankCategory>(),
        RankOptions.Where(o => o.IsChecked).Select(o => o.Rank).ToList(),
        ActiveIntake?.Id,
        IncludePermanentStaff);

    private void RefreshRecipientOptions()
    {
        var intakeId = ActiveIntake?.Id;
        RecipientOptions.Clear();
        foreach (var item in _recipientService.Search(null))
        {
            if (!RosterSelection.InScope(item.IntakeId, intakeId, IncludePermanentStaff)) continue;

            var row = new RecipientCheckRowViewModel(item.Id, item.Rank, item.FullName, item.UnitName);
            row.PropertyChanged += OnRecipientOptionPropertyChanged;
            RecipientOptions.Add(row);
        }

        RefreshRankFilters();
        RefreshSelectedRecipientsCount();
    }

    private void OnRecipientOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RecipientCheckRowViewModel.IsChecked) || _batchingRecipientChecks) return;
        RefreshSelectedRecipientsCount();
        RefreshPackageTemplateCounts();
    }

    private bool _batchingRecipientChecks;

    private void BatchRecipientChecks(Action apply)
    {
        _batchingRecipientChecks = true;
        try
        {
            apply();
        }
        finally
        {
            _batchingRecipientChecks = false;
        }

        RefreshSelectedRecipientsCount();
        RefreshPackageTemplateCounts();
    }

    private void RefreshPackageTemplateCounts()
    {
        var selection = BuildRosterSelection();
        foreach (var item in PackageTemplates.Where(t => t.IsXlsx))
            item.PersonCount = _generationService.GetRecipientCount(selection with { FitnessFilter = item.FitnessFilter });
    }

    private void RefreshSelectedRecipientsCount()
    {
        SelectedRecipientsCount = RecipientOptions.Count(r => r.IsChecked && r.IsVisible);
    }

    [RelayCommand]
    private void CheckAllRecipients() => BatchRecipientChecks(() =>
    {
        foreach (var row in RecipientOptions.Where(r => r.IsShown)) row.IsChecked = true;
    });

    [RelayCommand]
    private void UncheckAllRecipients() => BatchRecipientChecks(() =>
    {
        foreach (var row in RecipientOptions) row.IsChecked = false;
    });

    private bool _suppressRankSync;

    public ObservableCollection<RankCategoryChipViewModel> RankCategoryChips { get; } = new();
    public ObservableCollection<RankOptionViewModel> RankOptions { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RankListToggleLabel))]
    private bool isRankListExpanded;

    public string RankListToggleLabel => IsRankListExpanded ? "Окремі звання ▴" : "Окремі звання ▾";

    private void RefreshRankFilters()
    {
        foreach (var option in RankOptions) option.PropertyChanged -= OnRankOptionPropertyChanged;
        foreach (var chip in RankCategoryChips) chip.PropertyChanged -= OnRankCategoryChipPropertyChanged;

        RankOptions.Clear();
        RankCategoryChips.Clear();

        var groups = RecipientOptions
            .GroupBy(r => RankOrder.Normalize(r.Rank))
            .Select(g => new
            {
                DisplayRank = g.First().Rank,
                Count = g.Count(),
                Category = RankOrder.Category(g.First().Rank),
                Seniority = RankOrder.Seniority(g.First().Rank)
            })
            .OrderBy(g => g.Seniority)
            .ThenBy(g => g.DisplayRank, UkrainianCollation.Surname)
            .ToList();

        foreach (var g in groups)
        {
            var option = new RankOptionViewModel(g.DisplayRank, g.Category, g.Count);
            option.PropertyChanged += OnRankOptionPropertyChanged;
            RankOptions.Add(option);
        }

        foreach (var category in new[] { RankCategory.Officers, RankCategory.Sergeants, RankCategory.Soldiers })
        {
            var count = RankOptions.Where(o => o.Category == category).Sum(o => o.Count);
            var chip = new RankCategoryChipViewModel(category, count) { IsChecked = false };
            chip.PropertyChanged += OnRankCategoryChipPropertyChanged;
            RankCategoryChips.Add(chip);
        }
    }

    private void OnRankCategoryChipPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RankCategoryChipViewModel.IsChecked) || _suppressRankSync) return;
        var chip = (RankCategoryChipViewModel)sender!;
        if (chip.IsChecked is not bool value) return;

        if (!value && IsPartiallyChecked(chip.Category))
        {
            chip.IsChecked = true;
            return;
        }

        _suppressRankSync = true;
        try
        {
            foreach (var option in RankOptions.Where(o => o.Category == chip.Category))
                option.IsChecked = value;
        }
        finally
        {
            _suppressRankSync = false;
        }

        ApplyRecipientRankFilter();
    }

    private void OnRankOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RankOptionViewModel.IsChecked)) return;
        if (!_suppressRankSync) SyncCategoryChipState((RankOptionViewModel)sender!);
        ApplyRecipientRankFilter();
    }

    private bool IsPartiallyChecked(RankCategory category)
    {
        var siblings = RankOptions.Where(o => o.Category == category).ToList();
        return siblings.Any(o => o.IsChecked) && !siblings.All(o => o.IsChecked);
    }

    private void SyncCategoryChipState(RankOptionViewModel changed)
    {
        var chip = RankCategoryChips.FirstOrDefault(c => c.Category == changed.Category);
        if (chip is null) return;

        var siblings = RankOptions.Where(o => o.Category == changed.Category).ToList();
        var allChecked = siblings.Count > 0 && siblings.All(o => o.IsChecked);
        var noneChecked = siblings.All(o => !o.IsChecked);

        _suppressRankSync = true;
        try
        {
            chip.IsChecked = allChecked ? true : noneChecked ? false : null;
        }
        finally
        {
            _suppressRankSync = false;
        }
    }

    private void ApplyRecipientRankFilter()
    {
        var checkedRanks = new HashSet<string>(
            RankOptions.Where(o => o.IsChecked).Select(o => RankOrder.Normalize(o.Rank)),
            StringComparer.Ordinal);

        _batchingRecipientChecks = true;
        try
        {
            foreach (var row in RecipientOptions)
                row.IsVisible = checkedRanks.Count == 0 || checkedRanks.Contains(RankOrder.Normalize(row.Rank));
        }
        finally
        {
            _batchingRecipientChecks = false;
        }

        RefreshSelectedRecipientsCount();
        OnPropertyChanged(nameof(SelectedRecipientsCountLabel));
        RefreshAllRecipientsCount();
        RefreshPackageTemplateCounts();
    }

    private void RefreshAllRecipientsCount()
    {
        RecipientCount = _generationService.GetRecipientCount(BuildRosterSelection() with
        {
            AllRecipients = true,
            RecipientIds = Array.Empty<int>()
        });
        RefreshPackageTemplateCounts();
    }

    internal void RefreshPackages()
    {
        var selectedId = SelectedPackage?.Id;
        Packages = new ObservableCollection<GenerationPackageListItemViewModel>(
            _generationService.GetPackages().Select(p => new GenerationPackageListItemViewModel(p.Id, p.Name, p.Description, p.TemplateCount)));

        var current = selectedId is int id ? Packages.FirstOrDefault(p => p.Id == id) : null;
        if (current is not null)
        {
            current.IsSelected = true;
            SelectedPackage = current;
        }
        else if (selectedId is not null)
        {
            ClearPackageSelection();
        }

        RefreshLastRun();
    }

    private void ClearPackageSelection()
    {
        SelectedPackage = null;
        PackageTemplates = new ObservableCollection<PackageTemplateSummaryItemViewModel>();
        ManualTagForm = null;
    }

    [ObservableProperty] private string lastRunPackageName = string.Empty;
    [ObservableProperty] private string lastRunSummary = string.Empty;
    [ObservableProperty] private bool hasLastRun;

    private int? _lastRunPackageId;

    private void RefreshLastRun()
    {
        var last = _generationService.GetLastRun();

        _lastRunPackageId = last?.PackageId;
        HasLastRun = last is not null;
        LastRunPackageName = last?.PackageName ?? string.Empty;
        LastRunSummary = last is null
            ? string.Empty
            : $"{last.RunAt:dd.MM.yyyy HH:mm} · згенеровано {last.GeneratedCount}"
              + (last.ErrorCount > 0 ? $", помилок {last.ErrorCount}" : string.Empty);
    }

    [RelayCommand]
    private Task OpenLastPackageAsync()
    {
        var item = _lastRunPackageId is int id
            ? Packages.FirstOrDefault(p => p.Id == id)
            : null;

        return item is null ? Task.CompletedTask : SelectPackageAsync(item);
    }

    [RelayCommand]
    private async Task SelectPackageAsync(GenerationPackageListItemViewModel? item)
    {
        if (item is null) return;

        await _userSettings.UpdateAsync(s => s.LastPackageId = item.Id);
        await RefreshForPackageAsync(item);
    }

    private async Task RefreshForPackageAsync(GenerationPackageListItemViewModel item)
    {
        IsCreatingPackage = false;

        foreach (var p in Packages) p.IsSelected = false;
        item.IsSelected = true;
        SelectedPackage = item;

        var rosterSelection = BuildRosterSelection();
        var summary = new List<PackageTemplateSummaryItemViewModel>();
        summary.AddRange(_generationService.GetPackageTemplates(item.Id)
            .Select(t => new PackageTemplateSummaryItemViewModel(t.TemplateName, t.Kind)));
        summary.AddRange(_generationService.GetPackageExportTemplates(item.Id)
            .Select(t => new PackageTemplateSummaryItemViewModel(
                t.Name, t.FitnessFilter,
                _generationService.GetRecipientCount(rosterSelection with { FitnessFilter = t.FitnessFilter }))));
        PackageTemplates = new ObservableCollection<PackageTemplateSummaryItemViewModel>(summary);

        var tags = _generationService.GetManualTags(item.Id);
        var needsCourseOfficer = _generationService.PackageNeedsCourseOfficer(item.Id);
        ManualTagForm = tags.Count > 0 || needsCourseOfficer
            ? await _manualTagFormBuilder.BuildAsync(tags, $"pkg:{item.Id}", needsCourseOfficer)
            : null;

        RefreshRecipientOptions();
        RefreshAllRecipientsCount();
        if (string.IsNullOrWhiteSpace(OutputFolder)) await LoadDefaultOutputFolderAsync();
        LastResult = null;
        ProgressText = string.Empty;
    }

    [RelayCommand]
    private void ShowCreatePackage()
    {
        IsCreatingPackage = true;
        NewPackageName = string.Empty;
        NewPackageDescription = string.Empty;
        var checkItems = new List<GenerationTemplateCheckItemViewModel>();
        checkItems.AddRange(_generationService.GetAllTemplates()
            .Select(t => new GenerationTemplateCheckItemViewModel(t.Id, t.Name, TemplateKind.Docx)));
        checkItems.AddRange(_generationService.GetAllExportTemplates()
            .Select(t => new GenerationTemplateCheckItemViewModel(t.Id, t.Name, TemplateKind.Xlsx)));
        TemplateCheckItems = new ObservableCollection<GenerationTemplateCheckItemViewModel>(checkItems);

        foreach (var p in Packages) p.IsSelected = false;
        SelectedPackage = null;
        PackageTemplates = new ObservableCollection<PackageTemplateSummaryItemViewModel>();
        ManualTagForm = null;
    }

    [RelayCommand]
    private void CancelCreatePackage() => IsCreatingPackage = false;

    [RelayCommand]
    private async Task CreatePackageAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPackageName))
        {
            MessageBox.Show("Вкажіть назву пакета.", "Помилка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var templateIds = TemplateCheckItems.Where(t => t.IsChecked && t.Kind == TemplateKind.Docx).Select(t => t.Id).ToList();
        var exportTemplateIds = TemplateCheckItems.Where(t => t.IsChecked && t.Kind == TemplateKind.Xlsx)
            .Select(t => (t.Id, FitnessFilter.All)).ToList();
        if (templateIds.Count == 0 && exportTemplateIds.Count == 0)
        {
            MessageBox.Show("Оберіть хоча б один шаблон.", "Помилка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var createdId = _generationService.CreatePackage(NewPackageName, NewPackageDescription, templateIds, exportTemplateIds);
        IsCreatingPackage = false;

        RefreshPackages();
        var created = Packages.FirstOrDefault(p => p.Id == createdId);
        if (created is not null) await SelectPackageAsync(created);
    }

    [RelayCommand]
    private void DeletePackage(GenerationPackageListItemViewModel? item)
    {
        if (item is null) return;

        var confirm = MessageBox.Show(
            $"Видалити пакет «{item.Name}»?", "Підтвердження видалення",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        DeletePackageCore(item);
    }

    internal void DeletePackageCore(GenerationPackageListItemViewModel item)
    {
        _generationService.DeletePackage(item.Id);

        if (SelectedPackage?.Id == item.Id) ClearPackageSelection();

        RefreshPackages();
    }

    [RelayCommand]
    private async Task OpenRequirementsAsync()
    {
        if (SelectedPackage is null) return;

        var vm = _serviceProvider.GetRequiredService<PackageRequirementsViewModel>();
        await vm.InitializeAsync(SelectedPackage.Id, null);
        if (_dialogService.ShowDialog(vm, Application.Current?.MainWindow) == true)
        {
            _messenger.Send(new MatrixChangedMessage());
            RefreshPackages();
            if (SelectedPackage is not null) await RefreshForPackageAsync(SelectedPackage);
        }
    }

    [RelayCommand]
    private async Task PickOutputFolderAsync()
    {
        var dialog = new OpenFolderDialog { Title = "Оберіть папку для документів" };
        if (dialog.ShowDialog() != true) return;

        OutputFolder = dialog.FolderName;
        await _outputFolderService.SaveDefaultAsync(dialog.FolderName);
    }

    private bool CanGenerateAll() =>
        HasOutputFolder && SelectedPackage is not null && (UseAllRecipients || SelectedRecipientsCount > 0);

    [RelayCommand(CanExecute = nameof(CanGenerateAll))]
    private async Task GenerateAllAsync()
    {
        if (SelectedPackage is null || string.IsNullOrWhiteSpace(OutputFolder)) return;

        var packageId = SelectedPackage.Id;
        var outputFolderPath = OutputFolder;
        var regenerate = RegenerateExisting;
        var manualValues = ManualTagForm?.GetValues() ?? new Dictionary<string, string>();
        ApplyDocumentDate(manualValues, DocumentDate);
        var courseOfficerId = ManualTagForm?.CourseOfficer?.Selected?.RecipientId;
        var progress = new Progress<string>(message => ProgressText = message);
        var rosterSelection = BuildRosterSelection();

        LastResult = null;
        IsBusy = true;
        ProgressText = "Підготовка…";

        try
        {
            var result = await Task.Run(() =>
                _generationService.RunPackage(packageId, outputFolderPath, manualValues, regenerate, rosterSelection, progress, courseOfficerId));

            if (ManualTagForm is not null)
                await _manualTagFormBuilder.SaveAsync($"pkg:{packageId}", ManualTagForm);

            LastResult = new GenerationResultViewModel(result, outputFolderPath);
            if (result.Generated + result.GroupGenerated + result.DocxGroupGenerated > 0)
                _messenger.Send(new MatrixChangedMessage());
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
            RefreshLastRun();
        }
    }

    internal const string DocumentDateTag = ManualTagRowViewModel.DocumentDateTag;

    internal static void ApplyDocumentDate(Dictionary<string, string> manualValues, DateTime? documentDate)
    {
        manualValues[DocumentDateTag] = (documentDate ?? DateTime.Today).ToString("dd.MM.yyyy");
    }
}
