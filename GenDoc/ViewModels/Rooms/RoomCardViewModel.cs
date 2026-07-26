using CommunityToolkit.Mvvm.ComponentModel;
using GenDoc.Services.Rooms;

namespace GenDoc.ViewModels.Rooms;

public partial class RoomCardViewModel : ObservableObject
{
    private const int MaxDisplayedNames = 3;

    private List<RoomOccupantSummary> _occupants;

    public RoomCardViewModel(RoomOverview overview)
    {
        Id = overview.Id;
        building = overview.Building;
        number = overview.Number;
        capacity = overview.Capacity;
        note = overview.Note;
        _occupants = new List<RoomOccupantSummary>(overview.Occupants);
    }

    public static RoomCardViewModel CreateNew()
    {
        var card = new RoomCardViewModel(new RoomOverview(0, string.Empty, string.Empty, 1, null, new List<RoomOccupantSummary>()));
        card.IsNew = true;
        card.BeginEdit();
        return card;
    }

    public int Id { get; private set; }

    // Збережені (committed) значення — використовуються для показу в режимі перегляду.
    [ObservableProperty]
    private string building = string.Empty;

    [ObservableProperty]
    private string number = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OccupancyText))]
    [NotifyPropertyChangedFor(nameof(IsOverCapacity))]
    private int capacity = 1;

    [ObservableProperty]
    private string? note;

    // Буфери вводу для inline-редагування — не чіпають committed-значення, поки не Commit().
    [ObservableProperty]
    private string buildingInput = string.Empty;

    [ObservableProperty]
    private string numberInput = string.Empty;

    [ObservableProperty]
    private string capacityInput = "1";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEditing))]
    private bool isEditing;

    public bool IsNotEditing => !IsEditing;

    [ObservableProperty]
    private bool isNew;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    private string? validationError;

    public bool HasValidationError => !string.IsNullOrEmpty(ValidationError);

    public IReadOnlyList<string> OccupantNames => _occupants.Select(o => o.ShortName).ToList();
    public IReadOnlyList<string> DisplayedOccupantNames => OccupantNames.Take(MaxDisplayedNames).ToList();
    public int OccupantCount => _occupants.Count;
    public int ExtraOccupantCount => Math.Max(0, OccupantCount - MaxDisplayedNames);
    public bool HasExtraOccupants => ExtraOccupantCount > 0;
    public bool IsEmpty => OccupantCount == 0;
    public bool IsOverCapacity => OccupantCount > Capacity;
    public string OccupancyText => $"{OccupantCount} / {Capacity}";

    public void BeginEdit()
    {
        BuildingInput = Building;
        NumberInput = Number;
        CapacityInput = Capacity.ToString();
        ValidationError = null;
        IsEditing = true;
    }

    public void Rollback()
    {
        ValidationError = null;
        IsEditing = false;
    }

    // Викликається після поселення/виселення в панелі деталей — оновлює лічильники
    // й бейджі картки без повного перезавантаження екрана з бази.
    public void SetOccupants(List<RoomOccupantSummary> occupants)
    {
        _occupants = occupants;
        OnPropertyChanged(nameof(OccupantNames));
        OnPropertyChanged(nameof(DisplayedOccupantNames));
        OnPropertyChanged(nameof(OccupantCount));
        OnPropertyChanged(nameof(ExtraOccupantCount));
        OnPropertyChanged(nameof(HasExtraOccupants));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsOverCapacity));
        OnPropertyChanged(nameof(OccupancyText));
    }
}
