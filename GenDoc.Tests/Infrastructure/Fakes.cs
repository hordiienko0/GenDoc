using GenDoc.Data;
using GenDoc.Services;
using GenDoc.Services.Documents;

namespace GenDoc.Tests.Infrastructure;

public sealed class FakeAuditLog : IAuditLogService
{
    public List<string> Entries { get; } = new();

    public void LogCreate(AppDbContext db, string entityName, int entityId, string? newValue = null, string? details = null)
        => Entries.Add($"create:{entityName}:{entityId}");

    public void LogUpdate(AppDbContext db, string entityName, int entityId, string? oldValue, string? newValue, string? details = null)
        => Entries.Add($"update:{entityName}:{entityId}");

    public void LogDelete(AppDbContext db, string entityName, int entityId, string? oldValue = null, string? details = null)
        => Entries.Add($"delete:{entityName}:{entityId}");

    public void LogExport(AppDbContext db, string entityName, int count, string? details = null)
        => Entries.Add($"export:{entityName}:{count}");

    public void LogImport(AppDbContext db, string entityName, int count, string? details = null)
        => Entries.Add($"import:{entityName}:{count}");

    public void LogGenerate(AppDbContext db, string entityName, int entityId, string? details = null)
        => Entries.Add($"generate:{entityName}:{entityId}");

    public void Log(AppDbContext db, string action, string entityName, int entityId, string? oldValue = null, string? newValue = null, string? details = null)
        => Entries.Add($"{action}:{entityName}:{entityId}");
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

    public Task OpenAsync(string fileName, byte[] content)
    {
        Opened.Add((fileName, content));
        return Task.CompletedTask;
    }

    public Task PrintAsync(string fileName, byte[] content)
    {
        Printed.Add((fileName, content));
        return Task.CompletedTask;
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
