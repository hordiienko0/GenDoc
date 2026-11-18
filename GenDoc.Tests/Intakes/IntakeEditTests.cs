using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Intakes;
using GenDoc.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.Tests.Intakes;

public class IntakeEditTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

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

    private static int AddRootFolder(TestDb db, string name)
    {
        using var ctx = db.Factory.CreateDbContext();
        var root = new OrgNode { Name = name, Depth = 0, SortOrder = 0, Path = "/" };
        ctx.OrgNodes.Add(root);
        ctx.SaveChanges();
        root.Path = $"/{root.Id}/";
        ctx.SaveChanges();
        return root.Id;
    }

    private static async Task<(IIntakeService Service, Intake Intake, int BaseId, int PersonId, int AllId)> ArrangeAsync(TestDb db)
    {
        var baseId = AddRootFolder(db, "Курс");
        var service = BuildProvider(db).GetRequiredService<IIntakeService>();
        var intake = await service.CreateAsync(new IntakeCreateRequest(
            "Набір №1", baseId, Today, Today.AddMonths(3)));

        using var ctx = db.Factory.CreateDbContext();
        var all = ctx.OrgNodes.Single(n => n.IntakeId == intake.Id && n.Name == IntakeFolderNames.All);
        var person = new Recipient
        {
            LastName = "КОВАЛЕНКО", FirstName = "Іван", Rank = "солдат", Position = "курсант",
            ServiceNumber = "РЕД-001", OrgNodeId = all.Id, IntakeId = intake.Id
        };
        ctx.Recipients.Add(person);
        ctx.SaveChanges();

        return (service, intake, baseId, person.Id, all.Id);
    }

    [Fact]
    public async Task RenamingTheIntakeRenamesItsRootFolderToo()
    {
        using var db = new TestDb();
        var (service, intake, baseId, _, _) = await ArrangeAsync(db);

        await service.UpdateAsync(new IntakeUpdateRequest(
            intake.Id, "  Набір №7 (весна)  ", baseId, intake.DateStart, intake.DateEnd));

        using var ctx = db.Factory.CreateDbContext();
        Assert.Equal("Набір №7 (весна)", ctx.Intakes.Single(i => i.Id == intake.Id).DisplayNumber);
        Assert.Equal("Набір №7 (весна)", ctx.OrgNodes.Single(n => n.Id == intake.RootOrgNodeId).Name);
    }

    [Fact]
    public async Task MovingTheStartIntoTheFutureMakesTheIntakePlanned()
    {
        using var db = new TestDb();
        var (service, intake, baseId, _, _) = await ArrangeAsync(db);
        Assert.Equal(IntakeStatus.Active, intake.Status);

        await service.UpdateAsync(new IntakeUpdateRequest(
            intake.Id, intake.DisplayNumber, baseId, Today.AddDays(10), Today.AddMonths(4)));

        using var ctx = db.Factory.CreateDbContext();
        var saved = ctx.Intakes.Single(i => i.Id == intake.Id);
        Assert.Equal(Today.AddDays(10), saved.DateStart);
        Assert.Equal(Today.AddMonths(4), saved.DateEnd);
        Assert.Equal(IntakeStatus.Planned, saved.Status);
        Assert.False(saved.StatusIsPinned);
    }

    [Fact]
    public async Task PeriodCoveringTodayMakesAPlannedIntakeActive()
    {
        using var db = new TestDb();
        var baseId = AddRootFolder(db, "Курс");
        var service = BuildProvider(db).GetRequiredService<IIntakeService>();
        var intake = await service.CreateAsync(new IntakeCreateRequest(
            "Набір №2", baseId, Today.AddDays(5), Today.AddMonths(3)));
        Assert.Equal(IntakeStatus.Planned, intake.Status);

        await service.UpdateAsync(new IntakeUpdateRequest(
            intake.Id, intake.DisplayNumber, baseId, Today.AddDays(-1), Today.AddMonths(3)));

        using var ctx = db.Factory.CreateDbContext();
        Assert.Equal(IntakeStatus.Active, ctx.Intakes.Single(i => i.Id == intake.Id).Status);
    }

    [Fact]
    public async Task PeriodAlreadyOverMakesTheIntakeCompleted()
    {
        using var db = new TestDb();
        var (service, intake, baseId, _, _) = await ArrangeAsync(db);

        await service.UpdateAsync(new IntakeUpdateRequest(
            intake.Id, intake.DisplayNumber, baseId, Today.AddMonths(-4), Today.AddDays(-1)));

        using var ctx = db.Factory.CreateDbContext();
        var saved = ctx.Intakes.Single(i => i.Id == intake.Id);
        Assert.Equal(IntakeStatus.Completed, saved.Status);
        Assert.Null(saved.DateClosed);
    }

    [Fact]
    public async Task ClosedIntakeStaysClosedWhenItsDatesChange()
    {
        using var db = new TestDb();
        var (service, intake, baseId, _, _) = await ArrangeAsync(db);
        await service.CloseAsync(new IntakeCloseRequest(intake.Id, ReleaseRooms: false, MovePersonnel: false, TargetNodeId: null));

        await service.UpdateAsync(new IntakeUpdateRequest(
            intake.Id, intake.DisplayNumber, baseId, Today.AddDays(-30), Today.AddMonths(2)));

        using var ctx = db.Factory.CreateDbContext();
        var saved = ctx.Intakes.Single(i => i.Id == intake.Id);
        Assert.Equal(IntakeStatus.Completed, saved.Status);
        Assert.NotNull(saved.DateClosed);
        Assert.Equal(Today.AddMonths(2), saved.DateEnd);
    }

    [Fact]
    public async Task UnchangedDatesKeepAReopenedIntakeAsItIs()
    {
        using var db = new TestDb();
        var (service, intake, baseId, _, _) = await ArrangeAsync(db);
        await service.CloseAsync(new IntakeCloseRequest(intake.Id, false, false, null));
        await service.ReopenAsync(intake.Id);

        await service.UpdateAsync(new IntakeUpdateRequest(
            intake.Id, "Інша назва", baseId, intake.DateStart, intake.DateEnd));

        using var ctx = db.Factory.CreateDbContext();
        var saved = ctx.Intakes.Single(i => i.Id == intake.Id);
        Assert.Equal(IntakeStatus.Active, saved.Status);
        Assert.True(saved.StatusIsPinned);
    }

    [Fact]
    public async Task NewBaseFolderMovesTheWholeIntakeBranchAndKeepsItsPeople()
    {
        using var db = new TestDb();
        var (service, intake, _, personId, allId) = await ArrangeAsync(db);
        var otherBaseId = AddRootFolder(db, "Інший курс");

        await service.UpdateAsync(new IntakeUpdateRequest(
            intake.Id, intake.DisplayNumber, otherBaseId, intake.DateStart, intake.DateEnd));

        using var ctx = db.Factory.CreateDbContext();
        var otherBase = ctx.OrgNodes.Single(n => n.Id == otherBaseId);
        var root = ctx.OrgNodes.Single(n => n.Id == intake.RootOrgNodeId);
        Assert.Equal(otherBaseId, root.ParentId);
        Assert.Equal($"{otherBase.Path}{root.Id}/", root.Path);
        Assert.Equal(otherBase.Depth + 1, root.Depth);
        Assert.Equal(intake.Id, root.IntakeId);

        var branch = ctx.OrgNodes.Where(n => n.IntakeId == intake.Id).ToList();
        Assert.Equal(5, branch.Count);
        foreach (var node in branch)
        {
            Assert.StartsWith(root.Path, node.Path);
            Assert.Equal(otherBase.Depth + 1 + (node.Path.Count(c => c == '/') - root.Path.Count(c => c == '/')), node.Depth);
        }

        var all = branch.Single(n => n.Id == allId);
        Assert.Equal($"{root.Path}{all.Id}/", all.Path);

        var person = ctx.Recipients.Single(r => r.Id == personId);
        Assert.Equal(allId, person.OrgNodeId);
        Assert.Equal(intake.Id, person.IntakeId);
    }

    [Fact]
    public async Task EndBeforeStartIsRejected()
    {
        using var db = new TestDb();
        var (service, intake, baseId, _, _) = await ArrangeAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(new IntakeUpdateRequest(
            intake.Id, intake.DisplayNumber, baseId, Today, Today.AddDays(-1))));
    }

    [Fact]
    public async Task EmptyNameIsRejected()
    {
        using var db = new TestDb();
        var (service, intake, baseId, _, _) = await ArrangeAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(new IntakeUpdateRequest(
            intake.Id, "   ", baseId, intake.DateStart, intake.DateEnd)));
    }

    [Fact]
    public async Task BaseFolderInsideAnIntakeIsRejected()
    {
        using var db = new TestDb();
        var (service, intake, _, _, allId) = await ArrangeAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(new IntakeUpdateRequest(
            intake.Id, intake.DisplayNumber, allId, intake.DateStart, intake.DateEnd)));
    }

    [Fact]
    public async Task GetBaseNodeIdReturnsTheParentOfTheIntakeRoot()
    {
        using var db = new TestDb();
        var (service, intake, baseId, _, _) = await ArrangeAsync(db);

        Assert.Equal(baseId, await service.GetBaseNodeIdAsync(intake.Id));
    }
}
