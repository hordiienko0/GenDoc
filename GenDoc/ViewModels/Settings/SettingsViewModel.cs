using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Data;
using GenDoc.Services;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.ViewModels.Settings;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IAuditLogService _auditLogService;
    private int _id;

    public SettingsViewModel(IDbContextFactory<AppDbContext> dbFactory, IAuditLogService auditLogService)
    {
        _dbFactory = dbFactory;
        _auditLogService = auditLogService;

        Load();
    }

    [ObservableProperty] private string unitNumber = string.Empty;
    [ObservableProperty] private string city = string.Empty;
    [ObservableProperty] private string commanderRank = string.Empty;
    [ObservableProperty] private string commanderFullName = string.Empty;
    [ObservableProperty] private string hrOfficerFullName = string.Empty;
    [ObservableProperty] private string commanderPosition = string.Empty;
    [ObservableProperty] private string unitFullName = string.Empty;

    private void Load()
    {
        using var db = _dbFactory.CreateDbContext();
        var settings = db.OrganizationSettings.FirstOrDefault();
        if (settings is null) return;

        _id = settings.Id;
        UnitNumber = settings.UnitNumber;
        City = settings.City;
        CommanderRank = settings.CommanderRank;
        CommanderFullName = settings.CommanderFullName;
        HrOfficerFullName = settings.HrOfficerFullName;
        CommanderPosition = settings.CommanderPosition;
        UnitFullName = settings.UnitFullName;
    }

    [RelayCommand]
    private void Save()
    {
        using var db = _dbFactory.CreateDbContext();
        var settings = db.OrganizationSettings.First(s => s.Id == _id);

        var oldParts = new List<string>();
        var newParts = new List<string>();
        AddIfChanged(oldParts, newParts, "Номер в/ч", settings.UnitNumber, UnitNumber);
        AddIfChanged(oldParts, newParts, "Місто", settings.City, City);
        AddIfChanged(oldParts, newParts, "Звання командира", settings.CommanderRank, CommanderRank);
        AddIfChanged(oldParts, newParts, "ПІБ командира", settings.CommanderFullName, CommanderFullName);
        AddIfChanged(oldParts, newParts, "ПІБ кадровика", settings.HrOfficerFullName, HrOfficerFullName);
        AddIfChanged(oldParts, newParts, "Посада командира", settings.CommanderPosition, CommanderPosition);
        AddIfChanged(oldParts, newParts, "Повна назва частини", settings.UnitFullName, UnitFullName);

        settings.UnitNumber = UnitNumber.Trim();
        settings.City = City.Trim();
        settings.CommanderRank = CommanderRank.Trim();
        settings.CommanderFullName = CommanderFullName.Trim();
        settings.HrOfficerFullName = HrOfficerFullName.Trim();
        settings.CommanderPosition = CommanderPosition.Trim();
        settings.UnitFullName = UnitFullName.Trim();

        db.SaveChanges();

        if (oldParts.Count > 0)
        {
            _auditLogService.LogUpdate(db, "OrganizationSettings", _id,
                string.Join("; ", oldParts), string.Join("; ", newParts), "Оновлено дані частини");
            db.SaveChanges();
        }

        MessageBox.Show("Дані частини збережено.", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static void AddIfChanged(List<string> oldParts, List<string> newParts, string label, string oldValue, string newValue)
    {
        var trimmedNew = newValue.Trim();
        if (oldValue == trimmedNew) return;

        oldParts.Add($"{label}: {oldValue}");
        newParts.Add($"{label}: {trimmedNew}");
    }
}
