using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Services.Intakes;
using GenDoc.Services.OrgTree;
using GenDoc.Services.Personnel;
using GenDoc.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.Tests.Intakes;

public class IntakeCloseTests
{
    private static ServiceProvider BuildProvider(TestDb db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db.Factory);
        services.AddSingleton<IAuditLogService>(new FakeAuditLog());
        services.AddSingleton<ICurrentUserContext>(new FakeCurrentUser());
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

    private static async Task<(Intake Intake, int PersonId, ServiceProvider Provider)> ArrangeAsync(
        TestDb db, bool withRoom = false)
    {
        var rootId = SeedRoot(db);
        var provider = BuildProvider(db);
        var intakes = provider.GetRequiredService<IIntakeService>();

        var intake = await intakes.CreateAsync(new IntakeCreateRequest(
            "Набір №1", rootId,
            DateOnly.FromDateTime(DateTime.Today),
            DateOnly.FromDateTime(DateTime.Today.AddMonths(3))));

        int allNodeId;
        using (var ctx = db.Factory.CreateDbContext())
            allNodeId = ctx.OrgNodes.Single(n => n.IntakeId == intake.Id && n.Name == IntakeFolderNames.All).Id;

        var personnel = new PersonnelService(db.Factory, new FakeAuditLog(), new FakeCurrentUser());
        var saved = await personnel.SaveAsync(new PersonEditModel
        {
            LastName = "КОВАЛЕНКО",
            FirstName = "Іван",
            Rank = "солдат",
            Position = "курсант",
            ServiceNumber = "ЗАКР-001",
            FitnessCategory = "придатний",
            RoomBuilding = withRoom ? "1" : null,
            RoomNumber = withRoom ? "101" : null,
            OrgNodeId = allNodeId,
            IntakeId = intake.Id
        });
        Assert.True(saved.Success);

        return (intake, saved.Id, provider);
    }

    private static Recipient PersonOf(TestDb db, int id)
    {
        using var ctx = db.Factory.CreateDbContext();
        return ctx.Recipients.Single(r => r.Id == id);
    }

    private static OrgNode NodeOf(TestDb db, int id)
    {
        using var ctx = db.Factory.CreateDbContext();
        return ctx.OrgNodes.Single(n => n.Id == id);
    }

    [Fact]
    public async Task MovingGraduatesOutOfTheIntakeDetachesThemFromIt()
    {
        using var db = new TestDb();
        var (intake, personId, provider) = await ArrangeAsync(db);

        await provider.GetRequiredService<IIntakeService>().CloseAsync(
            new IntakeCloseRequest(intake.Id, ReleaseRooms: false, MovePersonnel: true, TargetNodeId: -1));

        var person = PersonOf(db, personId);
        Assert.Null(person.IntakeId);
        var folder = NodeOf(db, person.OrgNodeId!.Value);
        Assert.Null(folder.IntakeId);
        Assert.Equal("Набір №1", folder.Name);
    }

    [Fact]
    public async Task ChangingFitnessAfterGraduationDoesNotDragThePersonBackIntoTheClosedIntake()
    {
        using var db = new TestDb();
        var (intake, personId, provider) = await ArrangeAsync(db);
        await provider.GetRequiredService<IIntakeService>().CloseAsync(
            new IntakeCloseRequest(intake.Id, ReleaseRooms: false, MovePersonnel: true, TargetNodeId: -1));
        var graduateFolderId = PersonOf(db, personId).OrgNodeId!.Value;

        var personnel = new PersonnelService(db.Factory, new FakeAuditLog(), new FakeCurrentUser());
        var model = await personnel.GetForEditAsync(personId);
        model!.FitnessCategory = "обмежено придатний";
        var result = await personnel.SaveAsync(model);

        Assert.True(result.Success);
        Assert.Equal(graduateFolderId, PersonOf(db, personId).OrgNodeId);
    }

    [Fact]
    public async Task APersonWhoseFolderIsOutsideTheirIntakeIsNotMovedIntoItsCategoryFolder()
    {
        using var db = new TestDb();
        var (intake, personId, _) = await ArrangeAsync(db);

        int outsideId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var root = ctx.OrgNodes.Single(n => n.ParentId == null);
            var outside = new OrgNode { Name = "Окремо", ParentId = root.Id, Depth = 1, SortOrder = 5 };
            ctx.OrgNodes.Add(outside);
            ctx.SaveChanges();
            outside.Path = $"{root.Path}{outside.Id}/";
            ctx.Recipients.Single(r => r.Id == personId).OrgNodeId = outside.Id;
            ctx.SaveChanges();
            outsideId = outside.Id;
        }

        var personnel = new PersonnelService(db.Factory, new FakeAuditLog(), new FakeCurrentUser());
        var model = await personnel.GetForEditAsync(personId);
        model!.FitnessCategory = "непридатний";
        await personnel.SaveAsync(model);

        var person = PersonOf(db, personId);
        Assert.Equal(intake.Id, person.IntakeId);
        Assert.Equal(outsideId, person.OrgNodeId);
    }

    [Fact]
    public async Task IntakeCardAndTreeAgreeOnZeroPeopleAfterGraduation()
    {
        using var db = new TestDb();
        var (intake, _, provider) = await ArrangeAsync(db);
        var intakes = provider.GetRequiredService<IIntakeService>();

        await intakes.CloseAsync(
            new IntakeCloseRequest(intake.Id, ReleaseRooms: false, MovePersonnel: true, TargetNodeId: -1));

        var overview = (await intakes.GetOverviewsAsync()).Single(o => o.Id == intake.Id);
        var tree = new OrgTreeService(db.Factory, new FakeAuditLog(), new FakeCurrentUser());
        var inBranch = await tree.CountPeopleInBranchAsync(overview.RootOrgNodeId);

        Assert.Equal(0, inBranch);
        Assert.Equal(inBranch, overview.PeopleCount);
    }

    [Fact]
    public async Task RoomsAreStillReleasedWhenPeopleAreMovedInTheSameClose()
    {
        using var db = new TestDb();
        var (intake, personId, provider) = await ArrangeAsync(db, withRoom: true);
        Assert.NotNull(PersonOf(db, personId).RoomId);

        await provider.GetRequiredService<IIntakeService>().CloseAsync(
            new IntakeCloseRequest(intake.Id, ReleaseRooms: true, MovePersonnel: true, TargetNodeId: -1));

        var person = PersonOf(db, personId);
        Assert.Null(person.RoomId);
        Assert.Null(person.IntakeId);
    }

    [Fact]
    public async Task ClosingWithoutMovingKeepsPeopleInTheIntake()
    {
        using var db = new TestDb();
        var (intake, personId, provider) = await ArrangeAsync(db);

        await provider.GetRequiredService<IIntakeService>().CloseAsync(
            new IntakeCloseRequest(intake.Id, ReleaseRooms: false, MovePersonnel: false, TargetNodeId: null));

        Assert.Equal(intake.Id, PersonOf(db, personId).IntakeId);
    }
}
