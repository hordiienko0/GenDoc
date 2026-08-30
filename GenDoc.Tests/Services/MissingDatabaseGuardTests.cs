using GenDoc.Services;
using GenDoc.ViewModels.Login;

namespace GenDoc.Tests.Services;

/// <summary>
/// У рядок з'єднання не заданий Mode, а типовий - ReadWriteCreate, і перевірки
/// File.Exists не було ніде. Тож коли файла бази немає, Open() СТВОРЮЄ нову
/// порожню базу на введеному паролі, «SELECT count(*) FROM sqlite_master»
/// успішно повертає 0, і вхід виглядає вдалим.
///
/// Небезпечно це тим, що DbPaths.DatabasePath - це AppContext.BaseDirectory,
/// тобто bin\Debug\net8.0-windows: одне «dotnet clean», інший ярлик або свіжий
/// клон - і оператор входить своїм звичним паролем у порожню базу, не отримавши
/// жодного попередження, що стару не знайдено (аудит 2026-08-28).
///
/// Перший запуск лишається можливим - він просто перестає бути мовчазним.
/// </summary>
public class MissingDatabaseGuardTests
{
    private sealed class FakeUnlock : IDatabaseUnlockService
    {
        public bool Exists { get; init; }
        public int UnlockCalls { get; private set; }

        public bool DatabaseExists => Exists;
        public string DatabasePath => @"C:\GenDoc\gendoc.db";

        public bool TryUnlock(string password, out string? errorMessage)
        {
            UnlockCalls++;
            errorMessage = null;
            return true;
        }
    }

    private sealed class FakeProfiles : IUserProfileService
    {
        public List<UserProfileListItem> GetActiveProfiles() => new();
        public bool TryLogin(int userProfileId, string password, out string? errorMessage)
        { errorMessage = null; return true; }
        public bool TryCreateProfile(string fullName, string password, out string? errorMessage)
        { errorMessage = null; return true; }
    }

    private sealed class FakeSchema : IDatabaseSchemaInitializer
    {
        public int Calls { get; private set; }
        public void EnsureInitialized() => Calls++;
    }

    private static LoginViewModel Vm(FakeUnlock unlock, FakeSchema schema, Func<string, bool> confirm)
        => new(unlock, new FakeProfiles(), schema) { ConfirmCreateDatabase = confirm };

    [Fact]
    public void MissingDatabase_AndUserDeclines_DoesNotUnlockOrInitialize()
    {
        var unlock = new FakeUnlock { Exists = false };
        var schema = new FakeSchema();
        var confirmed = false;

        var vm = Vm(unlock, schema, _ => { confirmed = true; return false; });
        vm.DatabasePassword = "pwd";

        vm.UnlockDatabaseCommand.Execute(null);

        Assert.True(confirmed, "Користувача не спитали про створення нової бази.");
        Assert.Equal(0, unlock.UnlockCalls);
        Assert.Equal(0, schema.Calls);
        Assert.Equal(LoginStage.DatabasePassword, vm.Stage);
    }

    [Fact]
    public void MissingDatabase_AndUserAgrees_ProceedsToProfileSelect()
    {
        var unlock = new FakeUnlock { Exists = false };
        var schema = new FakeSchema();

        var vm = Vm(unlock, schema, _ => true);
        vm.DatabasePassword = "pwd";

        vm.UnlockDatabaseCommand.Execute(null);

        Assert.Equal(1, unlock.UnlockCalls);
        Assert.Equal(LoginStage.ProfileSelect, vm.Stage);
    }

    // Звичайний вхід у наявну базу нічого не питає.
    [Fact]
    public void ExistingDatabase_AsksNothing()
    {
        var unlock = new FakeUnlock { Exists = true };
        var asked = false;

        var vm = Vm(unlock, new FakeSchema(), _ => { asked = true; return true; });
        vm.DatabasePassword = "pwd";

        vm.UnlockDatabaseCommand.Execute(null);

        Assert.False(asked);
        Assert.Equal(LoginStage.ProfileSelect, vm.Stage);
    }

    // Повідомлення мусить називати ПОВНИЙ шлях: без нього оператор не зрозуміє,
    // що застосунок дивиться не туди, куди він думає.
    [Fact]
    public void ConfirmationMessage_NamesTheFullPath()
    {
        var unlock = new FakeUnlock { Exists = false };
        string? shown = null;

        var vm = Vm(unlock, new FakeSchema(), message => { shown = message; return false; });
        vm.DatabasePassword = "pwd";

        vm.UnlockDatabaseCommand.Execute(null);

        Assert.NotNull(shown);
        Assert.Contains(unlock.DatabasePath, shown!, StringComparison.Ordinal);
    }
}
