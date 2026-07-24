using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GenDoc.ViewModels.Templates;

public partial class DocxTemplateListItemViewModel : ObservableObject
{
    public DocxTemplateListItemViewModel(int id, string name, string originalFileName, DateTime uploadedAt, int tagCount)
    {
        Id = id;
        Name = name;
        OriginalFileName = originalFileName;
        UploadedAtDisplay = uploadedAt.ToString("dd.MM.yyyy");
        TagCount = tagCount;
    }

    public int Id { get; }
    public string Name { get; }
    public string OriginalFileName { get; }
    public string UploadedAtDisplay { get; }
    public int TagCount { get; }

    [ObservableProperty]
    private bool isMappingExpanded;

    [ObservableProperty]
    private ObservableCollection<DocxMappingRowViewModel> mappings = new();

    public bool MappingsLoaded { get; set; }
}
