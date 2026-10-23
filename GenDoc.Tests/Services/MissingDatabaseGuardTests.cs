using GenDoc.Services;
using GenDoc.ViewModels.Login;

namespace GenDoc.Tests.Services;

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
    public async Task MissingDatabase_AndUserDeclines_DoesNotUnlockOrInitialize()
    {
        var unlock = new FakeUnlock { Exists = false };
        var schema = new FakeSchema();
        var confirmed = false;

        var vm = Vm(unlock, schema, _ => { confirmed = true; return false; });
        vm.DatabasePassword = "pwd";
        vm.DatabasePasswordConfirm = "pwd";

        await vm.UnlockDatabaseCommand.ExecuteAsync(null);

        Assert.True(confirmed, "Користувача не спитали про створення нової бази.");
        Assert.Equal(0, unlock.UnlockCalls);
        Assert.Equal(0, schema.Calls);
        Assert.Equal(LoginStage.DatabasePassword, vm.Stage);
    }

    [Fact]
    public async Task MissingDatabase_AndUserAgrees_ProceedsToProfileSelect()
    {
        var unlock = new FakeUnlock { Exists = false };
        var schema = new FakeSchema();

        var vm = Vm(unlock, schema, _ => true);
        vm.DatabasePassword = "pwd";
        vm.DatabasePasswordConfirm = "pwd";

        await vm.UnlockDatabaseCommand.ExecuteAsync(null);

        Assert.Equal(1, unlock.UnlockCalls);
        // У щойно створеній базі профілів немає - одразу форма створення профілю.
        Assert.Equal(LoginStage.CreateProfile, vm.Stage);
    }

    [Fact]
    public async Task MissingDatabase_PasswordsDiffer_NothingIsCreated()
    {
        var unlock = new FakeUnlock { Exists = false };
        var schema = new FakeSchema();
        var asked = false;

        var vm = Vm(unlock, schema, _ => { asked = true; return true; });
        vm.DatabasePassword = "pwd";
        vm.DatabasePasswordConfirm = "pdw";

        await vm.UnlockDatabaseCommand.ExecuteAsync(null);

        Assert.True(vm.IsNewDatabase);
        Assert.False(asked);
        Assert.Equal(0, unlock.UnlockCalls);
        Assert.Equal(0, schema.Calls);
        Assert.Equal("Паролі не збігаються.", vm.ErrorMessage);
        Assert.Equal(LoginStage.DatabasePassword, vm.Stage);
    }

    [Fact]
    public void ExistingDatabase_AsksForThePasswordOnce()
    {
        var vm = Vm(new FakeUnlock { Exists = true }, new FakeSchema(), _ => true);

        Assert.False(vm.IsNewDatabase);
        Assert.Equal("Розблокувати", vm.UnlockButtonText);
    }

    [Fact]
    public async Task CreatingAProfile_LogsStraightIn()
    {
        var vm = Vm(new FakeUnlock { Exists = true }, new FakeSchema(), _ => true);
        vm.DatabasePassword = "pwd";
        await vm.UnlockDatabaseCommand.ExecuteAsync(null);
        var closed = false;
        vm.RequestClose += (_, _) => closed = true;

        vm.NewProfileFullName = "Петренко Іван Іванович";
        vm.NewProfilePassword = "1234";
        vm.NewProfilePasswordConfirm = "1234";
        vm.CreateProfileCommand.Execute(null);

        Assert.True(closed);
        Assert.True(vm.LoginResult);
    }

    [Fact]
    public async Task ExistingDatabase_AsksNothing()
    {
        var unlock = new FakeUnlock { Exists = true };
        var asked = false;

        var vm = Vm(unlock, new FakeSchema(), _ => { asked = true; return true; });
        vm.DatabasePassword = "pwd";

        await vm.UnlockDatabaseCommand.ExecuteAsync(null);

        Assert.False(asked);
        Assert.Equal(LoginStage.CreateProfile, vm.Stage);
    }

    [Fact]
    public async Task ConfirmationMessage_NamesTheFullPath()
    {
        var unlock = new FakeUnlock { Exists = false };
        string? shown = null;

        var vm = Vm(unlock, new FakeSchema(), message => { shown = message; return false; });
        vm.DatabasePassword = "pwd";
        vm.DatabasePasswordConfirm = "pwd";

        await vm.UnlockDatabaseCommand.ExecuteAsync(null);

        Assert.NotNull(shown);
        Assert.Contains(unlock.DatabasePath, shown!, StringComparison.Ordinal);
    }
}
