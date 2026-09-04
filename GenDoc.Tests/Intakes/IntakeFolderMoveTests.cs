using System.Windows;
using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Services.Intakes;
using GenDoc.Services.OrgTree;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Personnel;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.Tests.Intakes;

public class IntakeFolderMoveTests
{
    private sealed class NoCounts : ICountService
    {
        public Task<Dictionary<int, int>> GetTreeCountsAsync() => Task.FromResult(new Dictionary<int, int>());
    }

    private sealed class NoDialogs : IDialogService
    {
        public bool? ShowDialog<TViewModel>(TViewModel viewModel, Window? owner = null) where TViewModel : notnull => null;
    }

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
        services.AddSingleton<IOrgTreeService, OrgTreeService>();
        services.AddSingleton<ICountService, NoCounts>();
        services.AddSingleton<IDialogService, NoDialogs>();
        services.AddSingleton<ActiveIntakeState>();
        services.AddSingleton<OrgTreeViewModel>();
        return services.BuildServiceProvider();
    }

    private static async Task<(ServiceProvider Provider, int RootId, Intake Intake, int SubfolderId, int PersonId)> ArrangeAsync(TestDb db)
    {
        int rootId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var root = new OrgNode { Name = "Курс", Depth = 0, SortOrder = 0, Path = "/" };
            ctx.OrgNodes.Add(root);
            ctx.SaveChanges();
            root.Path = $"/{root.Id}/";
            ctx.SaveChanges();
            rootId = root.Id;
        }

        var provider = BuildProvider(db);
        var intake = await provider.GetRequiredService<IIntakeService>().CreateAsync(new IntakeCreateRequest(
            "Набір №1", rootId,
            DateOnly.FromDateTime(DateTime.Today),
            DateOnly.FromDateTime(DateTime.Today.AddMonths(3))));

        int subfolderId, personId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var all = ctx.OrgNodes.Single(n => n.IntakeId == intake.Id && n.Name == IntakeFolderNames.All);
            var group = new OrgNode
            {
                Name = "Перша група", ParentId = all.Id, Depth = all.Depth + 1, SortOrder = 9, IntakeId = intake.Id
            };
            ctx.OrgNodes.Add(group);
            ctx.SaveChanges();
            group.Path = $"{all.Path}{group.Id}/";

            var person = new Recipient
            {
                LastName = "ГРУПЕНКО", FirstName = "Петро", Rank = "солдат", Position = "курсант",
                ServiceNumber = "ГР-001", OrgNodeId = group.Id, IntakeId = intake.Id
            };
            ctx.Recipients.Add(person);
            ctx.SaveChanges();
            subfolderId = group.Id;
            personId = person.Id;
        }

        return (provider, rootId, intake, subfolderId, personId);
    }

    [Fact]
    public async Task MovingAnIntakeSubfolderOutOfTheIntakeClearsIntakeIdOnItAndItsPeople()
    {
        using var db = new TestDb();
        var (provider, rootId, _, subfolderId, personId) = await ArrangeAsync(db);

        await provider.GetRequiredService<IOrgTreeService>().MoveAsync(subfolderId, rootId);

        using var ctx = db.Factory.CreateDbContext();
        Assert.Null(ctx.OrgNodes.Single(n => n.Id == subfolderId).IntakeId);
        Assert.Null(ctx.Recipients.Single(r => r.Id == personId).IntakeId);
    }

    [Fact]
    public async Task MovingAPlainFolderIntoAnIntakeStillAdoptsIt()
    {
        using var db = new TestDb();
        var (provider, rootId, intake, _, _) = await ArrangeAsync(db);

        int plainId, personId, allId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var root = ctx.OrgNodes.Single(n => n.Id == rootId);
            var plain = new OrgNode { Name = "Окремо", ParentId = root.Id, Depth = 1, SortOrder = 7 };
            ctx.OrgNodes.Add(plain);
            ctx.SaveChanges();
            plain.Path = $"{root.Path}{plain.Id}/";
            var person = new Recipient
            {
                LastName = "ОКРЕМЕНКО", FirstName = "Ілля", Rank = "солдат", Position = "курсант",
                ServiceNumber = "ОК-001", OrgNodeId = plain.Id
            };
            ctx.Recipients.Add(person);
            ctx.SaveChanges();
            plainId = plain.Id;
            personId = person.Id;
            allId = ctx.OrgNodes.Single(n => n.IntakeId == intake.Id && n.Name == IntakeFolderNames.All).Id;
        }

        await provider.GetRequiredService<IOrgTreeService>().MoveAsync(plainId, allId);

        using var check = db.Factory.CreateDbContext();
        Assert.Equal(intake.Id, check.OrgNodes.Single(n => n.Id == plainId).IntakeId);
        Assert.Equal(intake.Id, check.Recipients.Single(r => r.Id == personId).IntakeId);
    }

    private static OrgNodeViewModel FindNode(OrgTreeViewModel tree, int id)
    {
        static IEnumerable<OrgNodeViewModel> Flatten(IEnumerable<OrgNodeViewModel> nodes)
        {
            foreach (var node in nodes)
            {
                yield return node;
                foreach (var child in Flatten(node.Children)) yield return child;
            }
        }

        return Flatten(tree.RootNodes).Single(n => n.Id == id);
    }

    private static IEnumerable<PickerNodeViewModel> FlattenPicker(IEnumerable<PickerNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in FlattenPicker(node.Children)) yield return child;
        }
    }

    [Fact]
    public async Task TheAllFolderAndTheCategoryFoldersCannotBeMoved()
    {
        using var db = new TestDb();
        var (provider, _, intake, subfolderId, _) = await ArrangeAsync(db);
        var tree = provider.GetRequiredService<OrgTreeViewModel>();
        await tree.ReloadAsync();

        int allId;
        List<int> categoryIds;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var nodes = ctx.OrgNodes.Where(n => n.IntakeId == intake.Id).ToList();
            allId = nodes.Single(n => n.Name == IntakeFolderNames.All).Id;
            categoryIds = nodes.Where(n => IntakeFitnessFolders.AllFolderNames.Contains(n.Name)).Select(n => n.Id).ToList();
        }

        Assert.False(FindNode(tree, allId).CanMove);
        foreach (var id in categoryIds)
            Assert.False(FindNode(tree, id).CanMove);
        Assert.True(FindNode(tree, subfolderId).CanMove);
    }

    [Fact]
    public async Task ThePickerForAnIntakeSubfolderDisablesTargetsOutsideTheIntake()
    {
        using var db = new TestDb();
        var (provider, rootId, intake, subfolderId, _) = await ArrangeAsync(db);
        var tree = provider.GetRequiredService<OrgTreeViewModel>();
        await tree.ReloadAsync();

        var picker = NodePickerDialogViewModel.ForNodeMove(tree, FindNode(tree, subfolderId));
        var byId = FlattenPicker(picker.RootNodes).ToDictionary(n => n.Id);

        Assert.False(byId[rootId].IsEnabled);
        Assert.True(byId[intake.RootOrgNodeId].IsEnabled);
    }
}
