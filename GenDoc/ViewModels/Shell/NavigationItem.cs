using CommunityToolkit.Mvvm.ComponentModel;

namespace GenDoc.ViewModels.Shell;

/// <summary>Визначає колір бейджа: інформаційний лічильник чи те, що потребує уваги.</summary>
public enum NavigationBadgeKind
{
    Info,
    Attention
}

public partial class NavigationItem : ObservableObject
{
    public string Title { get; }
    public Func<object> ContentFactory { get; }
    public NavigationBadgeKind BadgeKind { get; }

    public NavigationItem(string title, Func<object> contentFactory,
        NavigationBadgeKind badgeKind = NavigationBadgeKind.Info)
    {
        Title = title;
        ContentFactory = contentFactory;
        BadgeKind = badgeKind;
    }

    [ObservableProperty]
    private bool isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBadge))]
    private int badgeCount;

    public bool HasBadge => BadgeCount > 0;
}
