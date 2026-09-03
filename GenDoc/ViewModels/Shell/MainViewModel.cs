using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Data;
using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Services.Intakes;
using GenDoc.Services.Navigation;
using GenDoc.ViewModels.Archive;
using GenDoc.ViewModels.Audit;
using GenDoc.ViewModels.Generation;
using GenDoc.ViewModels.Import;
using GenDoc.ViewModels.Intakes;
using GenDoc.ViewModels.Personnel;
using GenDoc.ViewModels.Rooms;
using GenDoc.ViewModels.Settings;
using GenDoc.ViewModels.Templates;
using GenDoc.ViewModels.Trash;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Windows;

namespace GenDoc.ViewModels.Shell;

public partial class MainViewModel : ObservableObject
{
    public const string PersonnelSectionTitle = "Особовий склад";
    public const string IntakesSectionTitle = "Набори";
    public const string CompletenessSectionTitle = "Комплектність";
    public const string GenerationSectionTitle = "Генерація";
    public const string ArchiveSectionTitle = "Архів документів";

    private readonly IServiceProvider _serviceProvider;

    private readonly ActiveIntakeState _activeIntakeState;
    private readonly Services.Completeness.ICompletenessService _completenessService;
    private readonly IIntakeService _intakeService;
    private NavigationItem? _completenessNavItem;
    private NavigationItem? _intakesNavItem;

    public MainViewModel(
        IServiceProvider serviceProvider,
        ICurrentUserContext currentUserContext,
        IDbContextFactory<AppDbContext> dbFactory,
        ActiveIntakeState activeIntakeState,
        Services.Completeness.ICompletenessService completenessService,
        IIntakeService intakeService)
    {
        _serviceProvider = serviceProvider;
        _activeIntakeState = activeIntakeState;
        _completenessService = completenessService;
        _intakeService = intakeService;

        WeakReferenceMessenger.Default.Register<MainViewModel, ActiveIntakeChangedMessage>(this,
            static (recipient, message) =>
            {
                recipient.StatusBarIntakeText = recipient._activeIntakeState.StatusText;
                recipient.RefreshCompletenessBadgeFireAndForget();
                recipient.RefreshIntakesBadgeFireAndForget();
            });
        WeakReferenceMessenger.Default.Register<MainViewModel, MatrixChangedMessage>(this,
            static (recipient, message) => recipient.RefreshCompletenessBadgeFireAndForget());
        WeakReferenceMessenger.Default.Register<MainViewModel, CountsChangedMessage>(this,
            static (recipient, message) => recipient.RefreshCompletenessBadgeFireAndForget());
        WeakReferenceMessenger.Default.Register<MainViewModel, NavigateToSectionMessage>(this,
            static (recipient, message) => _ = recipient.NavigateAsync(message));
        _ = InitializeIntakeStateAsync();
        CurrentUserFullName = currentUserContext.CurrentUserFullName ?? string.Empty;
        var organization = ReadOrganizationSettings(dbFactory);
        OrganizationDisplayName = FormatOrganizationDisplayName(organization);
        SidebarUnitShortName = organization?.UnitNumber ?? string.Empty;
        SidebarUnitFullName = organization?.UnitFullName ?? string.Empty;

        var unitCaption = string.Join(" ", new[] { SidebarUnitShortName, SidebarUnitFullName }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
        WindowTitleText = string.IsNullOrWhiteSpace(unitCaption)
            ? AppTitle
            : $"{AppTitle} · {unitCaption}";

        StatusBarUserText = $"Користувач: {CurrentUserFullName}";
        StatusBarConnectionText = string.IsNullOrWhiteSpace(OrganizationDisplayName)
            ? "Захищене з'єднання"
            : $"{OrganizationDisplayName} · Захищене з'єднання";

        Groups = new ObservableCollection<NavigationGroup>
        {
            new(new[]
            {
                new NavigationItem(PersonnelSectionTitle, "\uE716", () => _serviceProvider.GetRequiredService<PersonnelViewModel>()),
                new NavigationItem("Постійний склад", "\uE77B", () => _serviceProvider.GetRequiredService<GenDoc.ViewModels.Staff.StaffViewModel>()),
                (_intakesNavItem = new NavigationItem(IntakesSectionTitle, "\uE787",
                    () => _serviceProvider.GetRequiredService<IntakesViewModel>())),
                new NavigationItem("Імпорт з Excel", "\uE896", () => _serviceProvider.GetRequiredService<ImportViewModel>()),
                new NavigationItem("Шаблони", "\uE8A5", () => _serviceProvider.GetRequiredService<TemplatesViewModel>()),
                new NavigationItem(GenerationSectionTitle, "\uE8C8", () => _serviceProvider.GetRequiredService<GenerationViewModel>()),
                (_completenessNavItem = new NavigationItem(CompletenessSectionTitle, "\uE73E",
                    () => _serviceProvider.GetRequiredService<GenDoc.ViewModels.Completeness.CompletenessViewModel>(),
                    NavigationBadgeKind.Attention)),
                new NavigationItem(ArchiveSectionTitle, "\uE8B7", () => _serviceProvider.GetRequiredService<ArchiveViewModel>()),
            }, showDividerAfter: true),
            new(new[]
            {
                new NavigationItem("Кімнати", "\uE80F", () => _serviceProvider.GetRequiredService<RoomsViewModel>()),
                new NavigationItem("Журнал дій", "\uE81C", () => _serviceProvider.GetRequiredService<AuditLogViewModel>()),
            }, showDividerAfter: true),
            new(new[]
            {
                new NavigationItem("Кошик", "\uE74D", () => _serviceProvider.GetRequiredService<TrashViewModel>()),
                new NavigationItem("Налаштування", "\uE713", () => _serviceProvider.GetRequiredService<SettingsViewModel>()),
            }, showDividerAfter: false),
        };

        var items = Groups.SelectMany(g => g.Items).ToList();
        var startItem = items.FirstOrDefault(i => i.Title == PersonnelSectionTitle) ?? items.First();
        _ = SelectItemAsync(startItem);
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSidebarUnitFullName))]
    private string sidebarUnitShortName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSidebarUnitFullName))]
    private string sidebarUnitFullName = string.Empty;

    public bool HasSidebarUnitFullName => !string.IsNullOrWhiteSpace(SidebarUnitFullName);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarExpanded))]
    [NotifyPropertyChangedFor(nameof(SidebarColumnWidth))]
    [NotifyPropertyChangedFor(nameof(SidebarWidth))]
    [NotifyPropertyChangedFor(nameof(SidebarToggleIcon))]
    [NotifyPropertyChangedFor(nameof(SidebarToggleTooltip))]
    private bool isSidebarPinned = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarExpanded))]
    [NotifyPropertyChangedFor(nameof(SidebarWidth))]
    private bool isSidebarHovered;

    public bool IsSidebarExpanded => IsSidebarPinned || IsSidebarHovered;

    public const double SidebarExpandedWidth = 212;
    public const double SidebarRailWidth = 52;

    public double SidebarWidth => IsSidebarExpanded ? SidebarExpandedWidth : SidebarRailWidth;

    public GridLength SidebarColumnWidth => new(IsSidebarPinned ? SidebarExpandedWidth : SidebarRailWidth);

    public string SidebarToggleIcon => IsSidebarPinned ? "\uE76B" : "\uE76C";

    public string SidebarToggleTooltip => IsSidebarPinned
        ? "Згорнути меню в смужку іконок"
        : "Закріпити меню розгорнутим";

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarPinned = !IsSidebarPinned;

    private const string AppTitle = "GenDoc - Облік особового складу";

    public string WindowTitleText { get; private set; } = AppTitle;

    public string StatusBarUserText { get; private set; } = string.Empty;
    public string StatusBarConnectionText { get; private set; } = string.Empty;

    [ObservableProperty]
    private string statusBarIntakeText = "Активного набору немає";

    private async Task InitializeIntakeStateAsync()
    {
        await _activeIntakeState.RefreshAsync();
        await RefreshCompletenessBadgeAsync();
        await RefreshIntakesBadgeAsync();
    }

    private async Task RefreshCompletenessBadgeAsync()
    {
        if (_completenessNavItem is null) return;
        _completenessNavItem.BadgeCount = await _completenessService.GetBadgeCountAsync();
    }

    private void RefreshCompletenessBadgeFireAndForget() => _ = RefreshCompletenessBadgeAsync();

    private async Task RefreshIntakesBadgeAsync()
    {
        if (_intakesNavItem is null) return;
        var overviews = await _intakeService.GetOverviewsAsync();
        _intakesNavItem.BadgeCount = overviews.Count(i =>
            i.Status is Models.Enums.IntakeStatus.Active or Models.Enums.IntakeStatus.Planned);
    }

    private void RefreshIntakesBadgeFireAndForget() => _ = RefreshIntakesBadgeAsync();

    public async Task<bool> TryLeaveCurrentSectionAsync()
        => CurrentContent is not IGuardedSection guarded || await guarded.TryLeaveAsync();

    [RelayCommand]
    private async Task SelectItemAsync(NavigationItem? item)
    {
        if (item is null || item == SelectedItem) return;

        if (!await TryLeaveCurrentSectionAsync()) return;

        if (SelectedItem is not null) SelectedItem.IsActive = false;
        SelectedItem = item;
        SelectedItem.IsActive = true;
        CurrentContent = item.ContentFactory();
    }

    private async Task NavigateAsync(NavigateToSectionMessage m)
    {
        var item = Groups.SelectMany(g => g.Items).FirstOrDefault(i => i.Title == m.SectionTitle);
        if (item is null) return;

        if (item != SelectedItem)
        {
            await SelectItemAsync(item);
            if (SelectedItem != item) return;
        }

        if (m.Payload is not null && CurrentContent is INavigationTarget target)
            await target.ApplyNavigationPayloadAsync(m.Payload);
    }

    private static OrganizationSettings? ReadOrganizationSettings(IDbContextFactory<AppDbContext> dbFactory)
    {
        using var db = dbFactory.CreateDbContext();
        return db.OrganizationSettings.AsNoTracking().FirstOrDefault();
    }

    private static string FormatOrganizationDisplayName(OrganizationSettings? settings)
    {
        if (settings is null) return string.Empty;

        return string.IsNullOrWhiteSpace(settings.UnitNumber)
            ? settings.City
            : string.IsNullOrWhiteSpace(settings.City) ? settings.UnitNumber : $"{settings.UnitNumber}, {settings.City}";
    }
}
