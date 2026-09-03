using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Services.Intakes;
using GenDoc.Services.Personnel;
using GenDoc.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.Tests.Intakes;

public class IntakeCreationDefaultsTests
{
    private static ServiceProvider BuildProvider(TestDb db, FakeCurrentUser user)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db.Factory);
        services.AddSingleton<IAuditLogService>(new FakeAuditLog());
        services.AddSingleton<ICurrentUserContext>(user);
        services.AddSingleton<IUserSettingsService, UserSettingsService>();
        services.AddSingleton<GenDoc.Services.Completeness.ICompletenessService>(
            _ => TestServices.Completeness(db));
        services.AddSingleton<IIntakeService, IntakeService>();
        services.AddSingleton<ActiveIntakeState>();
        return services.BuildServiceProvider();
    }

    private static int SeedRoot(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        var root = new OrgNode { Name = "Курс", Depth = 0, SortOrder = 0, Path = "/" };
        ctx.OrgNodes.Add(root);
        ctx.SaveChanges();
        root.Path = $"/{root.Id}/";
        ctx.SaveChanges();
        return root.Id;
    }

    private static int SeedProfile(TestDb db, string fullName = "Тест Тестович")
    {
        using var ctx = db.Factory.CreateDbContext();
        var profile = new UserProfile
        {
            FullName = fullName,
            PasswordHash = "не-використовується",
            CreatedAt = DateTime.Now
        };
        ctx.Users.Add(profile);
        ctx.SaveChanges();
        return profile.Id;
    }

    private static async Task<(Intake Intake, ServiceProvider Provider)> CreateIntakeAsync(TestDb db)
    {
        var rootId = SeedRoot(db);
        var user = new FakeCurrentUser(SeedProfile(db));
        var provider = BuildProvider(db, user);

        var intake = await provider.GetRequiredService<IIntakeService>().CreateAsync(
            new IntakeCreateRequest(
                "Набір №1", rootId,
                DateOnly.FromDateTime(DateTime.Today),
                DateOnly.FromDateTime(DateTime.Today.AddMonths(3))));

        return (intake, provider);
    }

    [Fact]
    public async Task ANewIntakeGetsAFolderForEveryFitnessCategory()
    {
        using var db = new TestDb();
        var (intake, _) = await CreateIntakeAsync(db);

        using var ctx = db.Factory.CreateDbContext();
        var names = ctx.OrgNodes
            .Where(n => n.IntakeId == intake.Id)
            .Select(n => n.Name)
            .ToList();

        Assert.Contains(IntakeFolderNames.All, names);
        foreach (var folder in IntakeFitnessFolders.AllFolderNames)
            Assert.Contains(folder, names);
    }

    [Fact]
    public async Task TheCategoryFoldersSitUnderTheAllFolder()
    {
        using var db = new TestDb();
        var (intake, _) = await CreateIntakeAsync(db);

        using var ctx = db.Factory.CreateDbContext();
        var nodes = ctx.OrgNodes.Where(n => n.IntakeId == intake.Id).ToList();
        var all = nodes.Single(n => n.Name == IntakeFolderNames.All);

        foreach (var folder in IntakeFitnessFolders.AllFolderNames)
        {
            var node = nodes.Single(n => n.Name == folder);
            Assert.Equal(all.Id, node.ParentId);
            Assert.Equal(all.Depth + 1, node.Depth);
            Assert.Equal($"{all.Path}{node.Id}/", node.Path);
        }
    }

    [Fact]
    public async Task AnIntakeThatHasAlreadyStartedIsTheActiveOne()
    {
        using var db = new TestDb();
        var (intake, provider) = await CreateIntakeAsync(db);

        var active = await provider.GetRequiredService<IIntakeService>().GetActiveAsync();

        Assert.Equal(intake.Id, active!.Id);
    }

    [Fact]
    public async Task TheActiveIntakeStateFollowsTheNewIntake()
    {
        using var db = new TestDb();
        var (intake, provider) = await CreateIntakeAsync(db);

        var state = provider.GetRequiredService<ActiveIntakeState>();

        Assert.True(state.HasActive);
        Assert.Equal(intake.Id, state.Current!.Id);
    }

    private static async Task<(int RecipientId, int IntakeId)> AddPersonAsync(
        TestDb db, PersonnelService personnel, int intakeId, string? fitness)
    {
        using var ctx = db.Factory.CreateDbContext();
        var allNode = ctx.OrgNodes.Single(n => n.IntakeId == intakeId && n.Name == IntakeFolderNames.All);

        var result = await personnel.SaveAsync(new PersonEditModel
        {
            LastName = "ТЕСТЕНКО",
            FirstName = "Тест",
            Rank = "солдат",
            Position = "курсант",
            ServiceNumber = "ТЕСТ-ПАПКА-001",
            FitnessCategory = fitness,
            OrgNodeId = allNode.Id,
            IntakeId = intakeId
        });

        Assert.True(result.Success);
        return (result.Id, intakeId);
    }

    private static string FolderOf(TestDb db, int recipientId)
    {
        using var ctx = db.Factory.CreateDbContext();
        var person = ctx.Recipients.Single(r => r.Id == recipientId);
        return ctx.OrgNodes.Single(n => n.Id == person.OrgNodeId!.Value).Name;
    }

    [Fact]
    public async Task ANewPersonLandsInTheFolderForTheirCategory()
    {
        using var db = new TestDb();
        var (intake, _) = await CreateIntakeAsync(db);
        var personnel = new PersonnelService(db.Factory, new FakeAuditLog(), new FakeCurrentUser());

        var (id, _) = await AddPersonAsync(db, personnel, intake.Id, "придатний");

        Assert.Equal(IntakeFolderNames.Fit, FolderOf(db, id));
    }

    [Fact]
    public async Task ChangingTheCategoryMovesThePersonToTheMatchingFolder()
    {
        using var db = new TestDb();
        var (intake, _) = await CreateIntakeAsync(db);
        var personnel = new PersonnelService(db.Factory, new FakeAuditLog(), new FakeCurrentUser());
        var (id, _) = await AddPersonAsync(db, personnel, intake.Id, "придатний");

        var model = await personnel.GetForEditAsync(id);
        model!.FitnessCategory = "обмежено придатний";
        await personnel.SaveAsync(model);

        Assert.Equal(IntakeFolderNames.LimitedFit, FolderOf(db, id));
    }

    [Fact]
    public async Task ClearingTheCategoryMovesThePersonToTheNoCategoryFolder()
    {
        using var db = new TestDb();
        var (intake, _) = await CreateIntakeAsync(db);
        var personnel = new PersonnelService(db.Factory, new FakeAuditLog(), new FakeCurrentUser());
        var (id, _) = await AddPersonAsync(db, personnel, intake.Id, "придатний");

        var model = await personnel.GetForEditAsync(id);
        model!.FitnessCategory = null;
        await personnel.SaveAsync(model);

        Assert.Equal(IntakeFolderNames.NoCategory, FolderOf(db, id));
    }

    [Fact]
    public async Task AnUnfitPersonGetsTheirOwnFolder()
    {
        using var db = new TestDb();
        var (intake, _) = await CreateIntakeAsync(db);
        var personnel = new PersonnelService(db.Factory, new FakeAuditLog(), new FakeCurrentUser());
        var (id, _) = await AddPersonAsync(db, personnel, intake.Id, "придатний");

        var model = await personnel.GetForEditAsync(id);
        model!.FitnessCategory = "непридатний";
        await personnel.SaveAsync(model);

        Assert.Equal(IntakeFolderNames.Unfit, FolderOf(db, id));
    }

    [Fact]
    public async Task EditingSomethingElseDoesNotDragThePersonBack()
    {
        using var db = new TestDb();
        var (intake, _) = await CreateIntakeAsync(db);
        var personnel = new PersonnelService(db.Factory, new FakeAuditLog(), new FakeCurrentUser());
        var (id, _) = await AddPersonAsync(db, personnel, intake.Id, "придатний");

        int customNodeId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var all = ctx.OrgNodes.Single(n => n.IntakeId == intake.Id && n.Name == IntakeFolderNames.All);
            var custom = new OrgNode
            {
                Name = "Перша група", ParentId = all.Id, Depth = all.Depth + 1,
                SortOrder = 9, IntakeId = intake.Id
            };
            ctx.OrgNodes.Add(custom);
            ctx.SaveChanges();
            custom.Path = $"{all.Path}{custom.Id}/";
            ctx.SaveChanges();
            customNodeId = custom.Id;
        }

        await personnel.MoveManyAsync(new[] { id }, customNodeId, IntakeFolderNames.Fit);

        var model = await personnel.GetForEditAsync(id);
        model!.Position = "старший курсант";
        await personnel.SaveAsync(model);

        Assert.Equal("Перша група", FolderOf(db, id));
    }
}
