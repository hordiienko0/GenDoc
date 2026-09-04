using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Services.Intakes;
using GenDoc.Services.OrgTree;
using GenDoc.Services.Personnel;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Personnel;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.Tests.Personnel;

public class PersonnelCheckedSurvivesSearchTests
{
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
        services.AddSingleton<GenDoc.Services.Completeness.ICompletenessService>(_ => TestServices.Completeness(db));
        services.AddSingleton<IIntakeService, IntakeService>();
        services.AddSingleton<IOrgTreeService, OrgTreeService>();
        services.AddSingleton<ICountService, CountService>();
        services.AddSingleton<IDialogService, NoDialogs>();
        services.AddSingleton<ActiveIntakeState>();
        services.AddSingleton<OrgTreeViewModel>();
        services.AddSingleton<IPersonnelService, PersonnelService>();
        return services.BuildServiceProvider();
    }

    private static int SeedRootWithPeople(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
        var root = new OrgNode { Name = "Курс", Depth = 0, SortOrder = 0, Path = "/" };
        ctx.OrgNodes.Add(root);
        ctx.SaveChanges();
        root.Path = $"/{root.Id}/";

        foreach (var (id, last, first) in new[] { (1, "ШЕВЧЕНКО", "Тарас"), (2, "ФРАНКО", "Іван"), (3, "КОВАЛЬ", "Петро") })
        {
            var person = TemplateFixtures.Person(id, last, first);
            person.OrgNodeId = root.Id;
            ctx.Recipients.Add(person);
        }
        ctx.SaveChanges();
        return root.Id;
    }

    private static async Task<PersonnelViewModel> CreateViewModelAsync(TestDb db, ServiceProvider provider, int rootId)
    {
        var tree = provider.GetRequiredService<OrgTreeViewModel>();
        await tree.EnsureLoadedAsync();
        tree.SelectNode(tree.FindById(rootId)!);

        var vm = new PersonnelViewModel(
            tree,
            provider.GetRequiredService<IPersonnelService>(),
            TestServices.Completeness(db),
            TestServices.Archive(db),
            TestServices.Generation(db),
            provider.GetRequiredService<IIntakeService>(),
            new NoDialogs(),
            TestServices.ManualTagForm(db, TestServices.Staff(db, 1), new FakeCurrentUser(), new FakeIntakeAccessor(), 1),
            new OutputFolderService(db.Factory));
        WeakReferenceMessenger.Default.Unregister<CountsChangedMessage>(vm);
        await vm.InitializeAsync();
        return vm;
    }

    [Fact]
    public async Task CheckedRows_StayChecked_WhileSearchHidesThem()
    {
        using var db = new TestDb();
        var rootId = SeedRootWithPeople(db);
        var provider = BuildProvider(db);
        var vm = await CreateViewModelAsync(db, provider, rootId);

        Assert.Equal(3, vm.RowCount);
        foreach (var row in vm.Rows) row.IsChecked = true;
        Assert.Equal("Обрано 3", vm.SelectionInfoText);

        vm.SearchText = "Шевч";
        vm.ApplySearch();

        Assert.Equal(1, vm.RowCount);
        Assert.Equal(3, vm.CheckedCount);
        Assert.Equal("Обрано 3 (2 приховано пошуком)", vm.SelectionInfoText);
        Assert.Equal(3, vm.CheckedRows.Count);
        Assert.True(vm.HeaderChecked);

        vm.SearchText = null;
        vm.ApplySearch();

        Assert.Equal(3, vm.RowCount);
        Assert.True(vm.Rows.All(r => r.IsChecked));
        Assert.Equal("Обрано 3", vm.SelectionInfoText);

        vm.ClearCheckedCommand.Execute(null);

        Assert.Equal(0, vm.CheckedCount);
        Assert.DoesNotContain(vm.Rows, r => r.IsChecked);
    }

    [Fact]
    public async Task HeaderCheckbox_FromPartialSelection_SelectsEveryVisibleRow()
    {
        using var db = new TestDb();
        var rootId = SeedRootWithPeople(db);
        var provider = BuildProvider(db);
        var vm = await CreateViewModelAsync(db, provider, rootId);

        vm.Rows[0].IsChecked = true;
        Assert.Null(vm.HeaderChecked);

        vm.HeaderChecked = false;

        Assert.True(vm.Rows.All(r => r.IsChecked));
        Assert.True(vm.HeaderChecked);

        vm.HeaderChecked = false;

        Assert.DoesNotContain(vm.Rows, r => r.IsChecked);
        Assert.False(vm.HeaderChecked);
    }

    [Fact]
    public async Task HeaderCheckbox_ActsOnVisibleRowsOnly_ButTheCountKeepsHiddenChecks()
    {
        using var db = new TestDb();
        var rootId = SeedRootWithPeople(db);
        var provider = BuildProvider(db);
        var vm = await CreateViewModelAsync(db, provider, rootId);

        vm.Rows.Single(r => r.LastName == "КОВАЛЬ").IsChecked = true;
        vm.SearchText = "Шевч";
        vm.ApplySearch();

        Assert.False(vm.HeaderChecked);
        Assert.Equal("Обрано 1 (1 приховано пошуком)", vm.SelectionInfoText);

        vm.HeaderChecked = true;

        Assert.Equal(2, vm.CheckedCount);
        Assert.True(vm.HeaderChecked);
        Assert.Equal("Обрано 2 (1 приховано пошуком)", vm.SelectionInfoText);
    }
}
