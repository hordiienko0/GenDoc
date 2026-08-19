using CommunityToolkit.Mvvm.ComponentModel;

namespace GenDoc.ViewModels.Generation;

// Рядок чекбокс-списку для вибору підмножини складу під час генерації -
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
    [NotifyPropertyChangedFor(nameof(IsShown))]
    private bool isVisible = true;

    // Пошук за ПІБ (2.3): лише показ; на позначки й участь у генерації не впливає.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShown))]
    private bool matchesSearch = true;

    public bool IsShown => IsVisible && MatchesSearch;

    internal static bool Matches(string fullName, string? query)
        => string.IsNullOrWhiteSpace(query)
           || fullName.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase);
}
