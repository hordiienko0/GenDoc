using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Data;
using GenDoc.Services;
using GenDoc.ViewModels.Audit;
using GenDoc.ViewModels.Generation;
using GenDoc.ViewModels.Import;
using GenDoc.ViewModels.Recipients;
using GenDoc.ViewModels.Settings;
using GenDoc.ViewModels.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.ViewModels.Shell;

public partial class MainViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;

    public MainViewModel(
        IServiceProvider serviceProvider,
        ICurrentUserContext currentUserContext,
        IDbContextFactory<AppDbContext> dbFactory)
    {
        _serviceProvider = serviceProvider;
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
                new NavigationItem("Особовий склад", () => _serviceProvider.GetRequiredService<RecipientsViewModel>()),
                new NavigationItem("Імпорт з Excel", () => _serviceProvider.GetRequiredService<ImportViewModel>()),
                new NavigationItem("Шаблони", () => _serviceProvider.GetRequiredService<TemplatesViewModel>()),
                new NavigationItem("Генерація", () => _serviceProvider.GetRequiredService<GenerationViewModel>()),
                new NavigationItem("Кімнати", () => new PlaceholderViewModel("Кімнати")),
            }, showDividerAfter: true),
            new(new[]
            {
                new NavigationItem("Журнал дій", () => _serviceProvider.GetRequiredService<AuditLogViewModel>()),
                new NavigationItem("Кошик", () => new PlaceholderViewModel("Кошик")),
            }, showDividerAfter: true),
            new(new[]
            {
                new NavigationItem("Налаштування", () => _serviceProvider.GetRequiredService<SettingsViewModel>()),
            }, showDividerAfter: false),
        };

        var firstItem = Groups.SelectMany(g => g.Items).First();
        SelectItem(firstItem);
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

    [RelayCommand]
    private void SelectItem(NavigationItem? item)
    {
        if (item is null || item == SelectedItem) return;

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
