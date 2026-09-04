using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GenDoc.Models.Enums;

namespace GenDoc.ViewModels.Templates;

public partial class DocxTemplateListItemViewModel : ObservableObject
{
    public DocxTemplateListItemViewModel(
        int id, string name, string? shortName, string originalFileName, DateTime uploadedAt, int tagCount,
        bool isFromBuilder = false,
        TemplateAudience audience = TemplateAudience.Intake,
        TemplateKind kind = TemplateKind.PerRecipient)
    {
        Id = id;
        Name = name;
        shortNameEdit = shortName ?? string.Empty;
        OriginalFileName = originalFileName;
        UploadedAtDisplay = uploadedAt.ToString("dd.MM.yyyy");
        TagCount = tagCount;
        IsFromBuilder = isFromBuilder;
        this.audience = audience;
        Kind = kind;
    }

    public int Id { get; }
    public string Name { get; }
    public string OriginalFileName { get; }
    public string UploadedAtDisplay { get; }
    public int TagCount { get; }

    public bool IsFromBuilder { get; }

    public TemplateKind Kind { get; }

    public bool IsGroup => Kind == TemplateKind.Group;

    public string KindBadge => IsGroup ? "Груповий" : "На кожного";

    [ObservableProperty]
    private string shortNameEdit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsForPermanentStaff))]
    [NotifyPropertyChangedFor(nameof(AudienceCaption))]
    private TemplateAudience audience;

    public bool IsForPermanentStaff
    {
        get => Audience == TemplateAudience.PermanentStaff;
        set => Audience = value ? TemplateAudience.PermanentStaff : TemplateAudience.Intake;
    }

    public string AudienceCaption => Audience == TemplateAudience.PermanentStaff
        ? "Постійний склад"
        : "Набори";

    public event Action<DocxTemplateListItemViewModel>? AudienceChanged;

    partial void OnAudienceChanged(TemplateAudience value) => AudienceChanged?.Invoke(this);

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private ObservableCollection<DocxMappingRowViewModel> mappings = new();

    public bool MappingsLoaded { get; set; }
}
