using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Services.Navigation;
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

    public GenerationViewModel(
        IGenerationService generationService,
        IDialogService dialogService,
        IServiceProvider serviceProvider,
        Services.Completeness.ICompletenessService completenessService)
    {
        _generationService = generationService;
        _dialogService = dialogService;
        _serviceProvider = serviceProvider;
        _completenessService = completenessService;
        RefreshPackages();
    }

    public async Task ApplyNavigationPayloadAsync(object payload)
    {
        if (payload is not IntakeNavigationPayload nav) return;

        var packageId = nav.PackageId ?? await _completenessService.GetDefaultPackageIdAsync();
        if (packageId is not int id) return;

        var item = Packages.FirstOrDefault(p => p.Id == id);
        if (item is not null) SelectPackage(item);
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
    private ObservableCollection<ManualTagInputViewModel> manualTagInputs = new();

    public bool HasManualTags => ManualTagInputs.Count > 0;

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
    [NotifyPropertyChangedFor(nameof(NotBusy))]
    private bool isBusy;

    public bool NotBusy => !IsBusy;

    [ObservableProperty]
    private string progressText = string.Empty;

    private void RefreshPackages()
    {
        Packages = new ObservableCollection<GenerationPackageListItemViewModel>(
            _generationService.GetPackages().Select(p => new GenerationPackageListItemViewModel(p.Id, p.Name, p.Description, p.TemplateCount)));
    }

    [RelayCommand]
    private void SelectPackage(GenerationPackageListItemViewModel? item)
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

        ManualTagInputs = new ObservableCollection<ManualTagInputViewModel>(
            _generationService.GetManualTags(item.Id).Select(t => new ManualTagInputViewModel(t)));

        RecipientCount = _generationService.GetRecipientCount();
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
        ManualTagInputs = new ObservableCollection<ManualTagInputViewModel>();
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
            ManualTagInputs = new ObservableCollection<ManualTagInputViewModel>();
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
            SelectPackage(SelectedPackage);
        }
    }

    [RelayCommand]
    private void PickOutputFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Оберіть папку для документів" };
        if (dialog.ShowDialog() != true) return;

        OutputFolder = dialog.FolderName;
    }

    private bool CanGenerateAll() => HasOutputFolder && SelectedPackage is not null;

    [RelayCommand(CanExecute = nameof(CanGenerateAll))]
    private async Task GenerateAllAsync()
    {
        if (SelectedPackage is null || string.IsNullOrWhiteSpace(OutputFolder)) return;

        var packageId = SelectedPackage.Id;
        var outputFolderPath = OutputFolder;
        var regenerate = RegenerateExisting;
        var manualValues = ManualTagInputs.ToDictionary(m => m.Tag, m => m.Value ?? string.Empty);
        var progress = new Progress<string>(message => ProgressText = message);

        IsBusy = true;
        ProgressText = "Підготовка…";

        var result = await Task.Run(() => _generationService.RunPackage(packageId, outputFolderPath, manualValues, regenerate, progress));

        IsBusy = false;
        ProgressText = string.Empty;

        var summary = $"DOCX — згенеровано: {result.Generated}, пропущено: {result.Skipped}, помилок: {result.Errors}";
        if (result.GroupGenerated + result.GroupSkipped + result.GroupErrors > 0)
            summary += $"\nXLSX (відомості) — згенеровано: {result.GroupGenerated}, пропущено: {result.GroupSkipped}, помилок: {result.GroupErrors}";

        MessageBox.Show(summary, "Генерація завершена", MessageBoxButton.OK, MessageBoxImage.Information);

        var openFolder = MessageBox.Show(
            "Відкрити папку з документами?", "Готово", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (openFolder == MessageBoxResult.Yes)
        {
            Process.Start("explorer.exe", outputFolderPath);
        }
    }
}
