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

// Що саме піде в діалог експорту: кнопка над списком бере те, що на екрані, права кнопка в дереві - цілу папку.
public class PersonnelExportRequestTests
{
    private sealed class RecordingDialogs : IDialogService
    {
        public List<object> Shown { get; } = new();

        public bool? ShowDialog<TViewModel>(TViewModel viewModel, Window? owner = null) where TViewModel : notnull
        {
            Shown.Add(viewModel);
            return null;
        }
    }

    private static ServiceProvider BuildProvider(TestDb db, IDialogService dialogs)
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
        services.AddSingleton(dialogs);
        services.AddSingleton<ActiveIntakeState>();
        services.AddSingleton<OrgTreeViewModel>();
        services.AddSingleton<IPersonnelService, PersonnelService>();
        return services.BuildServiceProvider();
    }

    private static (int RootId, int ChildId) SeedFolder(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
        var root = new OrgNode { Name = "31 набір", Depth = 0, SortOrder = 0, Path = "/" };
        ctx.OrgNodes.Add(root);
        ctx.SaveChanges();
        root.Path = $"/{root.Id}/";
        var child = new OrgNode { Name = "Прикомандировані", ParentId = root.Id, Depth = 1, SortOrder = 0, Path = "/" };
        ctx.OrgNodes.Add(child);
        ctx.SaveChanges();
        child.Path = $"{root.Path}{child.Id}/";

        foreach (var (id, last, first, nodeId) in new[]
                 {
                     (1, "ШЕВЧЕНКО", "Тарас", root.Id),
                     (2, "ФРАНКО", "Іван", root.Id),
                     (3, "КОВАЛЬ", "Петро", child.Id),
                 })
        {
            var person = TemplateFixtures.Person(id, last, first);
            person.OrgNodeId = nodeId;
            ctx.Recipients.Add(person);
        }
        ctx.SaveChanges();
        return (root.Id, child.Id);
    }

    private static async Task<PersonnelViewModel> CreateViewModelAsync(
        TestDb db, ServiceProvider provider, IDialogService dialogs, int nodeId)
    {
        var tree = provider.GetRequiredService<OrgTreeViewModel>();
        await tree.EnsureLoadedAsync();
        tree.SelectNode(tree.FindById(nodeId)!);

        var vm = new PersonnelViewModel(
            tree,
            provider.GetRequiredService<IPersonnelService>(),
            TestServices.Completeness(db),
            TestServices.Archive(db),
            TestServices.Generation(db),
            provider.GetRequiredService<IIntakeService>(),
            dialogs,
            TestServices.ManualTagForm(db, TestServices.Staff(db, 1), new FakeCurrentUser(), new FakeIntakeAccessor(), 1),
            new OutputFolderService(db.Factory),
            new ExportService(db.Factory, new FakeAuditLog(), new XlsxGenerationService()),
            TestServices.PersonExport(db, 1));
        WeakReferenceMessenger.Default.Unregister<CountsChangedMessage>(vm);
        await vm.InitializeAsync();
        return vm;
    }

    [Fact]
    public async Task ToolbarExport_NothingChecked_TakesTheFolderWithTheCurrentSearch()
    {
        using var db = new TestDb();
        var (rootId, _) = SeedFolder(db);
        var dialogs = new RecordingDialogs();
        var vm = await CreateViewModelAsync(db, BuildProvider(db, dialogs), dialogs, rootId);
        vm.SearchText = "  Франк ";

        var request = vm.BuildExportRequest()!;

        Assert.Equal(rootId, request.NodeId);
        Assert.Equal("31 набір", request.NodePath);
        Assert.True(request.IncludeDescendants);
        Assert.Null(request.OnlyIds);
        Assert.Equal("Франк", request.SearchText);
    }

    [Fact]
    public async Task ToolbarExport_WithCheckedRows_TakesOnlyThem_AndDropsTheSearch()
    {
        using var db = new TestDb();
        var (rootId, _) = SeedFolder(db);
        var dialogs = new RecordingDialogs();
        var vm = await CreateViewModelAsync(db, BuildProvider(db, dialogs), dialogs, rootId);
        vm.Rows.Single(r => r.LastName == "КОВАЛЬ").IsChecked = true;
        vm.Rows.Single(r => r.LastName == "ШЕВЧЕНКО").IsChecked = true;
        vm.SearchText = "Франк";
        vm.ApplySearch();

        var request = vm.BuildExportRequest()!;

        Assert.Equal(new[] { 1, 3 }, request.OnlyIds!.OrderBy(id => id));
        Assert.Null(request.SearchText);
    }

    [Fact]
    public async Task TreeContextMenu_OpensTheDialogForThatFolder_NotForTheCheckedRows()
    {
        using var db = new TestDb();
        var (rootId, childId) = SeedFolder(db);
        var dialogs = new RecordingDialogs();
        var provider = BuildProvider(db, dialogs);
        var vm = await CreateViewModelAsync(db, provider, dialogs, rootId);
        vm.Rows.Single(r => r.LastName == "ШЕВЧЕНКО").IsChecked = true;
        vm.SearchText = "Франк";
        var child = vm.Tree.FindById(childId)!;
        Assert.True(child.CanExport);

        vm.Tree.ExportCommand.Execute(child);
        for (var i = 0; i < 100 && dialogs.Shown.Count == 0; i++) await Task.Delay(20);

        var dialog = Assert.IsType<PersonnelExportDialogViewModel>(Assert.Single(dialogs.Shown));
        Assert.True(dialog.IsFolderScope);
        Assert.Equal("Папка: 31 набір › Прикомандировані", dialog.ScopeText);
        Assert.Null(dialog.SearchText);
        Assert.Equal("КОВАЛЬ", Assert.Single(dialog.MatchedRows).Person.LastName);
    }
}
