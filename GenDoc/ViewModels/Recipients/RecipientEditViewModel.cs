using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Services.Recipients;

namespace GenDoc.ViewModels.Recipients;

public partial class RecipientEditViewModel : ObservableObject
{
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

    [ObservableProperty] private string lastName = string.Empty;
    [ObservableProperty] private string firstName = string.Empty;
    [ObservableProperty] private string? middleName;
    [ObservableProperty] private string rank = string.Empty;
    [ObservableProperty] private string position = string.Empty;
    [ObservableProperty] private string serviceNumber = string.Empty;
    [ObservableProperty] private DateTime? dateOfBirth;
    [ObservableProperty] private string? unitName;
    [ObservableProperty] private string? roomBuilding;
    [ObservableProperty] private string? roomNumber;

    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private ObservableCollection<string> unitNameOptions = new();
    [ObservableProperty] private ObservableCollection<string> roomBuildingOptions = new();

    public void Initialize(int? recipientId)
    {
        UnitNameOptions = new ObservableCollection<string>(_recipientService.GetUnitNames());
        RoomBuildingOptions = new ObservableCollection<string>(_recipientService.GetRoomBuildings());

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
        }
    }

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(LastName) || string.IsNullOrWhiteSpace(FirstName) ||
            string.IsNullOrWhiteSpace(Rank) || string.IsNullOrWhiteSpace(Position) ||
            string.IsNullOrWhiteSpace(ServiceNumber))
        {
            ErrorMessage = "Заповніть прізвище, ім'я, звання, посаду та особовий номер.";
            return;
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
        });

        WasSaved = true;
        RequestClose?.Invoke(this, EventArgs.Empty);
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
}
