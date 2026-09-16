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
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public bool IsFromFolder => Services.Generation.PackageFolderOrigin.IsFromFolder(Description);
    public int TemplateCount { get; }
    public string TemplateCountDisplay => $"{TemplateCount} шабл.";

    [ObservableProperty]
    private bool isSelected;

    /// <summary>Картка в режимі перейменування: замість назви - поле вводу з «Зберегти»/«Скасувати».</summary>
    [ObservableProperty]
    private bool isRenaming;

    [ObservableProperty]
    private string editName = string.Empty;
}
