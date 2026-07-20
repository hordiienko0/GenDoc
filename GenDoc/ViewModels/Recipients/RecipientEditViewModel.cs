using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Services.Recipients;

namespace GenDoc.ViewModels.Recipients;

public partial class RecipientEditViewModel : ObservableObject
{
    private const int RequiredFieldsTotalCount = 7;

    private readonly IRecipientService _recipientService;
    private int _id;

    public RecipientEditViewModel(IRecipientService recipientService)
    {
        _recipientService = recipientService;
    }

    public bool WasSaved { get; private set; }
    public event EventHandler? RequestClose;

    [ObservableProperty] private string title = "Новий запис";
    [ObservableProperty] private bool canDelete;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequiredFieldsFilledCount))]
    [NotifyPropertyChangedFor(nameof(RequiredFieldsLabel))]
    [NotifyPropertyChangedFor(nameof(RequiredFieldsFraction))]
    [NotifyPropertyChangedFor(nameof(RequiredFieldsRemainingFraction))]
    private string lastName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequiredFieldsFilledCount))]
    [NotifyPropertyChangedFor(nameof(RequiredFieldsLabel))]
    [NotifyPropertyChangedFor(nameof(RequiredFieldsFraction))]
    [NotifyPropertyChangedFor(nameof(RequiredFieldsRemainingFraction))]
    private string firstName = string.Empty;

    [ObservableProperty] private string? middleName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequiredFieldsFilledCount))]
    [NotifyPropertyChangedFor(nameof(RequiredFieldsLabel))]
    [NotifyPropertyChangedFor(nameof(RequiredFieldsFraction))]
    [NotifyPropertyChangedFor(nameof(RequiredFieldsRemainingFraction))]
    private DateTime? dateOfBirth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequiredFieldsFilledCount))]
    [NotifyPropertyChangedFor(nameof(RequiredFieldsLabel))]
    [NotifyPropertyChangedFor(nameof(RequiredFieldsFraction))]
    [NotifyPropertyChangedFor(nameof(RequiredFieldsRemainingFraction))]
    private string rank = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequiredFieldsFilledCount))]
    [NotifyPropertyChangedFor(nameof(RequiredFieldsLabel))]
    [NotifyPropertyChangedFor(nameof(RequiredFieldsFraction))]
    [NotifyPropertyChangedFor(nameof(RequiredFieldsRemainingFraction))]
    private string position = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequiredFieldsFilledCount))]
    [NotifyPropertyChangedFor(nameof(RequiredFieldsLabel))]
    [NotifyPropertyChangedFor(nameof(RequiredFieldsFraction))]
    [NotifyPropertyChangedFor(nameof(RequiredFieldsRemainingFraction))]
    private string? unitName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequiredFieldsFilledCount))]
    [NotifyPropertyChangedFor(nameof(RequiredFieldsLabel))]
    [NotifyPropertyChangedFor(nameof(RequiredFieldsFraction))]
    [NotifyPropertyChangedFor(nameof(RequiredFieldsRemainingFraction))]
    private string serviceNumber = string.Empty;

    [ObservableProperty] private string? roomBuilding;
    [ObservableProperty] private string? roomNumber;

    [ObservableProperty] private string? errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRoomWarning))]
    private string? roomWarningMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasServiceNumberWarning))]
    private string? serviceNumberWarningMessage;

    [ObservableProperty] private ObservableCollection<string> unitNameOptions = new();
    [ObservableProperty] private ObservableCollection<string> roomBuildingOptions = new();
    [ObservableProperty] private ObservableCollection<string> rankOptions = new();
    [ObservableProperty] private ObservableCollection<string> rankSuggestions = new();

    public bool HasRoomWarning => !string.IsNullOrEmpty(RoomWarningMessage);
    public bool HasServiceNumberWarning => !string.IsNullOrEmpty(ServiceNumberWarningMessage);

    public int RequiredFieldsFilledCount => new[]
    {
        !string.IsNullOrWhiteSpace(LastName),
        !string.IsNullOrWhiteSpace(FirstName),
        DateOfBirth is not null,
        !string.IsNullOrWhiteSpace(Rank),
        !string.IsNullOrWhiteSpace(Position),
        !string.IsNullOrWhiteSpace(UnitName),
        !string.IsNullOrWhiteSpace(ServiceNumber),
    }.Count(f => f);

    public int RequiredFieldsTotal => RequiredFieldsTotalCount;
    public string RequiredFieldsLabel => $"{RequiredFieldsFilledCount} з {RequiredFieldsTotal}";
    public double RequiredFieldsFraction => (double)RequiredFieldsFilledCount / RequiredFieldsTotalCount;
    public double RequiredFieldsRemainingFraction => 1.0 - RequiredFieldsFraction;

    public void Initialize(int? recipientId)
    {
        UnitNameOptions = new ObservableCollection<string>(_recipientService.GetUnitNames());
        RoomBuildingOptions = new ObservableCollection<string>(_recipientService.GetRoomBuildings());
        RankOptions = new ObservableCollection<string>(_recipientService.GetRanks());

        ErrorMessage = null;

        if (recipientId is int id)
        {
            var model = _recipientService.GetForEdit(id);
            if (model is null) return;

            _id = model.Id;
            Title = "Редагування запису";
            CanDelete = true;
            LastName = model.LastName;
            FirstName = model.FirstName;
            MiddleName = model.MiddleName;
            Rank = model.Rank;
            Position = model.Position;
            ServiceNumber = model.ServiceNumber;
            DateOfBirth = model.DateOfBirth?.ToDateTime(TimeOnly.MinValue);
            UnitName = model.UnitName;
            RoomBuilding = model.RoomBuilding;
            RoomNumber = model.RoomNumber;
        }
        else
        {
            _id = 0;
            Title = "Новий запис";
            CanDelete = false;
            LastName = string.Empty;
            FirstName = string.Empty;
            MiddleName = null;
            Rank = string.Empty;
            Position = string.Empty;
            ServiceNumber = string.Empty;
            DateOfBirth = null;
            UnitName = null;
            RoomBuilding = null;
            RoomNumber = null;
        }

        UpdateRoomWarning();
        UpdateServiceNumberWarning();
        UpdateRankSuggestions();
    }

    partial void OnRoomBuildingChanged(string? value) => UpdateRoomWarning();
    partial void OnRoomNumberChanged(string? value) => UpdateRoomWarning();
    partial void OnServiceNumberChanged(string value) => UpdateServiceNumberWarning();
    partial void OnRankChanged(string value) => UpdateRankSuggestions();

    private void UpdateRoomWarning()
    {
        var info = _recipientService.GetRoomOccupancy(RoomBuilding, RoomNumber, _id);
        RoomWarningMessage = info is not null && info.OccupantCount >= info.Capacity
            ? $"Кімната {RoomNumber} переповнена: {info.OccupantCount} з {info.Capacity} місць"
            : null;
    }

    private void UpdateServiceNumberWarning()
    {
        var owner = _recipientService.FindDuplicateServiceNumberOwner(ServiceNumber, _id);
        ServiceNumberWarningMessage = owner is null ? null : $"Цей номер вже використовує {owner}";
    }

    private void UpdateRankSuggestions()
    {
        var matches = string.IsNullOrWhiteSpace(Rank)
            ? RankOptions.Take(3)
            : RankOptions.Where(r => r.Contains(Rank, StringComparison.OrdinalIgnoreCase)).Take(3);

        RankSuggestions = new ObservableCollection<string>(matches);
    }

    [RelayCommand]
    private void SelectRankSuggestion(string? suggestion)
    {
        if (suggestion is null) return;
        Rank = suggestion;
    }

    [RelayCommand]
    private void Save()
    {
        if (!TrySave()) return;

        WasSaved = true;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SaveAndAddAnother()
    {
        if (!TrySave()) return;

        WasSaved = true;
        Initialize(null);
    }

    [RelayCommand]
    private void Cancel()
    {
        WasSaved = false;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Delete()
    {
        if (_id == 0) return;

        var result = MessageBox.Show(
            $"Видалити «{LastName} {FirstName}» до кошика?",
            "Підтвердження видалення",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        _recipientService.Delete(_id);
        WasSaved = true;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    private bool TrySave()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(LastName) || string.IsNullOrWhiteSpace(FirstName) ||
            DateOfBirth is null ||
            string.IsNullOrWhiteSpace(Rank) || string.IsNullOrWhiteSpace(Position) ||
            string.IsNullOrWhiteSpace(UnitName) || string.IsNullOrWhiteSpace(ServiceNumber))
        {
            ErrorMessage = "Заповніть усі обов'язкові поля (позначені *).";
            return false;
        }

        _recipientService.Save(new RecipientEditModel
        {
            Id = _id,
            LastName = LastName,
            FirstName = FirstName,
            MiddleName = MiddleName,
            Rank = Rank,
            Position = Position,
            ServiceNumber = ServiceNumber,
            DateOfBirth = DateOfBirth.HasValue ? DateOnly.FromDateTime(DateOfBirth.Value) : null,
            UnitName = UnitName,
            RoomBuilding = RoomBuilding,
            RoomNumber = RoomNumber,
        }, out var saveError);

        if (saveError is not null)
        {
            ErrorMessage = saveError;
            return false;
        }

        return true;
    }
}
