using CommunityToolkit.Mvvm.ComponentModel;

namespace GenDoc.ViewModels.Generation;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShown))]
    private bool isVisible = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShown))]
    private bool matchesSearch = true;

    public bool IsShown => IsVisible && MatchesSearch;

    internal static bool Matches(string fullName, string? query)
        => string.IsNullOrWhiteSpace(query)
           || fullName.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase);
}
