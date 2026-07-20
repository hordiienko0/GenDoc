using CommunityToolkit.Mvvm.ComponentModel;

namespace GenDoc.ViewModels.Shell;

public partial class NavigationItem : ObservableObject
{
    public string Title { get; }
    public Func<object> ContentFactory { get; }

    public NavigationItem(string title, Func<object> contentFactory)
    {
        Title = title;
        ContentFactory = contentFactory;
    }

    [ObservableProperty]
    private bool isActive;
}
