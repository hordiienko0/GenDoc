using System.Collections.ObjectModel;
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

    [ObservableProperty]
    private ObservableCollection<ExportTemplateListItemViewModel> templates = new();

    [ObservableProperty]
    private ObservableCollection<DocxTemplateListItemViewModel> docxTemplates = new();

    private void Refresh()
    {
        Templates = new ObservableCollection<ExportTemplateListItemViewModel>(
            _exportTemplateService.GetTemplateListItems()
                .Select(t => new ExportTemplateListItemViewModel(t.Id, t.Name, t.OriginalFileName, t.UploadedAt, t.IsBuiltIn)));
    }

    private void RefreshDocxTemplates()
    {
        DocxTemplates = new ObservableCollection<DocxTemplateListItemViewModel>(
            _templateService.GetTemplateListItems()
                .Select(t => new DocxTemplateListItemViewModel(t.Id, t.Name, t.ShortName, t.OriginalFileName, t.UploadedAt, t.TagCount)));
    }

    [RelayCommand]
    private void UploadDocxTemplate()
    {
        var dialog = new OpenFileDialog { Filter = "Документи Word (*.docx)|*.docx" };
        if (dialog.ShowDialog() != true) return;

        var result = _templateService.Upload(dialog.FileName);
        if (!result.Success)
        {
            MessageBox.Show(result.ErrorMessage, "Помилка завантаження", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        RefreshDocxTemplates();
    }

    [RelayCommand]
    private void ToggleDocxMapping(DocxTemplateListItemViewModel? item)
    {
        if (item is null) return;

        if (!item.MappingsLoaded)
        {
            var mappings = _templateService.GetMappings(item.Id)
                .Select(m => new DocxMappingRowViewModel(m.Id, m.PlaceholderTag, m.SourceType, m.FieldName));
            item.Mappings = new ObservableCollection<DocxMappingRowViewModel>(mappings);
            item.MappingsLoaded = true;
        }

        item.IsMappingExpanded = !item.IsMappingExpanded;
    }

    [RelayCommand]
    private void SaveDocxMapping(DocxTemplateListItemViewModel? item)
    {
        if (item is null) return;

        var mappings = item.Mappings.Select(m => (m.Id, m.SourceType, m.SelectedFieldName)).ToList();
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
        var dialog = new OpenFileDialog { Filter = "Excel файли (*.xlsx)|*.xlsx" };
        if (dialog.ShowDialog() != true) return;

        _exportTemplateService.UploadTemplate(dialog.FileName);
        Refresh();
    }

    [RelayCommand]
    private void ToggleMapping(ExportTemplateListItemViewModel? item)
    {
        if (item is null) return;

        if (!item.MappingsLoaded)
        {
            var mappings = _exportTemplateService.GetMappings(item.Id)
                .Select(m => new TemplateMappingRowViewModel(m.ColumnIndex, m.HeaderText, Enum.Parse<ExportFieldKey>(m.FieldKey)));
            item.Mappings = new ObservableCollection<TemplateMappingRowViewModel>(mappings);
            item.MappingsLoaded = true;
        }

        item.IsMappingExpanded = !item.IsMappingExpanded;
    }

    [RelayCommand]
    private void SaveMapping(ExportTemplateListItemViewModel? item)
    {
        if (item is null) return;

        var mappings = item.Mappings.Select(m => (m.ColumnIndex, m.SelectedField.ToString())).ToList();
        _exportTemplateService.SaveMappings(item.Id, mappings);

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
