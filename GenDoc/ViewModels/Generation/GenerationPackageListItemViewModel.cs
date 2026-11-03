using System.Collections.ObjectModel;
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

    /// <summary>На картці - лише назва теки; повний шлях видно в підказці.</summary>
    public string? DescriptionDisplay => Services.Generation.PackageFolderOrigin.ShortDisplay(Description);
    public int TemplateCount { get; }
    public string TemplateCountDisplay => $"{TemplateCount} шабл.";

    [ObservableProperty]
    private bool isSelected;

    /// <summary>Картка в режимі редагування: замість назви - поле вводу, шаблони на додавання і «Зберегти»/«Скасувати».</summary>
    [ObservableProperty]
    private bool isEditing;

    [ObservableProperty]
    private string editName = string.Empty;

    /// <summary>Групові DOCX і XLSX, яких ще немає в пакеті; позначені додаються після «Зберегти».</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAddableTemplates))]
    private ObservableCollection<GenerationTemplateCheckItemViewModel> addableTemplates = new();

    public bool HasAddableTemplates => AddableTemplates.Count > 0;
}
