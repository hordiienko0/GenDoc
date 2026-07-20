using System.Collections.ObjectModel;

namespace GenDoc.ViewModels.Shell;

public class NavigationGroup
{
    public ObservableCollection<NavigationItem> Items { get; }
    public bool ShowDividerAfter { get; }

    public NavigationGroup(IEnumerable<NavigationItem> items, bool showDividerAfter)
    {
        Items = new ObservableCollection<NavigationItem>(items);
        ShowDividerAfter = showDividerAfter;
    }
}
