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

    /// <summary>Excel-шаблони з {{тегами}} - вони формують документ, тож показуються
    /// разом із шаблонами Word, а не серед вивантажень списків.</summary>
    [ObservableProperty]
    private ObservableCollection<ExportTemplateListItemViewModel> documentExcelTemplates = new();

    /// <summary>Excel без тегів - заголовок у рядку 1, дані нижче: просте вивантаження списку.</summary>
    [ObservableProperty]
    private ObservableCollection<ExportTemplateListItemViewModel> listExportTemplates = new();

    [ObservableProperty]
    private ObservableCollection<DocxTemplateListItemViewModel> docxTemplates = new();

    /// <summary>
    /// Два ВИДИ над ТІЄЮ САМОЮ колекцією, а не дві окремі колекції: елементи
    /// лишаються тими самими об'єктами, тож вибір, завантажений мапінг і
    /// перемикач аудиторії працюють як раніше. Дві копії списку довелося б
    /// синхронізувати, і рядок губив би стан при переході між групами.
    /// </summary>
    [ObservableProperty]
    private ListCollectionView? intakeTemplatesView;

    [ObservableProperty]
    private ListCollectionView? permanentStaffTemplatesView;

    /// <summary>Група постійного складу ховається, доки жодного такого шаблону
    /// немає: порожній розділ лише додає шуму.</summary>
    [ObservableProperty]
    private bool hasPermanentStaffTemplates;

    private void Refresh()
    {
        var all = _exportTemplateService.GetTemplateListItems()
            .Select(t => new ExportTemplateListItemViewModel(
                t.Id, t.Name, t.OriginalFileName, t.UploadedAt, t.IsBuiltIn, t.UsesPlaceholders, t.TagCount,
                t.RepeatSheetPerDate, t.IsFromBuilder))
            .ToList();

        // Групування за призначенням, а не за розширенням файлу.
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

        // Зміна аудиторії зберігається одразу - окремої кнопки немає, як і в
        // короткої назви поруч.
        foreach (var item in items) item.AudienceChanged += OnTemplateAudienceChanged;

        DocxTemplates = new ObservableCollection<DocxTemplateListItemViewModel>(items);

        IntakeTemplatesView = TemplateAudienceGroups.Intake(DocxTemplates);
        PermanentStaffTemplatesView = TemplateAudienceGroups.PermanentStaff(DocxTemplates);

        RefreshAudienceGroups();
    }

    /// <summary>Перерахувати обидві групи. Викликається й після перемикання
    /// аудиторії - інакше рядок лишався б у старій групі до перезаходу
    /// в розділ, і скидалося б, ніби перемикач не спрацював.</summary>
    private void RefreshAudienceGroups()
    {
        IntakeTemplatesView?.Refresh();
        PermanentStaffTemplatesView?.Refresh();

        HasPermanentStaffTemplates =
            DocxTemplates.Any(t => t.Audience == TemplateAudience.PermanentStaff);
    }

    /// <summary>Конструктор живе всередині «Шаблонів»: не окремий пункт меню, а
    /// повноекранний режим цього ж розділу. Не null - розділ показує конструктор.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBuilderOpen))]
    [NotifyPropertyChangedFor(nameof(IsListVisible))]
    private TemplateBuilderViewModel? builder;

    public bool IsBuilderOpen => Builder is not null;

    public bool IsListVisible => Builder is null;

    /// <summary>Незбережене складання в конструкторі гинуло мовчки при переході
    /// в інший розділ. Питає той самий guard, що й «✕ Закрити» в конструкторі.</summary>
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

    /// <summary>Той самий конструктор, але одразу у режимі відомості: зібраний
    /// .xlsx лягає в ExportTemplate і потрапляє в той самий розділ «Шаблони
    /// документів» (він за тегами, а отже формує документ).</summary>
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
            // Практично недосяжно: кнопка є лише в рядків з BuilderJson. Лишається
            // на випадок зіпсованого JSON - краще сказати, ніж відкрити порожній екран.
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
            // Режим міг перемкнутись уже після відкриття, тож перечитуємо обидва
            // переліки, а не той, з якого зайшли.
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

    /// <summary>Обраний шаблон - його мапінг показує права панель. Типи різні
    /// (Word / Excel), тому object: розкладку добирає типізований DataTemplate у XAML.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTemplate))]
    private object? selectedTemplate;

    /// <summary>Word і Excel описані різними в'ю-моделями з різними редакторами
    /// мапінгу, тож права панель тримає два окремі слоти, а не один нетипізований:
    /// два неявні DataTemplate на один тип XAML не дозволяє.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDocxTemplate))]
    private DocxTemplateListItemViewModel? selectedDocxTemplate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedExportTemplate))]
    private ExportTemplateListItemViewModel? selectedExportTemplate;

    public bool HasSelectedTemplate => SelectedTemplate is not null;

    // ContentControl із заданим ContentTemplate малює шаблон навіть при Content = null
    // (порожні поля й друга кнопка «Зберегти мапінг» над справжньою) - тому слоти
    // ховаємо явно, а не покладаємось на порожній Content.
    public bool HasSelectedDocxTemplate => SelectedDocxTemplate is not null;

    public bool HasSelectedExportTemplate => SelectedExportTemplate is not null;

    /// <summary>Ширина панелі мапінгу. Поки оператор не чіпав роздільник, панель
    /// розсувається сама під свій вміст (Auto) - довгі назви полів і теги інакше
    /// не вміщаються у фіксовану ширину. Щойно її потягнули, ширина стає явною
    /// й більше не стрибає під час перемикання шаблонів.</summary>
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

    /// <summary>Закриває праву панель: без цього вона лишалася б на екрані назавжди
    /// після першого ж кліку на олівець.</summary>
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

    // Не команда, а обробник події рядка: перемикач у списку зберігає одразу.
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
