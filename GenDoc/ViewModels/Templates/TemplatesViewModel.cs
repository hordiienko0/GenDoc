using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Models.Enums;
using GenDoc.Models.TemplateBuilder;
using GenDoc.Services;
using GenDoc.Services.Templates;
using GenDoc.ViewModels.Shell;
using GenDoc.ViewModels.Templates.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace GenDoc.ViewModels.Templates;

public partial class TemplatesViewModel : ObservableObject, IGuardedSection
{
    private readonly IExportTemplateService _exportTemplateService;
    private readonly ITemplateService _templateService;
    private readonly IServiceProvider _serviceProvider;

    public TemplatesViewModel(
        IExportTemplateService exportTemplateService,
        ITemplateService templateService,
        IServiceProvider serviceProvider)
    {
        _exportTemplateService = exportTemplateService;
        _templateService = templateService;
        _serviceProvider = serviceProvider;
        Refresh();
        RefreshDocxTemplates();
    }

    [ObservableProperty]
    private ObservableCollection<ExportTemplateListItemViewModel> documentExcelTemplates = new();

    [ObservableProperty]
    private ObservableCollection<ExportTemplateListItemViewModel> listExportTemplates = new();

    [ObservableProperty]
    private ObservableCollection<DocxTemplateListItemViewModel> docxTemplates = new();

    [ObservableProperty]
    private ListCollectionView? intakeTemplatesView;

    [ObservableProperty]
    private ListCollectionView? permanentStaffTemplatesView;

    [ObservableProperty]
    private bool hasPermanentStaffTemplates;

    private void Refresh()
    {
        var all = _exportTemplateService.GetTemplateListItems()
            .Select(t => new ExportTemplateListItemViewModel(
                t.Id, t.Name, t.OriginalFileName, t.UploadedAt, t.IsBuiltIn, t.UsesPlaceholders, t.TagCount,
                t.RepeatSheetPerDate, t.IsFromBuilder))
            .ToList();

        DocumentExcelTemplates = new ObservableCollection<ExportTemplateListItemViewModel>(
            all.Where(t => t.UsesPlaceholders));
        ListExportTemplates = new ObservableCollection<ExportTemplateListItemViewModel>(
            all.Where(t => !t.UsesPlaceholders));
    }

    private void RefreshDocxTemplates()
    {
        var items = _templateService.GetTemplateListItems()
            .Select(t => new DocxTemplateListItemViewModel(
                t.Id, t.Name, t.ShortName, t.OriginalFileName, t.UploadedAt, t.TagCount, t.IsFromBuilder, t.Audience))
            .ToList();

        foreach (var item in items) item.AudienceChanged += OnTemplateAudienceChanged;

        DocxTemplates = new ObservableCollection<DocxTemplateListItemViewModel>(items);

        IntakeTemplatesView = TemplateAudienceGroups.Intake(DocxTemplates);
        PermanentStaffTemplatesView = TemplateAudienceGroups.PermanentStaff(DocxTemplates);

        RefreshAudienceGroups();
    }

    private void RefreshAudienceGroups()
    {
        IntakeTemplatesView?.Refresh();
        PermanentStaffTemplatesView?.Refresh();

        HasPermanentStaffTemplates =
            DocxTemplates.Any(t => t.Audience == TemplateAudience.PermanentStaff);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBuilderOpen))]
    [NotifyPropertyChangedFor(nameof(IsListVisible))]
    private TemplateBuilderViewModel? builder;

    public bool IsBuilderOpen => Builder is not null;

    public bool IsListVisible => Builder is null;

    public Task<bool> TryLeaveAsync()
    {
        if (Builder is null) return Task.FromResult(true);
        if (!Builder.TryLeave()) return Task.FromResult(false);

        Builder = null;
        return Task.FromResult(true);
    }

    [RelayCommand]
    private void CreateWithBuilder()
    {
        var builderViewModel = CreateBuilder();
        builderViewModel.StartNew(TemplateBuilderMode.Word);
        Builder = builderViewModel;
    }

    [RelayCommand]
    private void CreateVidomistWithBuilder()
    {
        var builderViewModel = CreateBuilder();
        builderViewModel.StartNew(TemplateBuilderMode.Excel);
        Builder = builderViewModel;
    }

    [RelayCommand]
    private void EditExportWithBuilder(ExportTemplateListItemViewModel? item)
    {
        if (item is null) return;

        var builderViewModel = CreateBuilder();
        if (!builderViewModel.LoadTemplate(item.Id, TemplateBuilderMode.Excel))
        {
            MessageBox.Show(
                "Ця книга завантажена файлом і не має джерела блоків, тож у конструкторі не відкривається.",
                "Немає джерела блоків", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Builder = builderViewModel;
    }

    [RelayCommand]
    private void EditWithBuilder(DocxTemplateListItemViewModel? item)
    {
        if (item is null) return;

        var builderViewModel = CreateBuilder();
        if (!builderViewModel.LoadTemplate(item.Id, TemplateBuilderMode.Word))
        {
            MessageBox.Show(
                "Цей шаблон завантажений файлом і не має джерела блоків, тож у конструкторі не відкривається.",
                "Немає джерела блоків", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Builder = builderViewModel;
    }

    private TemplateBuilderViewModel CreateBuilder()
    {
        var builderViewModel = _serviceProvider.GetRequiredService<TemplateBuilderViewModel>();
        builderViewModel.RequestClose += () => Builder = null;
        builderViewModel.Saved += () =>
        {
            RefreshDocxTemplates();
            Refresh();
        };
        return builderViewModel;
    }

    [RelayCommand]
    private void UploadDocxTemplate() => UploadAnyTemplate();

    private void UploadAnyTemplate()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Шаблони (*.docx;*.xlsx)|*.docx;*.xlsx|Документи Word (*.docx)|*.docx|Excel файли (*.xlsx)|*.xlsx"
        };
        if (dialog.ShowDialog() != true) return;

        var extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
        switch (extension)
        {
            case ".docx":
                var result = _templateService.Upload(dialog.FileName);
                if (!result.Success)
                {
                    MessageBox.Show(result.ErrorMessage, "Помилка завантаження", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                RefreshDocxTemplates();
                break;

            case ".xlsx":
                _exportTemplateService.UploadTemplate(dialog.FileName);
                Refresh();
                break;

            default:
                MessageBox.Show("Підтримуються лише файли .docx та .xlsx.", "Непідтримуваний формат", MessageBoxButton.OK, MessageBoxImage.Error);
                break;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTemplate))]
    private object? selectedTemplate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDocxTemplate))]
    private DocxTemplateListItemViewModel? selectedDocxTemplate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedExportTemplate))]
    private ExportTemplateListItemViewModel? selectedExportTemplate;

    public bool HasSelectedTemplate => SelectedTemplate is not null;

    public bool HasSelectedDocxTemplate => SelectedDocxTemplate is not null;

    public bool HasSelectedExportTemplate => SelectedExportTemplate is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MappingPanelColumnWidth))]
    private double mappingPanelWidth = 430;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MappingPanelColumnWidth))]
    private bool isMappingPanelAutoSized = true;

    public GridLength MappingPanelColumnWidth
        => IsMappingPanelAutoSized ? GridLength.Auto : new GridLength(MappingPanelWidth);

    public const double MappingPanelMinWidth = 320;
    public const double MappingPanelMaxWidth = 900;

    [RelayCommand]
    private void SelectTemplate(object? item)
    {
        switch (item)
        {
            case DocxTemplateListItemViewModel docx:
                EnsureDocxMappingsLoaded(docx);
                break;
            case ExportTemplateListItemViewModel export:
                EnsureExportMappingsLoaded(export);
                break;
            default:
                return;
        }

        foreach (var t in DocxTemplates) t.IsSelected = ReferenceEquals(t, item);
        foreach (var t in DocumentExcelTemplates) t.IsSelected = ReferenceEquals(t, item);
        foreach (var t in ListExportTemplates) t.IsSelected = ReferenceEquals(t, item);

        SelectedTemplate = item;
        SelectedDocxTemplate = item as DocxTemplateListItemViewModel;
        SelectedExportTemplate = item as ExportTemplateListItemViewModel;
    }

    [RelayCommand]
    private void ClearTemplateSelection()
    {
        foreach (var t in DocxTemplates) t.IsSelected = false;
        foreach (var t in DocumentExcelTemplates) t.IsSelected = false;
        foreach (var t in ListExportTemplates) t.IsSelected = false;

        SelectedTemplate = null;
        SelectedDocxTemplate = null;
        SelectedExportTemplate = null;
    }

    private void EnsureDocxMappingsLoaded(DocxTemplateListItemViewModel item)
    {
        if (item.MappingsLoaded) return;

        var mappings = _templateService.GetMappings(item.Id)
            .Select(m => new DocxMappingRowViewModel(m.Id, m.PlaceholderTag, m.SourceType, m.FieldName, m.DateFormat));
        item.Mappings = new ObservableCollection<DocxMappingRowViewModel>(mappings);
        item.MappingsLoaded = true;
    }

    private void EnsureExportMappingsLoaded(ExportTemplateListItemViewModel item)
    {
        if (item.MappingsLoaded) return;

        var mappings = item.UsesPlaceholders
            ? _exportTemplateService.GetMappings(item.Id)
                .Select(m => new TemplateMappingRowViewModel(m.Id, m.PlaceholderTag, m.SourceType, m.FieldKey))
            : _exportTemplateService.GetMappings(item.Id)
                .Select(m => new TemplateMappingRowViewModel(m.ColumnIndex, m.HeaderText, Enum.Parse<ExportFieldKey>(m.FieldKey)));
        item.Mappings = new ObservableCollection<TemplateMappingRowViewModel>(mappings);
        item.MappingsLoaded = true;
    }

    [RelayCommand]
    private void SaveDocxMapping(DocxTemplateListItemViewModel? item)
    {
        if (item is null) return;

        var mappings = item.Mappings.Select(m => (m.Id, m.SourceType, m.SelectedFieldName, m.SelectedDateFormat)).ToList();
        _templateService.SaveMappings(item.Id, mappings);

        MessageBox.Show("Мапінг міток збережено.", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnTemplateAudienceChanged(DocxTemplateListItemViewModel item)
    {
        _templateService.SaveAudience(item.Id, item.Audience);
        RefreshAudienceGroups();
    }

    [RelayCommand]
    private void SaveShortName(DocxTemplateListItemViewModel? item)
    {
        if (item is null) return;
        _templateService.SaveShortName(item.Id, item.ShortNameEdit);
    }

    [RelayCommand]
    private void DeleteDocxTemplate(DocxTemplateListItemViewModel? item)
    {
        if (item is null) return;

        var confirm = MessageBox.Show(
            $"Видалити шаблон «{item.Name}»?",
            "Підтвердження видалення",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        var (success, errorMessage) = _templateService.Delete(item.Id);
        if (!success)
        {
            MessageBox.Show(errorMessage, "Неможливо видалити", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        RefreshDocxTemplates();
    }

    [RelayCommand]
    private void UploadTemplate()
    {
        UploadAnyTemplate();
    }

    [RelayCommand]
    private void SaveMapping(ExportTemplateListItemViewModel? item)
    {
        if (item is null) return;

        if (item.UsesPlaceholders)
        {
            var placeholderMappings = item.Mappings.Select(m => (m.Id, m.SourceType, m.SelectedFieldName)).ToList();
            _exportTemplateService.SavePlaceholderMappings(item.Id, placeholderMappings);
            _exportTemplateService.SetRepeatSheetPerDate(item.Id, item.RepeatSheetPerDate);
        }
        else
        {
            var mappings = item.Mappings.Select(m => (m.ColumnIndex, m.SelectedField.ToString())).ToList();
            _exportTemplateService.SaveMappings(item.Id, mappings);
        }

        MessageBox.Show("Мапінг колонок збережено.", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private void DeleteTemplate(ExportTemplateListItemViewModel? item)
    {
        if (item is null || item.IsBuiltIn) return;

        var result = MessageBox.Show(
            $"Видалити шаблон «{item.Name}»?",
            "Підтвердження видалення",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        _exportTemplateService.Delete(item.Id);
        Refresh();
    }
}
