using GenDoc.Data;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services.Duty;

public class EveningCheckService : IEveningCheckService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IAuditLogService _auditLog;
    private readonly IDutyRosterService _rosters;

    public EveningCheckService(IDbContextFactory<AppDbContext> dbFactory, IAuditLogService auditLog, IDutyRosterService rosters)
    {
        _dbFactory = dbFactory;
        _auditLog = auditLog;
        _rosters = rosters;
    }

    public Task<IReadOnlyList<DutyFolderOption>> GetFoldersAsync() => _rosters.GetFoldersAsync();

    public async Task<IReadOnlyList<EveningCheckPersonOption>> GetPeopleAsync(IReadOnlyCollection<int> recipientIds)
    {
        if (recipientIds.Count == 0) return Array.Empty<EveningCheckPersonOption>();

        using var db = _dbFactory.CreateDbContext();
        var ids = recipientIds.ToList();
        var people = await db.Recipients.AsNoTracking().Where(r => ids.Contains(r.Id)).ToListAsync();

        return RosterOrdering.Apply(people)
            .Select(r => new EveningCheckPersonOption(r.Id, r.Rank?.Trim() ?? string.Empty,
                EveningCheckText.FullName(r.LastName, r.FirstName, r.MiddleName), !FitnessCategoryHelper.IsRegular(r.FitnessCategory)))
            .ToList();
    }

    public async Task<string> GetDefaultSignatureAsync()
    {
        using var db = _dbFactory.CreateDbContext();
        var org = await db.OrganizationSettings.AsNoTracking().FirstOrDefaultAsync();
        return EveningCheckText.Signature(org);
    }

    public void LogExport(EveningCheckData data, string filePath)
    {
        using var db = _dbFactory.CreateDbContext();
        _auditLog.LogExport(db, "EveningCheck", data.PeopleCount,
            $"Список вечірньої перевірки {DutyRosterText.PeriodLabel(data.DateFrom, data.DateTo)}, "
            + $"{data.PeopleCount} {PluralHelper.Pluralize(data.PeopleCount, "особа", "особи", "осіб")} → {System.IO.Path.GetFileName(filePath)}");
        db.SaveChanges();
    }
}
