namespace GenDoc.Services.Duty;

public interface IDutyRosterService
{
    Task<IReadOnlyList<DutyRosterListItem>> GetListAsync();
    /// <summary>Усе дерево папок (батьки перед дітьми) з тим, кого кожна папка приведе в графік.</summary>
    Task<IReadOnlyList<DutyFolderOption>> GetFoldersAsync();

    /// <summary>
    /// Люди, яких можна додати в графік: склад набору й постійний склад, крім уже доданих.
    /// Для графіка не з одного набору - усі люди з бази.
    /// </summary>
    Task<IReadOnlyList<DutyPersonOption>> GetPeopleToAddAsync(int? intakeId, IReadOnlyCollection<int> alreadyInRoster);

    Task<DutyRosterData> CreateAsync(DutyRosterCreateRequest request);
    Task<DutyRosterData?> LoadAsync(int id);
    Task SaveAsync(DutyRosterData roster);
    Task DeleteAsync(int id);

    /// <summary>Дні відпустки, шпиталю й відрядження в межах періоду - для зелених клітинок «з подій».</summary>
    Task<IReadOnlyDictionary<int, IReadOnlySet<DateOnly>>> GetEventExemptionsAsync(
        IReadOnlyCollection<int> recipientIds, DateOnly from, DateOnly to);

    /// <summary>Замінює звільнення «з подій» на актуальні; ручні позначки не чіпає.</summary>
    Task ApplyEventExemptionsAsync(DutyRosterData roster);

    Task<DutyCarryOver?> GetCarryOverAsync(DutyRosterData roster);

    /// <summary>Налаштування графіка поточного курсового (профілю): галочки, шапка, підвал.</summary>
    Task<DutyRosterPrefs> GetUserPrefsAsync();

    /// <summary>Запам'ятати налаштування з цього графіка для поточного курсового.</summary>
    Task RememberPrefsAsync(DutyRosterData roster);

    void LogExport(DutyRosterData roster, string filePath);
}
