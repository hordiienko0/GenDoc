using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Data;
using GenDoc.Services;
using GenDoc.ViewModels.Archive;
using GenDoc.ViewModels.Audit;
using GenDoc.ViewModels.Personnel;
using GenDoc.ViewModels.Trash;
using GenDoc.ViewModels.Generation;
using GenDoc.ViewModels.Import;
using GenDoc.ViewModels.Recipients;
using GenDoc.ViewModels.Rooms;
using GenDoc.ViewModels.Settings;
using GenDoc.ViewModels.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.ViewModels.Shell;

public partial class MainViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;

    private readonly ActiveIntakeState _activeIntakeState;
    private readonly Services.Completeness.ICompletenessService _completenessService;
    private NavigationItem? _completenessNavItem;

    public MainViewModel(
        IServiceProvider serviceProvider,
        ICurrentUserContext currentUserContext,
        IDbContextFactory<AppDbContext> dbFactory,
        ActiveIntakeState activeIntakeState,
        Services.Completeness.ICompletenessService completenessService)
    {
        _serviceProvider = serviceProvider;
        _activeIntakeState = activeIntakeState;
        _completenessService = completenessService;

        WeakReferenceMessenger.Default.Register<MainViewModel, ActiveIntakeChangedMessage>(this,
            static (recipient, message) =>
            {
                recipient.StatusBarIntakeText = recipient._activeIntakeState.StatusText;
                recipient.RefreshCompletenessBadgeFireAndForget();
            });
        WeakReferenceMessenger.Default.Register<MainViewModel, MatrixChangedMessage>(this,
            static (recipient, message) => recipient.RefreshCompletenessBadgeFireAndForget());
        WeakReferenceMessenger.Default.Register<MainViewModel, CountsChangedMessage>(this,
            static (recipient, message) => recipient.RefreshCompletenessBadgeFireAndForget());
        _ = InitializeIntakeStateAsync();
        CurrentUserFullName = currentUserContext.CurrentUserFullName ?? string.Empty;
        OrganizationDisplayName = ReadOrganizationDisplayName(dbFactory);

        StatusBarUserText = $"Користувач: {CurrentUserFullName}";
        StatusBarConnectionText = string.IsNullOrWhiteSpace(OrganizationDisplayName)
            ? "Захищене з'єднання"
            : $"{OrganizationDisplayName} · Захищене з'єднання";

        Groups = new ObservableCollection<NavigationGroup>
        {
            new(new[]
            {
                new NavigationItem("Особовий склад", () => _serviceProvider.GetRequiredService<PersonnelViewModel>()),
                new NavigationItem("Імпорт з Excel", () => _serviceProvider.GetRequiredService<ImportViewModel>()),
                new NavigationItem("Шаблони", () => _serviceProvider.GetRequiredService<TemplatesViewModel>()),
                new NavigationItem("Генерація", () => _serviceProvider.GetRequiredService<GenerationViewModel>()),
                new NavigationItem("Архів документів", () => _serviceProvider.GetRequiredService<ArchiveViewModel>()),
                (_completenessNavItem = new NavigationItem("Комплектність",
                    () => _serviceProvider.GetRequiredService<GenDoc.ViewModels.Completeness.CompletenessViewModel>())),
                new NavigationItem("Кімнати", () => _serviceProvider.GetRequiredService<RoomsViewModel>()),
            }, showDividerAfter: true),
            new(new[]
            {
                new NavigationItem("Журнал дій", () => _serviceProvider.GetRequiredService<AuditLogViewModel>()),
                new NavigationItem("Кошик", () => _serviceProvider.GetRequiredService<TrashViewModel>()),
            }, showDividerAfter: true),
            new(new[]
            {
                new NavigationItem("Налаштування", () => _serviceProvider.GetRequiredService<SettingsViewModel>()),
            }, showDividerAfter: false),
        };

        var firstItem = Groups.SelectMany(g => g.Items).First();
        _ = SelectItemAsync(firstItem);
    }

    [ObservableProperty]
    private ObservableCollection<NavigationGroup> groups = new();

    [ObservableProperty]
    private NavigationItem? selectedItem;

    [ObservableProperty]
    private object? currentContent;

    [ObservableProperty]
    private string currentUserFullName = string.Empty;

    [ObservableProperty]
    private string organizationDisplayName = string.Empty;

    public string StatusBarUserText { get; private set; } = string.Empty;
    public string StatusBarConnectionText { get; private set; } = string.Empty;

    [ObservableProperty]
    private string statusBarIntakeText = "Активного набору немає";

    private async Task InitializeIntakeStateAsync()
    {
        await _activeIntakeState.RefreshAsync();
        await RefreshCompletenessBadgeAsync();
    }

    private async Task RefreshCompletenessBadgeAsync()
    {
        if (_completenessNavItem is null) return;
        _completenessNavItem.BadgeCount = await _completenessService.GetBadgeCountAsync();
    }

    private void RefreshCompletenessBadgeFireAndForget() => _ = RefreshCompletenessBadgeAsync();

    [RelayCommand]
    private async Task SelectItemAsync(NavigationItem? item)
    {
        if (item is null || item == SelectedItem) return;

        // Розділ із незбереженими змінами може заблокувати перехід.
        if (CurrentContent is IGuardedSection guarded && !await guarded.TryLeaveAsync()) return;

        if (SelectedItem is not null) SelectedItem.IsActive = false;
        SelectedItem = item;
        SelectedItem.IsActive = true;
        CurrentContent = item.ContentFactory();
    }

    private static string ReadOrganizationDisplayName(IDbContextFactory<AppDbContext> dbFactory)
    {
        using var db = dbFactory.CreateDbContext();
        var settings = db.OrganizationSettings.AsNoTracking().FirstOrDefault();
        if (settings is null) return string.Empty;

        return string.IsNullOrWhiteSpace(settings.UnitNumber)
            ? settings.City
            : string.IsNullOrWhiteSpace(settings.City) ? settings.UnitNumber : $"{settings.UnitNumber}, {settings.City}";
    }
}
