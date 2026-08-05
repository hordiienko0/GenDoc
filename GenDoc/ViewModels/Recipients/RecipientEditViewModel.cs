using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Models.Enums;
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

    // Анкетні дані (прикомандировані) — Б.4
    [ObservableProperty] private string? nationality;
    [ObservableProperty] private string? vos;
    [ObservableProperty] private DateTime? courseArrivalDate;
    [ObservableProperty] private string? maritalStatus;
    [ObservableProperty] private string? registrationAddress;
    [ObservableProperty] private string? residenceAddress;
    [ObservableProperty] private string? phone;
    [ObservableProperty] private string? note;
    [ObservableProperty] private string? groupName;
    [ObservableProperty] private string? nameTransliterated;
    [ObservableProperty] private string? servedBefore;
    [ObservableProperty] private string? extraNote;
    [ObservableProperty] private string? commanderContact;
    [ObservableProperty] private string? travelCertificateNumber;
    [ObservableProperty] private string? foodCertificate;
    [ObservableProperty] private string? idDocumentNumber;
    [ObservableProperty] private string? medicalBoard;
    [ObservableProperty] private string? medicalBoardNumber;
    [ObservableProperty] private DateTime? medicalBoardDate;
    private bool _isLoadingMedicalBoard;
    [ObservableProperty] private string? medicalBoardConclusion;
    [ObservableProperty] private string? originUnit;
    [ObservableProperty] private string? vehicle;

    // Уточнення відмінків — Part F
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGenderMale))]
    [NotifyPropertyChangedFor(nameof(IsGenderFemale))]
    private Gender? gender;

    [ObservableProperty] private string? rankAccusative;
    [ObservableProperty] private string? fullNameAccusative;
    [ObservableProperty] private string? positionAccusative;

    public bool IsGenderMale
    {
        get => Gender == GenDoc.Models.Enums.Gender.Male;
        set { if (value) Gender = GenDoc.Models.Enums.Gender.Male; }
    }

    public bool IsGenderFemale
    {
        get => Gender == GenDoc.Models.Enums.Gender.Female;
        set { if (value) Gender = GenDoc.Models.Enums.Gender.Female; }
    }

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

            Nationality = model.Nationality;
            Vos = model.Vos;
            CourseArrivalDate = model.CourseArrivalDate?.ToDateTime(TimeOnly.MinValue);
            MaritalStatus = model.MaritalStatus;
            RegistrationAddress = model.RegistrationAddress;
            ResidenceAddress = model.ResidenceAddress;
            Phone = model.Phone;
            Note = model.Note;
            GroupName = model.GroupName;
            NameTransliterated = model.NameTransliterated;
            ServedBefore = model.ServedBefore;
            ExtraNote = model.ExtraNote;
            CommanderContact = model.CommanderContact;
            TravelCertificateNumber = model.TravelCertificateNumber;
            FoodCertificate = model.FoodCertificate;
            IdDocumentNumber = model.IdDocumentNumber;
            MedicalBoard = model.MedicalBoard;
            LoadMedicalBoardSubFields(model.MedicalBoard);
            MedicalBoardConclusion = model.MedicalBoardConclusion;
            OriginUnit = model.OriginUnit;
            Vehicle = model.Vehicle;

            Gender = model.Gender;
            RankAccusative = model.RankAccusative;
            FullNameAccusative = model.FullNameAccusative;
            PositionAccusative = model.PositionAccusative;
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

            Nationality = null;
            Vos = null;
            CourseArrivalDate = null;
            MaritalStatus = null;
            RegistrationAddress = null;
            ResidenceAddress = null;
            Phone = null;
            Note = null;
            GroupName = null;
            NameTransliterated = null;
            ServedBefore = null;
            ExtraNote = null;
            CommanderContact = null;
            TravelCertificateNumber = null;
            FoodCertificate = null;
            IdDocumentNumber = null;
            MedicalBoard = null;
            LoadMedicalBoardSubFields(null);
            MedicalBoardConclusion = null;
            OriginUnit = null;
            Vehicle = null;

            Gender = null;
            RankAccusative = null;
            FullNameAccusative = null;
            PositionAccusative = null;
        }

        UpdateRoomWarning();
        UpdateServiceNumberWarning();
        UpdateRankSuggestions();
    }

    partial void OnRoomBuildingChanged(string? value) => UpdateRoomWarning();
    partial void OnRoomNumberChanged(string? value) => UpdateRoomWarning();
    partial void OnServiceNumberChanged(string value) => UpdateServiceNumberWarning();

    private static readonly Regex MedicalBoardDatePattern = new(@"\d{1,2}\.\d{1,2}\.\d{2,4}", RegexOptions.Compiled);

    partial void OnMedicalBoardNumberChanged(string? value) => RecomputeMedicalBoard();
    partial void OnMedicalBoardDateChanged(DateTime? value) => RecomputeMedicalBoard();

    private void RecomputeMedicalBoard()
    {
        if (_isLoadingMedicalBoard) return;

        MedicalBoard = (MedicalBoardNumber, MedicalBoardDate) switch
        {
            (string n, DateTime d) when !string.IsNullOrWhiteSpace(n) => $"№ {n} від {d:dd.MM.yyyy}",
            (string n, null) when !string.IsNullOrWhiteSpace(n) => $"№ {n}",
            (_, DateTime d) => $"від {d:dd.MM.yyyy}",
            _ => null
        };
    }

    private void LoadMedicalBoardSubFields(string? value)
    {
        _isLoadingMedicalBoard = true;
        try
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                MedicalBoardNumber = null;
                MedicalBoardDate = null;
                return;
            }

            var dateMatch = MedicalBoardDatePattern.Match(value);
            if (dateMatch.Success
                && DateTime.TryParseExact(dateMatch.Value, "dd.MM.yyyy", CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsedDate))
            {
                MedicalBoardDate = parsedDate;
                var remainder = value.Remove(dateMatch.Index, dateMatch.Length)
                    .Replace("№", string.Empty).Replace("від", string.Empty).Trim(' ', ',', '-');
                MedicalBoardNumber = string.IsNullOrWhiteSpace(remainder) ? null : remainder;
            }
            else
            {
                MedicalBoardDate = null;
                MedicalBoardNumber = value;
            }
        }
        finally
        {
            _isLoadingMedicalBoard = false;
        }
    }
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

            Nationality = Nationality,
            Vos = Vos,
            CourseArrivalDate = CourseArrivalDate.HasValue ? DateOnly.FromDateTime(CourseArrivalDate.Value) : null,
            MaritalStatus = MaritalStatus,
            RegistrationAddress = RegistrationAddress,
            ResidenceAddress = ResidenceAddress,
            Phone = Phone,
            Note = Note,
            GroupName = GroupName,
            NameTransliterated = NameTransliterated,
            ServedBefore = ServedBefore,
            ExtraNote = ExtraNote,
            CommanderContact = CommanderContact,
            TravelCertificateNumber = TravelCertificateNumber,
            FoodCertificate = FoodCertificate,
            IdDocumentNumber = IdDocumentNumber,
            MedicalBoard = MedicalBoard,
            MedicalBoardConclusion = MedicalBoardConclusion,
            OriginUnit = OriginUnit,
            Vehicle = Vehicle,

            Gender = Gender,
            RankAccusative = RankAccusative,
            FullNameAccusative = FullNameAccusative,
            PositionAccusative = PositionAccusative,
        }, out var saveError);

        if (saveError is not null)
        {
            ErrorMessage = saveError;
            return false;
        }

        return true;
    }
}
