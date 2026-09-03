using CommunityToolkit.Mvvm.ComponentModel;
using GenDoc.Services;

namespace GenDoc.ViewModels.Generation;

public partial class RankOptionViewModel : ObservableObject
{
    public RankOptionViewModel(string rank, RankCategory category, int count)
    {
        Rank = rank;
        Category = category;
        Count = count;
    }

    public string Rank { get; }
    public RankCategory Category { get; }
    public int Count { get; }
    public string Label => $"{Rank} ({Count})";

    [ObservableProperty]
    private bool isChecked;
}
