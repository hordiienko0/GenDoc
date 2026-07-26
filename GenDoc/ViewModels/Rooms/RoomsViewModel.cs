using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Services;
using GenDoc.Services.Rooms;

namespace GenDoc.ViewModels.Rooms;

public partial class RoomsViewModel : ObservableObject
{
    private readonly IRoomService _roomService;
    private List<RoomCardViewModel> _allRooms = new();

    public RoomsViewModel(IRoomService roomService)
    {
        _roomService = roomService;
        Load();
    }

    [ObservableProperty]
    private ObservableCollection<RoomCardViewModel> rooms = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearchEmpty))]
    private string? searchText;

    public bool IsSearchEmpty => string.IsNullOrEmpty(SearchText);

    [ObservableProperty]
    private bool isLoading;

    // --- Панель деталей кімнати ---

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPanelOpen))]
    [NotifyPropertyChangedFor(nameof(SelectedRoomTitle))]
    [NotifyPropertyChangedFor(nameof(SelectedRoomOccupancyLabel))]
    private RoomCardViewModel? selectedRoom;

    public bool IsPanelOpen => SelectedRoom is not null;

    public string SelectedRoomTitle
        => SelectedRoom is null ? string.Empty : $"{SelectedRoom.Building}, кімната №{SelectedRoom.Number}";

    public string SelectedRoomOccupancyLabel
        => SelectedRoom is null ? string.Empty : $"{SelectedRoom.OccupantCount} з {SelectedRoom.Capacity} місць зайнято";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOccupantsEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowOccupantsList))]
    private ObservableCollection<RoomOccupantViewModel> occupants = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOccupantsEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowOccupantsList))]
    private bool isOccupantsLoading;

    public bool ShowOccupantsEmpty => !IsOccupantsLoading && Occupants.Count == 0;
    public bool ShowOccupantsList => !IsOccupantsLoading && Occupants.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotAssigning))]
    private bool isAssigning;

    public bool IsNotAssigning => !IsAssigning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAssignSearchEmpty))]
    private string assignSearchText = string.Empty;

    public bool IsAssignSearchEmpty => string.IsNullOrEmpty(AssignSearchText);

    [ObservableProperty]
    private ObservableCollection<UnassignedPersonItem> assignResults = new();

    public string OccupancyLabel
    {
        get
        {
            var roomsCount = _allRooms.Count;
            var occupiedSeats = _allRooms.Sum(r => r.OccupantCount);
            var totalSeats = _allRooms.Sum(r => r.Capacity);

            var roomsWord = PluralHelper.Pluralize(roomsCount, "кімната", "кімнати", "кімнат");
            var seatsWord = PluralHelper.Pluralize(totalSeats, "місце", "місця", "місць");

            return $"{roomsCount} {roomsWord} · {occupiedSeats} з {totalSeats} {seatsWord}";
        }
    }

    public int OverCapacityCount => _allRooms.Count(r => r.IsOverCapacity);
    public bool HasOverCapacityWarning => OverCapacityCount > 0;

    public bool ShowEmptyNoRooms => _allRooms.Count == 0 && Rooms.Count == 0;
    public bool ShowEmptySearch => _allRooms.Count > 0 && Rooms.Count == 0;

    partial void OnSearchTextChanged(string? value) => ApplyFilter();

    partial void OnAssignSearchTextChanged(string value) => RunAssignSearch();

    private void Load()
    {
        CloseRoom();

        IsLoading = true;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            _allRooms = _roomService.GetAll().Select(r => new RoomCardViewModel(r)).ToList();
            ApplyFilter();
        }
        finally
        {
            Mouse.OverrideCursor = null;
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        IEnumerable<RoomCardViewModel> filtered = _allRooms;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var text = SearchText.Trim();
            filtered = _allRooms.Where(r =>
                r.Building.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                r.Number.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                r.OccupantNames.Any(n => n.Contains(text, StringComparison.OrdinalIgnoreCase)));
        }

        Rooms = new ObservableCollection<RoomCardViewModel>(filtered);
        NotifyComputedProperties();
    }

    private void NotifyComputedProperties()
    {
        OnPropertyChanged(nameof(OccupancyLabel));
        OnPropertyChanged(nameof(OverCapacityCount));
        OnPropertyChanged(nameof(HasOverCapacityWarning));
        OnPropertyChanged(nameof(ShowEmptyNoRooms));
        OnPropertyChanged(nameof(ShowEmptySearch));
        OnPropertyChanged(nameof(SelectedRoomTitle));
        OnPropertyChanged(nameof(SelectedRoomOccupancyLabel));
    }

    [RelayCommand]
    private void AddRoom()
    {
        var card = RoomCardViewModel.CreateNew();
        Rooms.Insert(0, card);
        NotifyComputedProperties();
    }

    [RelayCommand]
    private void EditRoom(RoomCardViewModel? card)
    {
        card?.BeginEdit();
    }

    [RelayCommand]
    private void SaveRoom(RoomCardViewModel? card)
    {
        if (card is null) return;

        var building = card.BuildingInput.Trim();
        var number = card.NumberInput.Trim();

        if (string.IsNullOrWhiteSpace(building) || string.IsNullOrWhiteSpace(number))
        {
            card.ValidationError = "Вкажіть корпус і номер кімнати.";
            return;
        }

        if (!int.TryParse(card.CapacityInput, out var capacity) || capacity <= 0)
        {
            card.ValidationError = "Кількість місць має бути цілим числом більше нуля.";
            return;
        }

        var duplicate = _allRooms.Any(r => r.Id != card.Id &&
            string.Equals(r.Building, building, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.Number, number, StringComparison.OrdinalIgnoreCase));
        if (duplicate)
        {
            card.ValidationError = $"Кімната «{building} №{number}» вже існує.";
            return;
        }

        var result = card.IsNew
            ? _roomService.Create(building, number, capacity, null)
            : _roomService.Update(card.Id, building, number, capacity, card.Note);

        if (!result.Success)
        {
            card.ValidationError = result.ErrorMessage;
            return;
        }

        Load();
    }

    [RelayCommand]
    private void CancelEdit(RoomCardViewModel? card)
    {
        if (card is null) return;

        if (card.IsNew)
        {
            Rooms.Remove(card);
            NotifyComputedProperties();
        }
        else
        {
            card.Rollback();
        }
    }

    [RelayCommand]
    private void DeleteRoom(RoomCardViewModel? card)
    {
        if (card is null) return;

        if (card.IsNew)
        {
            Rooms.Remove(card);
            NotifyComputedProperties();
            return;
        }

        var message = card.OccupantCount > 0
            ? $"У кімнаті {card.Building} №{card.Number} розміщено {card.OccupantCount} осіб. Видалити кімнату і зняти їх з розміщення?"
            : $"Видалити кімнату {card.Building} №{card.Number}?";

        var confirm = MessageBox.Show(message, "Підтвердження видалення", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        _roomService.Delete(card.Id);
        Load();
    }

    [RelayCommand]
    private async Task OpenRoomAsync(RoomCardViewModel? card)
    {
        if (card is null || card.IsNew || card.IsEditing) return;

        if (SelectedRoom == card)
        {
            CloseRoom();
            return;
        }

        if (SelectedRoom is not null) SelectedRoom.IsSelected = false;

        SelectedRoom = card;
        card.IsSelected = true;
        IsAssigning = false;
        AssignSearchText = string.Empty;
        AssignResults = new ObservableCollection<UnassignedPersonItem>();

        IsOccupantsLoading = true;
        Occupants = new ObservableCollection<RoomOccupantViewModel>();
        try
        {
            var roomId = card.Id;
            var rows = await Task.Run(() => _roomService.GetOccupants(roomId));

            if (SelectedRoom != card) return; // користувач встиг відкрити іншу кімнату/закрити панель

            Occupants = BuildOccupantRows(rows);
        }
        finally
        {
            IsOccupantsLoading = false;
        }
    }

    [RelayCommand]
    private void CloseRoom()
    {
        if (SelectedRoom is not null) SelectedRoom.IsSelected = false;
        SelectedRoom = null;
        Occupants = new ObservableCollection<RoomOccupantViewModel>();
        IsAssigning = false;
        AssignSearchText = string.Empty;
        AssignResults = new ObservableCollection<UnassignedPersonItem>();
    }

    [RelayCommand]
    private void ShowAssign()
    {
        if (SelectedRoom is null) return;
        IsAssigning = true;
        AssignSearchText = string.Empty;
        RunAssignSearch();
    }

    [RelayCommand]
    private void CancelAssign()
    {
        IsAssigning = false;
        AssignSearchText = string.Empty;
        AssignResults = new ObservableCollection<UnassignedPersonItem>();
    }

    private void RunAssignSearch()
    {
        if (!IsAssigning) return;

        var results = _roomService.SearchUnassigned(AssignSearchText, 8);
        AssignResults = new ObservableCollection<UnassignedPersonItem>(
            results.Select(r => new UnassignedPersonItem(r.Id, r.FullName, r.Rank, r.Unit)));
    }

    [RelayCommand]
    private void AssignPerson(UnassignedPersonItem? person)
    {
        if (person is null || SelectedRoom is null) return;
        var room = SelectedRoom;

        if (room.OccupantCount >= room.Capacity)
        {
            var confirm = MessageBox.Show(
                $"У кімнаті {room.Building} №{room.Number} немає вільних місць ({room.OccupantCount} з {room.Capacity}). Поселити понад ліміт?",
                "Підтвердження", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;
        }

        _roomService.AssignOccupant(person.Id, room.Id);

        IsAssigning = false;
        AssignSearchText = string.Empty;
        AssignResults = new ObservableCollection<UnassignedPersonItem>();

        RefreshSelectedRoomOccupants();
        NotifyComputedProperties();
    }

    [RelayCommand]
    private void UnassignPerson(RoomOccupantViewModel? occupant)
    {
        if (occupant is null || SelectedRoom is null) return;

        _roomService.UnassignOccupant(occupant.RecipientId);

        RefreshSelectedRoomOccupants();
        NotifyComputedProperties();
    }

    private void RefreshSelectedRoomOccupants()
    {
        if (SelectedRoom is null) return;

        var rows = _roomService.GetOccupants(SelectedRoom.Id);
        Occupants = BuildOccupantRows(rows);

        SelectedRoom.SetOccupants(rows.Select(o => new RoomOccupantSummary(o.RecipientId, o.ShortName)).ToList());
    }

    private static ObservableCollection<RoomOccupantViewModel> BuildOccupantRows(List<RoomOccupantDto> rows)
        => new(rows.Select((o, i) =>
            new RoomOccupantViewModel(i + 1, o.RecipientId, o.FullName, o.Rank, o.Position, o.Unit, o.PersonalNumber)));
}
