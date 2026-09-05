using GenDoc.Data;
using GenDoc.Services;
using GenDoc.Services.Documents;

namespace GenDoc.Tests.Infrastructure;

public sealed class FakeAuditLog : IAuditLogService
{
    public List<string> Entries { get; } = new();
    public List<string?> Details { get; } = new();

    private void Add(string entry, string? details)
    {
        Entries.Add(entry);
        Details.Add(details);
    }

    public void LogCreate(AppDbContext db, string entityName, int entityId, string? newValue = null, string? details = null)
        => Add($"create:{entityName}:{entityId}", details ?? newValue);

    public void LogUpdate(AppDbContext db, string entityName, int entityId, string? oldValue, string? newValue, string? details = null)
        => Add($"update:{entityName}:{entityId}", details ?? newValue ?? oldValue);

    public void LogDelete(AppDbContext db, string entityName, int entityId, string? oldValue = null, string? details = null)
        => Add($"delete:{entityName}:{entityId}", details ?? oldValue);

    public void LogExport(AppDbContext db, string entityName, int count, string? details = null)
        => Add($"export:{entityName}:{count}", details);

    public void LogImport(AppDbContext db, string entityName, int count, string? details = null)
        => Add($"import:{entityName}:{count}", details);

    public void LogGenerate(AppDbContext db, string entityName, int entityId, string? details = null)
        => Add($"generate:{entityName}:{entityId}", details);

    public void Log(AppDbContext db, string action, string entityName, int entityId, string? oldValue = null, string? newValue = null, string? details = null)
        => Add($"{action}:{entityName}:{entityId}", details ?? newValue ?? oldValue);
}

public sealed class FakeCurrentUser : ICurrentUserContext
{
    public FakeCurrentUser(int userId = 1, string fullName = "Тест Тестович")
    {
        CurrentUserId = userId;
        CurrentUserFullName = fullName;
    }

    public int? CurrentUserId { get; private set; }
    public string? CurrentUserFullName { get; private set; }

    public void SetCurrentUser(int userId, string fullName)
    {
        CurrentUserId = userId;
        CurrentUserFullName = fullName;
    }

    public void Clear()
    {
        CurrentUserId = null;
        CurrentUserFullName = null;
    }
}

public sealed class FakeTempFiles : ISecureTempFileService
{
    public List<(string FileName, byte[] Content)> Opened { get; } = new();
    public List<(string FileName, byte[] Content)> Printed { get; } = new();
    public bool PrintStarts { get; set; } = true;

    public Task OpenAsync(string fileName, byte[] content)
    {
        Opened.Add((fileName, content));
        return Task.CompletedTask;
    }

    public Task<bool> PrintAsync(string fileName, byte[] content)
    {
        if (PrintStarts) Printed.Add((fileName, content));
        return Task.FromResult(PrintStarts);
    }

    public Task CleanupAsync() => Task.CompletedTask;
}

public sealed class FakeIntakeAccessor : GenDoc.Services.Completeness.IIntakeServiceAccessor
{
    private readonly GenDoc.Models.Intake? _intake;

    public FakeIntakeAccessor() { }

    public FakeIntakeAccessor(GenDoc.Models.Intake intake) => _intake = intake;

    public GenDoc.Models.Intake? ActiveIntake => _intake;
}
