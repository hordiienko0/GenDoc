using CommunityToolkit.Mvvm.ComponentModel;
using GenDoc.Services;

namespace GenDoc.ViewModels.Generation;

// Чип-перемикач категорії звань. IsChecked — тристанове: true/false — усі звання
// категорії позначено/знято, null — позначено лише частину (після ручного зняття
// однієї позначки в списку "Окремі звання").
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
