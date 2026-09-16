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
        TemplateKind kind = TemplateKind.PerRecipient,
        StaffEventKind? staffEvent = null)
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
        selectedStaffEvent = StaffEventOptions.FirstOrDefault(o => o.Kind == staffEvent) ?? StaffEventOptions[0];
    }

    /// <summary>Подія постійного складу, документи якої формує шаблон; перший пункт - «без події».</summary>
    public static readonly IReadOnlyList<StaffEventOption> StaffEventOptions = new[]
    {
        new StaffEventOption(null, "Без події"),
        new StaffEventOption(StaffEventKind.BusinessTrip, Staff.StaffEventNaming.For(StaffEventKind.BusinessTrip)),
        new StaffEventOption(StaffEventKind.Leave, Staff.StaffEventNaming.For(StaffEventKind.Leave)),
        new StaffEventOption(StaffEventKind.Sick, Staff.StaffEventNaming.For(StaffEventKind.Sick))
    };

    public ObservableCollection<StaffEventOption> StaffEventChoices { get; } = new(StaffEventOptions);

    [ObservableProperty]
    private StaffEventOption selectedStaffEvent;

    public StaffEventKind? StaffEvent => SelectedStaffEvent.Kind;

    public event Action<DocxTemplateListItemViewModel>? StaffEventChanged;

    partial void OnSelectedStaffEventChanged(StaffEventOption value) => StaffEventChanged?.Invoke(this);

    /// <summary>Подія доречна лише для шаблонів постійного складу - наборівські її не мають.</summary>
    public bool ShowsStaffEvent => Audience != TemplateAudience.Intake && Kind == TemplateKind.PerRecipient;

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
    [NotifyPropertyChangedFor(nameof(IsForEveryone))]
    [NotifyPropertyChangedFor(nameof(ShowsStaffEvent))]
    [NotifyPropertyChangedFor(nameof(AudienceCaption))]
    private TemplateAudience audience;

    public bool IsForPermanentStaff
    {
        get => Audience == TemplateAudience.PermanentStaff;
        set => Audience = value ? TemplateAudience.PermanentStaff : TemplateAudience.Intake;
    }

    /// <summary>Шаблон стосується будь-кого — і слухача набору, і постійного складу.</summary>
    public bool IsForEveryone
    {
        get => Audience == TemplateAudience.Everyone;
        set => Audience = value ? TemplateAudience.Everyone : TemplateAudience.Intake;
    }

    public string AudienceCaption => TemplateAudienceCaption.For(Audience);

    public event Action<DocxTemplateListItemViewModel>? AudienceChanged;

    partial void OnAudienceChanged(TemplateAudience value) => AudienceChanged?.Invoke(this);

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private ObservableCollection<DocxMappingRowViewModel> mappings = new();

    public bool MappingsLoaded { get; set; }
}

/// <summary>Пункт вибору події в списку шаблонів (WPF не біндиться до кортежів).</summary>
public record StaffEventOption(StaffEventKind? Kind, string Label);
