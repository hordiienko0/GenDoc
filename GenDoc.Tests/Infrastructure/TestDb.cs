using GenDoc.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GenDoc.Tests.Infrastructure;

public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly List<string> _sql = new();
    private readonly bool _recordSql;

    public TestDb(bool recordSql = false)
    {
        _recordSql = recordSql;
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var seed = new TestAppDbContext(_connection, null);
        seed.Database.EnsureCreated();

        Factory = new TestFactory(_connection, recordSql ? Record : null);
    }

    public IDbContextFactory<AppDbContext> Factory { get; }

    public IReadOnlyList<string> Sql
    {
        get { lock (_sql) return _sql.ToList(); }
    }

    public int SelectCount(string table)
        => Sql.Count(s => s.Contains("SELECT", StringComparison.Ordinal)
                       && s.Contains($"\"{table}\"", StringComparison.Ordinal));

    public void ClearSql()
    {
        lock (_sql) _sql.Clear();
    }

    private void Record(string message)
    {
        if (!_recordSql) return;
        lock (_sql) _sql.Add(message);
    }

    public AppDbContext NewContext() => new TestAppDbContext(_connection, null);

    public void Dispose() => _connection.Dispose();

    private sealed class TestFactory : IDbContextFactory<AppDbContext>
    {
        private readonly SqliteConnection _connection;
        private readonly Action<string>? _log;

        public TestFactory(SqliteConnection connection, Action<string>? log)
        {
            _connection = connection;
            _log = log;
        }

        public AppDbContext CreateDbContext() => new TestAppDbContext(_connection, _log);
    }

    private sealed class TestAppDbContext : AppDbContext
    {
        private readonly SqliteConnection _connection;
        private readonly Action<string>? _log;

        public TestAppDbContext(SqliteConnection connection, Action<string>? log)
            : base(new NullPasswordProvider())
        {
            _connection = connection;
            _log = log;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite(_connection);

            if (_log is not null)
            {
                optionsBuilder.LogTo(
                    _log,
                    new[] { Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuted },
                    LogLevel.Information);
            }
        }
    }

    private sealed class NullPasswordProvider : IDbPasswordProvider
    {
        public string? Password => null;
        public void SetPassword(string password) { }
        public void Clear() { }
    }
}
