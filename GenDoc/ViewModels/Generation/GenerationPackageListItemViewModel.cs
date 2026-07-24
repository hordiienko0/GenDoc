using CommunityToolkit.Mvvm.ComponentModel;

namespace GenDoc.ViewModels.Generation;

public partial class GenerationPackageListItemViewModel : ObservableObject
{
    public GenerationPackageListItemViewModel(int id, string name, string? description, int templateCount)
    {
        Id = id;
        Name = name;
        Description = description;
        TemplateCount = templateCount;
    }

    public int Id { get; }
    public string Name { get; }
    public string? Description { get; }
    public int TemplateCount { get; }
    public string TemplateCountDisplay => $"{TemplateCount} шабл.";

    [ObservableProperty]
    private bool isSelected;
}
