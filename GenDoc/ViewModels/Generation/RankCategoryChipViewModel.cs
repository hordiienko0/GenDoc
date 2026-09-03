using CommunityToolkit.Mvvm.ComponentModel;
using GenDoc.Services;

namespace GenDoc.ViewModels.Generation;

public partial class RankCategoryChipViewModel : ObservableObject
{
    public RankCategoryChipViewModel(RankCategory category, int count)
    {
        Category = category;
        DisplayName = RankOrder.CategoryDisplayName(category);
        Count = count;
    }

    public RankCategory Category { get; }
    public string DisplayName { get; }
    public int Count { get; }
    public string Label => $"{DisplayName} ({Count})";

    [ObservableProperty]
    private bool? isChecked;
}
