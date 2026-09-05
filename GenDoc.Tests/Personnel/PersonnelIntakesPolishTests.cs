using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Intakes;
using GenDoc.Services.Personnel;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Login;
using GenDoc.ViewModels.Personnel;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.Tests.Personnel;

public class PersonnelIntakesPolishTests
{
    private static ServiceProvider BuildProvider(TestDb db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db.Factory);
        services.AddSingleton<IAuditLogService>(new FakeAuditLog());
        services.AddSingleton<ICurrentUserContext>(new FakeCurrentUser());
        services.AddSingleton<IUserSettingsService, UserSettingsService>();
        services.AddSingleton<GenDoc.Services.Completeness.ICompletenessService>(_ => TestServices.Completeness(db));
        services.AddSingleton<IIntakeService, IntakeService>();
        services.AddSingleton<ActiveIntakeState>();
        return services.BuildServiceProvider();
    }

    private static int SeedRoot(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
        var root = new OrgNode { Name = "Курс", Depth = 0, SortOrder = 0, Path = "/" };
        ctx.OrgNodes.Add(root);
        ctx.SaveChanges();
        root.Path = $"/{root.Id}/";
        ctx.SaveChanges();
        return root.Id;
    }

    [Fact]
    public async Task Reopen_RefusesWhenAnotherIntakeIsActive()
    {
        using var db = new TestDb();
        var rootId = SeedRoot(db);
        var intakes = BuildProvider(db).GetRequiredService<IIntakeService>();
        var today = DateOnly.FromDateTime(DateTime.Today);

        var first = await intakes.CreateAsync(new IntakeCreateRequest("Набір №1", rootId, today, today.AddMonths(3)));
        await intakes.CloseAsync(new IntakeCloseRequest(first.Id, false, false, null));
        var second = await intakes.CreateAsync(new IntakeCreateRequest("Набір №2", rootId, today, today.AddMonths(3)));

        var conflict = await intakes.GetReopenConflictAsync(first.Id);
        Assert.Contains("Набір №2", conflict);
        await Assert.ThrowsAsync<InvalidOperationException>(() => intakes.ReopenAsync(first.Id));

        await intakes.CloseAsync(new IntakeCloseRequest(second.Id, false, false, null));
        Assert.Null(await intakes.GetReopenConflictAsync(first.Id));
        await intakes.ReopenAsync(first.Id);
        Assert.Equal(first.Id, (await intakes.GetActiveAsync())!.Id);
    }

    [Fact]
    public async Task Save_RoomWithOnlyANumber_IsAValidationError()
    {
        using var db = new TestDb();
        var rootId = SeedRoot(db);
        var personnel = new PersonnelService(db.Factory, new FakeAuditLog(), new FakeCurrentUser());

        var result = await personnel.SaveAsync(new PersonEditModel
        {
            LastName = "КОВАЛЕНКО", FirstName = "Іван", Rank = "солдат", Position = "курсант",
            ServiceNumber = "СН1", OrgNodeId = rootId, RoomNumber = "12"
        });

        Assert.False(result.Success);
        Assert.Equal("Вкажіть і корпус, і номер", result.Errors["Room"]);
    }

    private static PersonCardViewModel Card(PersonEditModel model)
        => new(null!, null!, null!, null!, null!, null!, null!, null!, model, "Курс");

    [Fact]
    public void Fitness_NormalizesCaseAndOffersNoCategory()
    {
        var card = Card(new PersonEditModel { Id = 5, FitnessCategory = "ПРИДАТНИЙ" });
        Assert.Equal("придатний", card.Fitness);
        Assert.Equal(PersonCardViewModel.NoFitnessOption, card.FitnessList[0]);

        var empty = Card(new PersonEditModel { Id = 6 });
        Assert.Equal(PersonCardViewModel.NoFitnessOption, empty.Fitness);
        Assert.Null(PersonCardViewModel.FitnessToSave(PersonCardViewModel.NoFitnessOption));

        var unknown = Card(new PersonEditModel { Id = 7, FitnessCategory = "тимчасово непридатний" });
        Assert.Equal("тимчасово непридатний", unknown.Fitness);
        Assert.Contains("тимчасово непридатний", unknown.FitnessList);
    }

    [Fact]
    public void NewCard_DocumentsTabAsksToSaveFirst_AndCancelCloses()
    {
        var card = Card(new PersonEditModel());
        var closed = false;
        card.CloseRequested += () => closed = true;

        Assert.Equal("Спершу збережіть особу - документи з'являться після збереження", card.DocumentsEmptyNote);
        Assert.False(card.HasMissingDocuments);
        Assert.False(card.CanWorkWithDocuments);

        card.CancelCommand.Execute(null);

        Assert.True(closed);
    }

    [Fact]
    public void Personnel_UsesUkrainianPlurals()
    {
        Assert.Equal("1 запис", PersonnelViewModel.RecordsText(1));
        Assert.Equal("2 записи", PersonnelViewModel.RecordsText(2));
        Assert.Equal("5 записів", PersonnelViewModel.RecordsText(5));
        Assert.Equal("Видалити 3 записи до кошика?", PersonnelViewModel.DeleteConfirmationText(3));
    }

    [Fact]
    public void MoveWarning_DistinguishesLeavingAnIntakeFromCrossingIntakes()
    {
        Assert.Null(NodePickerDialogViewModel.BuildMoveWarning(new int?[] { 1, 1 }, 1, 2));
        Assert.Equal(
            "Увага: 2 із 2 осіб буде винесено з набору у звичайну папку - вони втратять прив'язку до набору.",
            NodePickerDialogViewModel.BuildMoveWarning(new int?[] { 1, 1 }, null, 2));
        Assert.Equal(
            "Увага: 1 із 2 осіб буде переміщено між наборами.",
            NodePickerDialogViewModel.BuildMoveWarning(new int?[] { 1, 2 }, 2, 2));
        Assert.Equal(
            "Увага: 1 із 1 осіб буде додано до набору.",
            NodePickerDialogViewModel.BuildMoveWarning(new int?[] { null }, 2, 1));
    }

    [Fact]
    public void GenerateDialog_OffersGroupWordTemplatesForTheSelection()
    {
        var all = new List<(int Id, string Name)> { (1, "Рапорт") };
        var groups = new List<(int Id, string Name)> { (9, "Наказ про зарахування") };

        var ordered = GenerateDocumentsDialogViewModel.OrderTemplates(all, new List<int>(), null, groups);

        var group = Assert.Single(ordered, t => t.Id == 9);
        Assert.Equal("Групові документи (Word) - один на обраних", group.Group);
        Assert.True(group.IsGroup);
    }

    [Fact]
    public void Wizard_DefaultsToThreeMonths()
    {
        var vm = new IntakeWizardViewModel(null!, null!);
        Assert.Equal(DateTime.Today.AddMonths(3), vm.DateEnd);
    }

    private sealed class FakeUnlock : IDatabaseUnlockService
    {
        public bool DatabaseExists => true;
        public string DatabasePath => @"C:\GenDoc\gendoc.db";
        public bool TryUnlock(string password, out string? errorMessage) { errorMessage = null; return true; }
    }

    private sealed class OneProfile : IUserProfileService
    {
        public List<UserProfileListItem> GetActiveProfiles() => new() { new UserProfileListItem(7, "Курсовий") };
        public bool TryLogin(int userProfileId, string password, out string? errorMessage) { errorMessage = null; return true; }
        public bool TryCreateProfile(string fullName, string password, out string? errorMessage) { errorMessage = null; return true; }
    }

    private sealed class NoSchema : IDatabaseSchemaInitializer
    {
        public void EnsureInitialized() { }
    }

    [Fact]
    public async Task Login_SingleProfile_IsPreselected()
    {
        var vm = new LoginViewModel(new FakeUnlock(), new OneProfile(), new NoSchema()) { DatabasePassword = "pwd" };

        await vm.UnlockDatabaseCommand.ExecuteAsync(null);

        Assert.Equal(LoginStage.ProfileSelect, vm.Stage);
        Assert.Equal(7, vm.SelectedProfile!.Id);
        Assert.False(vm.IsBusy);
    }
}
