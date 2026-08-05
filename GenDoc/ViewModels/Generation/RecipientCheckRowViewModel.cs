using CommunityToolkit.Mvvm.ComponentModel;

namespace GenDoc.ViewModels.Generation;

// Рядок чекбокс-списку для вибору підмножини складу під час генерації —
// той самий патерн, що й RecipientRowViewModel.IsSelected у розділі «Особовий склад».
public partial class RecipientCheckRowViewModel : ObservableObject
{
    public RecipientCheckRowViewModel(int id, string rank, string fullName, string unitName)
    {
        Id = id;
        Rank = rank;
        FullName = fullName;
        UnitName = unitName;
    }

    public int Id { get; }
    public string Rank { get; }
    public string FullName { get; }
    public string UnitName { get; }

    [ObservableProperty]
    private bool isChecked;

    // Керується фільтром звань: людина, чиє звання відфільтроване, ховається
    // зі списку і автоматично знімається з позначення (не бере участі в генерації).
    [ObservableProperty]
    private bool isVisible = true;
}
