namespace GenDoc.ViewModels.Rooms;

// Простий read-only рядок таблиці мешканців панелі деталей - список повністю
// перебудовується у RoomsViewModel при кожній зміні складу (поселення/виселення),
// тож окремого INotifyPropertyChanged тут не потрібно.
public class RoomOccupantViewModel
{
    public RoomOccupantViewModel(
        int rowNumber, int recipientId, string fullName, string rank, string position, string unit, string personalNumber)
    {
        RowNumber = rowNumber;
        RecipientId = recipientId;
        FullName = fullName;
        Rank = rank;
        Position = position;
        Unit = unit;
        PersonalNumber = personalNumber;
    }

    public int RowNumber { get; }
    public int RecipientId { get; }
    public string FullName { get; }
    public string Rank { get; }
    public string Position { get; }
    public string Unit { get; }
    public string PersonalNumber { get; }

    public string PositionUnitDisplay => $"{Position} · {Unit}";
}
