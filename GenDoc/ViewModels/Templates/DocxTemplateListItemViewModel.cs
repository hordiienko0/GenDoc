using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GenDoc.ViewModels.Templates;

public partial class DocxTemplateListItemViewModel : ObservableObject
{
    public DocxTemplateListItemViewModel(
        int id, string name, string? shortName, string originalFileName, DateTime uploadedAt, int tagCount,
        bool isFromBuilder = false)
    {
        Id = id;
        Name = name;
        shortNameEdit = shortName ?? string.Empty;
        OriginalFileName = originalFileName;
        UploadedAtDisplay = uploadedAt.ToString("dd.MM.yyyy");
        TagCount = tagCount;
        IsFromBuilder = isFromBuilder;
    }

    public int Id { get; }
    public string Name { get; }
    public string OriginalFileName { get; }
    public string UploadedAtDisplay { get; }
    public int TagCount { get; }

    /// <summary>Шаблон зібраний конструктором — його можна відкрити на редагування
    /// блоками. Завантажений файлом .docx у конструктор не повертається.</summary>
    public bool IsFromBuilder { get; }

    [ObservableProperty]
    private string shortNameEdit;


    /// <summary>Підсвітка рядка в списку ліворуч; мапінг показує права панель.</summary>
    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private ObservableCollection<DocxMappingRowViewModel> mappings = new();

    public bool MappingsLoaded { get; set; }
}
