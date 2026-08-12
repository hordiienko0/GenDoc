using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Templates;
using Microsoft.Win32;

namespace GenDoc.ViewModels.Templates;

public partial class TemplatesViewModel : ObservableObject
{
    private readonly IExportTemplateService _exportTemplateService;
    private readonly ITemplateService _templateService;

    public TemplatesViewModel(IExportTemplateService exportTemplateService, ITemplateService templateService)
    {
        _exportTemplateService = exportTemplateService;
        _templateService = templateService;
        Refresh();
        RefreshDocxTemplates();
    }

    /// <summary>Excel-шаблони з {{тегами}} — вони формують документ, тож показуються
    /// разом із шаблонами Word, а не серед вивантажень списків.</summary>
    [ObservableProperty]
    private ObservableCollection<ExportTemplateListItemViewModel> documentExcelTemplates = new();

    /// <summary>Excel без тегів — заголовок у рядку 1, дані нижче: просте вивантаження списку.</summary>
    [ObservableProperty]
    private ObservableCollection<ExportTemplateListItemViewModel> listExportTemplates = new();

    [ObservableProperty]
    private ObservableCollection<DocxTemplateListItemViewModel> docxTemplates = new();

    private void Refresh()
    {
        var all = _exportTemplateService.GetTemplateListItems()
            .Select(t => new ExportTemplateListItemViewModel(
                t.Id, t.Name, t.OriginalFileName, t.UploadedAt, t.IsBuiltIn, t.UsesPlaceholders, t.TagCount, t.RepeatSheetPerDate))
            .ToList();

        // Групування за призначенням, а не за розширенням файлу.
        DocumentExcelTemplates = new ObservableCollection<ExportTemplateListItemViewModel>(
            all.Where(t => t.UsesPlaceholders));
        ListExportTemplates = new ObservableCollection<ExportTemplateListItemViewModel>(
            all.Where(t => !t.UsesPlaceholders));
    }

    private void RefreshDocxTemplates()
    {
        DocxTemplates = new ObservableCollection<DocxTemplateListItemViewModel>(
            _templateService.GetTemplateListItems()
                .Select(t => new DocxTemplateListItemViewModel(t.Id, t.Name, t.ShortName, t.OriginalFileName, t.UploadedAt, t.TagCount)));
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

    /// <summary>Обраний шаблон — його мапінг показує права панель. Типи різні
    /// (Word / Excel), тому object: розкладку добирає типізований DataTemplate у XAML.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTemplate))]
    private object? selectedTemplate;

    public bool HasSelectedTemplate => SelectedTemplate is not null;

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
    }

    [RelayCommand]
    private void ToggleDocxMapping(DocxTemplateListItemViewModel? item)
    {
        if (item is null) return;

        EnsureDocxMappingsLoaded(item);
        item.IsMappingExpanded = !item.IsMappingExpanded;
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
    private void ToggleMapping(ExportTemplateListItemViewModel? item)
    {
        if (item is null) return;

        EnsureExportMappingsLoaded(item);
        item.IsMappingExpanded = !item.IsMappingExpanded;
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
