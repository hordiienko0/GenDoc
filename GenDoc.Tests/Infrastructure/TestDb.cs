using GenDoc.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Tests.Infrastructure;

// Незашифрована in-memory база для тестів сервісів. З'єднання мусить лишатись
// відкритим весь час життя TestDb — SQLite знищує in-memory базу, щойно
// закривається останнє з'єднання до неї.
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public TestDb()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var seed = new TestAppDbContext(_connection);
        seed.Database.EnsureCreated();

        Factory = new TestFactory(_connection);
    }

    public IDbContextFactory<AppDbContext> Factory { get; }

    public AppDbContext NewContext() => new TestAppDbContext(_connection);

    public void Dispose() => _connection.Dispose();

    private sealed class TestFactory : IDbContextFactory<AppDbContext>
    {
        private readonly SqliteConnection _connection;
        public TestFactory(SqliteConnection connection) => _connection = connection;
        public AppDbContext CreateDbContext() => new TestAppDbContext(_connection);
    }

    // Перевизначає OnConfiguring і НЕ викликає base — так гілка з паролем
    // і DbPaths.DatabasePath не виконується взагалі.
    private sealed class TestAppDbContext : AppDbContext
    {
        private readonly SqliteConnection _connection;

        public TestAppDbContext(SqliteConnection connection)
            : base(new NullPasswordProvider()) => _connection = connection;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseSqlite(_connection);
    }

    private sealed class NullPasswordProvider : IDbPasswordProvider
    {
        public string? Password => null;
        public void SetPassword(string password) { }
        public void Clear() { }
    }
}
