namespace GenDoc.Services.Duty;

public interface IEveningCheckService
{
    /// <summary>Усе дерево папок (батьки перед дітьми) з тим, кого кожна папка приведе в блок - те саме, що в графіку чергувань.</summary>
    Task<IReadOnlyList<DutyFolderOption>> GetFoldersAsync();

    /// <summary>Люди за Id - за старшинством звань і абеткою, як друкуються в блоці.</summary>
    Task<IReadOnlyList<EveningCheckPersonOption>> GetPeopleAsync(IReadOnlyCollection<int> recipientIds);

    /// <summary>«Начальник курсів П та ПК:  звання  І. ПРІЗВИЩЕ» з налаштувань частини.</summary>
    Task<string> GetDefaultSignatureAsync();

    void LogExport(EveningCheckData data, string filePath);
}
