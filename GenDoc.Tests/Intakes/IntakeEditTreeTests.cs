using System.Windows;
using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Services.Intakes;
using GenDoc.Services.OrgTree;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Personnel;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.Tests.Intakes;

public class IntakeEditTreeTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private sealed class NoCounts : ICountService
    {
        public Task<Dictionary<int, int>> GetTreeCountsAsync() => Task.FromResult(new Dictionary<int, int>());
    }

    /// <summary>Імітує користувача: бачить заповнене вікно, міняє поля й тисне «Зберегти».</summary>
    private sealed class EditingDialog : IDialogService
    {
        public Action<IntakeWizardViewModel> Edit { get; set; } = _ => { };
        public IntakeWizardViewModel? Shown { get; private set; }

        public bool? ShowDialog<TViewModel>(TViewModel viewModel, Window? owner = null) where TViewModel : notnull
        {
            if (viewModel is not IntakeWizardViewModel wizard) return null;
            Shown = wizard;
            Edit(wizard);
            Task.Run(() => wizard.CreateCommand.ExecuteAsync(null)).GetAwaiter().GetResult();
            return wizard.UpdatedIntake is not null;
        }
    }

    private static (ServiceProvider Provider, EditingDialog Dialog) BuildProvider(TestDb db)
    {
        var dialog = new EditingDialog();
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
        services.AddSingleton<IDialogService>(dialog);
        services.AddSingleton<ActiveIntakeState>();
        services.AddSingleton<OrgTreeViewModel>();
        return (services.BuildServiceProvider(), dialog);
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

    private static IEnumerable<OrgNodeViewModel> Flatten(IEnumerable<OrgNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }

    [Fact]
    public async Task EditOpensPrefilledAndRenamesTheIntakeFolderInTheTree()
    {
        using var db = new TestDb();
        var baseId = AddRootFolder(db, "Курс");
        var (provider, dialog) = BuildProvider(db);
        var intake = await provider.GetRequiredService<IIntakeService>().CreateAsync(new IntakeCreateRequest(
            "Набір №1", baseId, Today, Today.AddMonths(3)));
        var tree = provider.GetRequiredService<OrgTreeViewModel>();
        await tree.ReloadAsync();

        dialog.Edit = wizard =>
        {
            Assert.True(wizard.IsEditMode);
            Assert.Equal("Зберегти", wizard.SubmitText);
            Assert.Equal("Набір №1", wizard.DisplayNumber);
            Assert.Equal(baseId, wizard.SelectedBaseNode?.Id);
            Assert.Equal(Today.ToDateTime(TimeOnly.MinValue), wizard.DateStart);
            Assert.Equal(Today.AddMonths(3).ToDateTime(TimeOnly.MinValue), wizard.DateEnd);
            Assert.DoesNotContain(wizard.BaseNodes, b => b.Id == intake.RootOrgNodeId);

            wizard.DisplayNumber = "Набір №1-А";
        };

        var root = Flatten(tree.RootNodes).Single(n => n.Id == intake.RootOrgNodeId);
        await tree.EditIntakeCommand.ExecuteAsync(root);

        Assert.NotNull(dialog.Shown);
        var reloadedRoot = Flatten(tree.RootNodes).Single(n => n.Id == intake.RootOrgNodeId);
        Assert.Equal("Набір №1-А", reloadedRoot.Name);
        Assert.Equal("Набір №1-А", tree.GetIntake(intake.Id)?.DisplayNumber);
    }

    [Fact]
    public async Task IntakeThatEndsUpFinishedStaysVisibleInTheTree()
    {
        using var db = new TestDb();
        var baseId = AddRootFolder(db, "Курс");
        var (provider, dialog) = BuildProvider(db);
        var intake = await provider.GetRequiredService<IIntakeService>().CreateAsync(new IntakeCreateRequest(
            "Набір №1", baseId, Today, Today.AddMonths(3)));
        var tree = provider.GetRequiredService<OrgTreeViewModel>();
        await tree.ReloadAsync();
        Assert.True(tree.IsFilterActive);

        dialog.Edit = wizard =>
        {
            wizard.DateStart = Today.AddMonths(-4).ToDateTime(TimeOnly.MinValue);
            wizard.DateEnd = Today.AddDays(-1).ToDateTime(TimeOnly.MinValue);
        };

        Assert.True(await tree.EditIntakeByIdAsync(intake.Id));

        Assert.True(tree.IsFilterAll);
        Assert.True(Flatten(tree.RootNodes).Single(n => n.Id == intake.RootOrgNodeId).IsVisibleInFilter);
    }

    [Fact]
    public async Task EditCommandIgnoresFoldersThatAreNotAnIntakeRoot()
    {
        using var db = new TestDb();
        var baseId = AddRootFolder(db, "Курс");
        var (provider, dialog) = BuildProvider(db);
        var tree = provider.GetRequiredService<OrgTreeViewModel>();
        await tree.ReloadAsync();

        await tree.EditIntakeCommand.ExecuteAsync(Flatten(tree.RootNodes).Single(n => n.Id == baseId));

        Assert.Null(dialog.Shown);
    }
}
