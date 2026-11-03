using System.IO;
using System.Windows;
using ClosedXML.Excel;
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

// «Список ПІБ»: набір і прикомандировані в одній теці - повні ПІБ усіх однією колонкою, за абеткою.
public class PersonnelListenerNamesTests
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

    // Тека «31 набір» з людьми набору й прикомандированими впереміш і вкладена тека з ще однією особою.
    private static int SeedFolder(TestDb db)
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
                     (3, "ЄРМАК", "Андрій", root.Id),
                     (4, "КОВАЛЬ", "Петро", child.Id),
                 })
        {
            var person = TemplateFixtures.Person(id, last, first);
            person.OrgNodeId = nodeId;
            ctx.Recipients.Add(person);
        }
        ctx.Recipients.Add(new Recipient
        {
            Id = 5, LastName = "ГНАТЮК", FirstName = "Олег", MiddleName = null,
            Rank = "солдат", Position = "слухач", ServiceNumber = "СН0005", OrgNodeId = root.Id
        });
        ctx.SaveChanges();
        return root.Id;
    }

    private static async Task<PersonnelViewModel> CreateViewModelAsync(TestDb db, ServiceProvider provider, int nodeId)
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
            new NoDialogs(),
            TestServices.ManualTagForm(db, TestServices.Staff(db, 1), new FakeCurrentUser(), new FakeIntakeAccessor(), 1),
            new OutputFolderService(db.Factory),
            new ExportService(db.Factory, new FakeAuditLog(), new XlsxGenerationService()));
        WeakReferenceMessenger.Default.Unregister<CountsChangedMessage>(vm);
        await vm.InitializeAsync();
        return vm;
    }

    [Fact]
    public async Task NothingChecked_TakesEveryoneInTheFolder_FullNamesAlphabetically()
    {
        using var db = new TestDb();
        var rootId = SeedFolder(db);
        var vm = await CreateViewModelAsync(db, BuildProvider(db), rootId);

        Assert.Equal(new[]
        {
            "ГНАТЮК Олег",
            "ЄРМАК Андрій Петрович",
            "КОВАЛЬ Петро Петрович",
            "ФРАНКО Іван Петрович",
            "ШЕВЧЕНКО Тарас Петрович",
        }, vm.ListenerNames());
    }

    [Fact]
    public async Task CheckedRows_TakeOnlyThem_EvenWhenSearchHidesSome()
    {
        using var db = new TestDb();
        var rootId = SeedFolder(db);
        var vm = await CreateViewModelAsync(db, BuildProvider(db), rootId);

        vm.Rows.Single(r => r.LastName == "ФРАНКО").IsChecked = true;
        vm.Rows.Single(r => r.LastName == "ЄРМАК").IsChecked = true;
        vm.SearchText = "Франк";
        vm.ApplySearch();

        Assert.Equal(new[] { "ЄРМАК Андрій Петрович", "ФРАНКО Іван Петрович" }, vm.ListenerNames());
    }

    [Fact]
    public async Task NothingChecked_WithSearch_TakesOnlyVisibleRows()
    {
        using var db = new TestDb();
        var rootId = SeedFolder(db);
        var vm = await CreateViewModelAsync(db, BuildProvider(db), rootId);

        vm.SearchText = "Шевч";
        vm.ApplySearch();

        Assert.Equal(new[] { "ШЕВЧЕНКО Тарас Петрович" }, vm.ListenerNames());
    }

    [Fact]
    public async Task Excel_HasNumberedColumnOfFullNames()
    {
        using var db = new TestDb();
        var rootId = SeedFolder(db);
        var vm = await CreateViewModelAsync(db, BuildProvider(db), rootId);
        var path = Path.Combine(Path.GetTempPath(), $"listeners_{Guid.NewGuid():N}.xlsx");

        try
        {
            var result = await vm.WriteListenerNamesAsync(vm.ListenerNames(), path);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(5, result.RowCount);

            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheet("Список слухачів");
            Assert.Equal("№ з/п", sheet.Cell(1, 1).GetString());
            Assert.Equal("Прізвище, ім'я та по батькові", sheet.Cell(1, 2).GetString());
            Assert.Equal(1, sheet.Cell(2, 1).GetValue<int>());
            Assert.Equal("ГНАТЮК Олег", sheet.Cell(2, 2).GetString());
            Assert.Equal(5, sheet.Cell(6, 1).GetValue<int>());
            Assert.Equal("ШЕВЧЕНКО Тарас Петрович", sheet.Cell(6, 2).GetString());
            Assert.True(sheet.Cell(7, 2).IsEmpty());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
