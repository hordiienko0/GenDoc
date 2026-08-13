using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GenDoc.ViewModels.Templates;

public partial class ExportTemplateListItemViewModel : ObservableObject
{
    public ExportTemplateListItemViewModel(
        int id, string name, string originalFileName, DateTime uploadedAt, bool isBuiltIn, bool usesPlaceholders,
        int tagCount, bool repeatSheetPerDate, bool isFromBuilder = false)
    {
        IsFromBuilder = isFromBuilder;
        Id = id;
        Name = name;
        OriginalFileName = originalFileName;
        UploadedAtDisplay = uploadedAt.ToString("dd.MM.yyyy");
        IsBuiltIn = isBuiltIn;
        UsesPlaceholders = usesPlaceholders;
        TagCount = tagCount;
        this.repeatSheetPerDate = repeatSheetPerDate;
    }

    public int Id { get; }
    public string Name { get; }
    public string OriginalFileName { get; }
    public string UploadedAtDisplay { get; }
    public bool IsBuiltIn { get; }
    public bool UsesPlaceholders { get; }
    public int TagCount { get; }
    public bool CanDelete => !IsBuiltIn;

    /// <summary>Відомість зібрана конструктором — її можна відкрити на редагування
    /// блоками. Завантажена книга назад у блоки не розбирається.</summary>
    public bool IsFromBuilder { get; }

    // Лише для книг-за-тегами: перший аркуш клонується по одному на кожну
    // дату з ручного тега {{період}}. Зберігається разом з мапінгом (кнопка
    // «Зберегти мапінг») — легкий вибір, без окремої кнопки.
    [ObservableProperty]
    private bool repeatSheetPerDate;


    /// <summary>Підсвітка рядка в списку ліворуч; мапінг показує права панель.</summary>
    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private ObservableCollection<TemplateMappingRowViewModel> mappings = new();

    public bool MappingsLoaded { get; set; }
}
