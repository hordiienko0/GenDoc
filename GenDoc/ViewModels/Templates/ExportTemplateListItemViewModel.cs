using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GenDoc.ViewModels.Templates;

public partial class ExportTemplateListItemViewModel : ObservableObject
{
    public ExportTemplateListItemViewModel(int id, string name, string originalFileName, DateTime uploadedAt, bool isBuiltIn)
    {
        Id = id;
        Name = name;
        OriginalFileName = originalFileName;
        UploadedAtDisplay = uploadedAt.ToString("dd.MM.yyyy");
        IsBuiltIn = isBuiltIn;
    }

    public int Id { get; }
    public string Name { get; }
    public string OriginalFileName { get; }
    public string UploadedAtDisplay { get; }
    public bool IsBuiltIn { get; }
    public bool CanDelete => !IsBuiltIn;

    [ObservableProperty]
    private bool isMappingExpanded;

    [ObservableProperty]
    private ObservableCollection<TemplateMappingRowViewModel> mappings = new();

    public bool MappingsLoaded { get; set; }
}
