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

    public GenerationViewModel(
        IGenerationService generationService,
        IDialogService dialogService,
        IServiceProvider serviceProvider,
        Services.Completeness.ICompletenessService completenessService,
        IRecipientService recipientService,
        IManualTagFormBuilder manualTagFormBuilder)
    {
        _generationService = generationService;
        _dialogService = dialogService;
        _serviceProvider = serviceProvider;
        _completenessService = completenessService;
        _recipientService = recipientService;
        _manualTagFormBuilder = manualTagFormBuilder;
        RefreshPackages();
        RefreshRecipientOptions();
    }

    public async Task ApplyNavigationPayloadAsync(object payload)
    {
        if (payload is not IntakeNavigationPayload nav) return;

        var packageId = nav.PackageId ?? await _completenessService.GetDefaultPackageIdAsync();
        if (packageId is not int id) return;

        var item = Packages.FirstOrDefault(p => p.Id == id);
        if (item is not null) await SelectPackageAsync(item);
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
    private int recipientCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasManualTags))]
    private ManualTagFormViewModel? manualTagForm;

    public bool HasManualTags => ManualTagForm?.HasContent == true;

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

    [ObservableProperty]
    private bool regenerateExisting;

    [ObservableProperty]
    private DateTime? documentDate = DateTime.Today;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotBusy))]
    private bool isBusy;

    public bool NotBusy => !IsBusy;

    [ObservableProperty]
    private string progressText = string.Empty;

    // ── Особовий склад: весь / позначені ────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseSelectedRecipients))]
    [NotifyPropertyChangedFor(nameof(ShowNoRecipientsSelectedHint))]
    [NotifyCanExecuteChangedFor(nameof(GenerateAllCommand))]
    private bool useAllRecipients = true;

    public bool UseSelectedRecipients => !UseAllRecipients;

    public ObservableCollection<RecipientCheckRowViewModel> RecipientOptions { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedRecipientsCountLabel))]
    [NotifyPropertyChangedFor(nameof(ShowNoRecipientsSelectedHint))]
    [NotifyCanExecuteChangedFor(nameof(GenerateAllCommand))]
    private int selectedRecipientsCount;

    public string SelectedRecipientsCountLabel => $"Обрано {SelectedRecipientsCount} з {RecipientOptions.Count(r => r.IsVisible)}";
    public bool ShowNoRecipientsSelectedHint => UseSelectedRecipients && SelectedRecipientsCount == 0;

    private void RefreshRecipientOptions()
    {
        RecipientOptions.Clear();
        foreach (var item in _recipientService.Search(null))
        {
            var row = new RecipientCheckRowViewModel(item.Id, item.Rank, item.FullName, item.UnitName);
            row.PropertyChanged += OnRecipientOptionPropertyChanged;
            RecipientOptions.Add(row);
        }

        RefreshRankFilters();
        RefreshSelectedRecipientsCount();
    }

    private void OnRecipientOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RecipientCheckRowViewModel.IsChecked)) RefreshSelectedRecipientsCount();
    }

    private void RefreshSelectedRecipientsCount()
    {
        SelectedRecipientsCount = RecipientOptions.Count(r => r.IsChecked && r.IsVisible);
    }

    [RelayCommand]
    private void CheckAllRecipients()
    {
        foreach (var row in RecipientOptions.Where(r => r.IsVisible)) row.IsChecked = true;
    }

    [RelayCommand]
    private void UncheckAllRecipients()
    {
        foreach (var row in RecipientOptions) row.IsChecked = false;
    }

    // ── Фільтр звань ─────────────────────────────────────────────────────

    private bool _suppressRankSync;

    public ObservableCollection<RankCategoryChipViewModel> RankCategoryChips { get; } = new();
    public ObservableCollection<RankOptionViewModel> RankOptions { get; } = new();

    [ObservableProperty]
    private bool isRankListExpanded;

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
        if (chip.IsChecked is not bool value) return; // програмний перехід у невизначений стан — не каскадувати

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

    // Звужує чекбокс-список одержувачів до обраних звань (порожній вибір — без
    // обмеження); відфільтровані ховаються і знімаються з позначення, щоб оператор
    // ніколи не згенерував звіт на когось, кого не бачить у списку.
    private void ApplyRecipientRankFilter()
    {
        var checkedRanks = new HashSet<string>(
            RankOptions.Where(o => o.IsChecked).Select(o => RankOrder.Normalize(o.Rank)),
            StringComparer.Ordinal);

        foreach (var row in RecipientOptions)
        {
            var visible = checkedRanks.Count == 0 || checkedRanks.Contains(RankOrder.Normalize(row.Rank));
            row.IsVisible = visible;
            if (!visible) row.IsChecked = false;
        }

        RefreshSelectedRecipientsCount();
        OnPropertyChanged(nameof(SelectedRecipientsCountLabel));
    }

    private void RefreshPackages()
    {
        Packages = new ObservableCollection<GenerationPackageListItemViewModel>(
            _generationService.GetPackages().Select(p => new GenerationPackageListItemViewModel(p.Id, p.Name, p.Description, p.TemplateCount)));
    }

    [RelayCommand]
    private async Task SelectPackageAsync(GenerationPackageListItemViewModel? item)
    {
        if (item is null) return;

        IsCreatingPackage = false;

        foreach (var p in Packages) p.IsSelected = false;
        item.IsSelected = true;
        SelectedPackage = item;

        var summary = new List<PackageTemplateSummaryItemViewModel>();
        summary.AddRange(_generationService.GetPackageTemplates(item.Id)
            .Select(t => new PackageTemplateSummaryItemViewModel(t.TemplateName)));
        summary.AddRange(_generationService.GetPackageExportTemplates(item.Id)
            .Select(t => new PackageTemplateSummaryItemViewModel(
                t.Name, t.FitnessFilter, _generationService.GetRecipientCount(t.FitnessFilter))));
        PackageTemplates = new ObservableCollection<PackageTemplateSummaryItemViewModel>(summary);

        var tags = _generationService.GetManualTags(item.Id);
        ManualTagForm = tags.Count > 0
            ? await _manualTagFormBuilder.BuildAsync(tags, $"pkg:{item.Id}")
            : null;

        RecipientCount = _generationService.GetRecipientCount();
        RefreshRecipientOptions();
        OutputFolder = null;
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
    private void CreatePackage()
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

        _generationService.CreatePackage(NewPackageName, NewPackageDescription, templateIds, exportTemplateIds);
        IsCreatingPackage = false;

        RefreshPackages();
    }

    [RelayCommand]
    private void DeletePackage(GenerationPackageListItemViewModel? item)
    {
        if (item is null) return;

        var confirm = MessageBox.Show(
            $"Видалити пакет «{item.Name}»?", "Підтвердження видалення",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        _generationService.DeletePackage(item.Id);

        if (SelectedPackage?.Id == item.Id)
        {
            SelectedPackage = null;
            PackageTemplates = new ObservableCollection<PackageTemplateSummaryItemViewModel>();
            ManualTagForm = null;
        }

        RefreshPackages();
    }

    [RelayCommand]
    private async Task OpenRequirementsAsync()
    {
        if (SelectedPackage is null) return;

        var vm = _serviceProvider.GetRequiredService<PackageRequirementsViewModel>();
        await vm.InitializeAsync(SelectedPackage.Id, null);
        if (_dialogService.ShowDialog(vm, Application.Current.MainWindow) == true)
        {
            WeakReferenceMessenger.Default.Send(new MatrixChangedMessage());
            await SelectPackageAsync(SelectedPackage);
        }
    }

    [RelayCommand]
    private void PickOutputFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Оберіть папку для документів" };
        if (dialog.ShowDialog() != true) return;

        OutputFolder = dialog.FolderName;
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
        var progress = new Progress<string>(message => ProgressText = message);

        // Фільтр звань — орthogonal до вибору "весь склад / позначені": звужує
        // обидва варіанти однаково, звідси й порожні RankCategories (леф-рівень
        // Ranks уже покриває будь-яку комбінацію обраних категорій/окремих звань).
        var checkedRanks = RankOptions.Where(o => o.IsChecked).Select(o => o.Rank).ToList();

        var rosterSelection = new RosterSelection(
            UseAllRecipients,
            UseAllRecipients ? Array.Empty<int>() : RecipientOptions.Where(r => r.IsChecked && r.IsVisible).Select(r => r.Id).ToList(),
            FitnessFilter.All,
            false,
            Array.Empty<RankCategory>(),
            checkedRanks);

        IsBusy = true;
        ProgressText = "Підготовка…";

        var result = await Task.Run(() =>
            _generationService.RunPackage(packageId, outputFolderPath, manualValues, regenerate, rosterSelection, progress));

        if (ManualTagForm is not null)
            await _manualTagFormBuilder.SaveAsync($"pkg:{packageId}", ManualTagForm);

        IsBusy = false;
        ProgressText = string.Empty;

        var summary = $"DOCX — згенеровано: {result.Generated}, пропущено: {result.Skipped}, помилок: {result.Errors}";
        if (result.GroupGenerated + result.GroupSkipped + result.GroupErrors > 0)
            summary += $"\nXLSX (відомості) — згенеровано: {result.GroupGenerated}, пропущено: {result.GroupSkipped}, помилок: {result.GroupErrors}";
        if (result.DocxGroupGenerated + result.DocxGroupSkipped + result.DocxGroupErrors > 0)
            summary += $"\nГруповий DOCX — згенеровано: {result.DocxGroupGenerated}, пропущено: {result.DocxGroupSkipped}, помилок: {result.DocxGroupErrors}";

        MessageBox.Show(summary, "Генерація завершена", MessageBoxButton.OK, MessageBoxImage.Information);

        var openFolder = MessageBox.Show(
            "Відкрити папку з документами?", "Готово", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (openFolder == MessageBoxResult.Yes)
        {
            Process.Start("explorer.exe", outputFolderPath);
        }
    }

    // Тег "дата" зарезервований під це поле — підставляється в кожен документ
    // пакета незалежно від шаблону; якщо шаблон явно мапить {{дата}} вручну,
    // це поле є єдиним джерелом значення (перекриває будь-який попередній запис).
    internal static void ApplyDocumentDate(Dictionary<string, string> manualValues, DateTime? documentDate)
    {
        manualValues["дата"] = (documentDate ?? DateTime.Today).ToString("dd.MM.yyyy");
    }
}
