using CommunityToolkit.Mvvm.ComponentModel;
using GenDoc.Services.Recipients;

namespace GenDoc.ViewModels.Recipients;

// Обгортка над RecipientListItem (record із сервісу) - додає мутабельний
// IsSelected для режиму масового видалення чекбоксами, не займаючи цим
// DTO-контракт сервісу.
public partial class RecipientRowViewModel : ObservableObject
{
    public RecipientRowViewModel(RecipientListItem item)
    {
        Item = item;
    }

    public RecipientListItem Item { get; }

    public int Id => Item.Id;
    public string FullName => Item.FullName;
    public string Rank => Item.Rank;
    public string Position => Item.Position;
    public string UnitName => Item.UnitName;
    public string RoomDisplay => Item.RoomDisplay;

    [ObservableProperty]
    private bool isSelected;
}
