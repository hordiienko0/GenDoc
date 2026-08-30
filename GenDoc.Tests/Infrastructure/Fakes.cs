using GenDoc.Data;
using GenDoc.Services;
using GenDoc.Services.Documents;

namespace GenDoc.Tests.Infrastructure;

// Пише назви дій у список - тест може перевірити, що аудит-запис зроблено,
// не тягнучи справжню таблицю AuditLog.
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

// Нічого не пише на диск і нічого не запускає - лише запам'ятовує, що просили відкрити.
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

// Заглушка активного заїзду для CompletenessService - тестам, що не перевіряють
// саме прив'язку до заїзду, достатньо стабільного null.
public sealed class FakeIntakeAccessor : GenDoc.Services.Completeness.IIntakeServiceAccessor
{
    private readonly GenDoc.Models.Intake? _intake;

    public FakeIntakeAccessor() { }

    // Бейдж навігації читає активний набір саме звідси, тож для тестів на нього
    // фейк мусить уміти його віддати - інакше GetBadgeCountAsync виходить на
    // першому ж рядку й тест нічого не доводить.
    public FakeIntakeAccessor(GenDoc.Models.Intake intake) => _intake = intake;

    public GenDoc.Models.Intake? ActiveIntake => _intake;
}
