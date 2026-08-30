using System.IO;
using GenDoc.Data;
using Microsoft.Data.Sqlite;

namespace GenDoc.Services
{
    public class DatabaseUnlockService : IDatabaseUnlockService
    {
        private readonly IDbPasswordProvider _passwordProvider;

        public DatabaseUnlockService(IDbPasswordProvider passwordProvider)
        {
            _passwordProvider = passwordProvider;
            SqlCipherBootstrapper.EnsureInitialized();
        }

        public string DatabasePath => DbPaths.DatabasePath;

        public bool DatabaseExists => File.Exists(DbPaths.DatabasePath);

        public bool TryUnlock(string password, out string? errorMessage)
        {
            errorMessage = null;

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = DbPaths.DatabasePath,
                Password = password
            }.ToString();

            using var connection = new SqliteConnection(connectionString);
            try
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT count(*) FROM sqlite_master;";
                command.ExecuteScalar();
            }
            catch (SqliteException)
            {
                errorMessage = "Невірний пароль або пошкоджена база даних.";
                return false;
            }
            finally
            {
                connection.Close();
            }

            _passwordProvider.SetPassword(password);
            return true;
        }
    }
}
