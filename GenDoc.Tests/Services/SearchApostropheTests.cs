using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Services.Intakes;
using GenDoc.Services.OrgTree;
using GenDoc.Services.Personnel;
using GenDoc.Services.Recipients;
using GenDoc.Services.Rooms;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Personnel;
using GenDoc.ViewModels.Rooms;
using GenDoc.ViewModels.Staff;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.Tests.Services;

public class SearchApostropheTests
{
    private sealed class NoDialogs : IDialogService
    {
        public bool? ShowDialog<TViewModel>(TViewModel viewModel, Window? owner = null) where TViewModel : notnull => null;
    }

    [Theory]
    [InlineData("О’Коннор")]
    [InlineData("Оʼконнор")]
    [InlineData("О‘Кон")]
    [InlineData("О`Кон")]
    [InlineData("о'кон")]
    public void Contains_TreatsEveryApostropheAsPlain(string query)
        => Assert.True(SearchNormalization.Contains("О'КОННОР Шон", query));

    [Fact]
    public void Contains_MatchesTypographicApostropheInTheStoredValue()
        => Assert.True(SearchNormalization.Contains("О’КОННОР", "о'кон"));

    [Fact]
    public void PrepareQuery_TrimsAndNormalizes()
    {
        Assert.Equal("О'Кон", SearchNormalization.PrepareQuery("  О’Кон "));
        Assert.Null(SearchNormalization.PrepareQuery("   "));
        Assert.Null(SearchNormalization.PrepareQuery(null));
    }

    [Fact]
    public void RankOrder_AndHeaderNormalization_ShareTheApostropheRule()
    {
        Assert.Equal(RankOrder.Normalize("майор мед. служби"), RankOrder.Normalize("майор мед. служби"));
        Assert.Equal("імя", HeaderNormalization.Normalize("Імʼя"));
        Assert.Equal("імя", HeaderNormalization.Normalize("Ім`я"));
    }

    [Fact]
    public void RecipientService_Search_FindsOConnorWithTypographicApostrophe()
    {
        using var db = new TestDb();
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Recipients.Add(TemplateFixtures.Person(1, "О'КОННОР", "Шон"));
            ctx.Recipients.Add(TemplateFixtures.Person(2, "ШЕВЧЕНКО", "Тарас"));
            ctx.SaveChanges();
        }

        var service = new RecipientService(db.Factory, new FakeAuditLog(), new FakeCurrentUser());

        Assert.Single(service.Search("О’Кон"));
        Assert.Single(service.Search("о'кон шон"));
        Assert.Empty(service.Search("О’Кон Тарас"));
    }

    [Fact]
    public async Task PersonnelViewModel_Search_FindsOConnorWithTypographicApostrophe()
    {
        using var db = new TestDb();
        int rootId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
            var root = new OrgNode { Name = "Курс", Depth = 0, SortOrder = 0, Path = "/" };
            ctx.OrgNodes.Add(root);
            ctx.SaveChanges();
            root.Path = $"/{root.Id}/";
            var person = TemplateFixtures.Person(1, "О'КОННОР", "Шон");
            person.OrgNodeId = root.Id;
            var other = TemplateFixtures.Person(2, "ШЕВЧЕНКО", "Тарас");
            other.OrgNodeId = root.Id;
            ctx.Recipients.AddRange(person, other);
            ctx.SaveChanges();
            rootId = root.Id;
        }

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
        var provider = services.BuildServiceProvider();

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

        vm.SearchText = "О’Кон";
        vm.ApplySearch();

        Assert.Equal("О'КОННОР", Assert.Single(vm.Rows).LastName);
    }

    [Fact]
    public async Task StaffViewModel_Search_FindsOConnorWithTypographicApostrophe()
    {
        using var db = new TestDb();
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
            ctx.Recipients.Add(TemplateFixtures.Person(1, "О'КОННОР", "Шон"));
            ctx.Recipients.Add(TemplateFixtures.Person(2, "ШЕВЧЕНКО", "Тарас"));
            ctx.SaveChanges();
        }

        var vm = new StaffViewModel(TestServices.Staff(db, 1), new NoDialogs(), new ServiceCollection().BuildServiceProvider());
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.RowCount);

        vm.SearchText = "О’Кон";

        Assert.Equal(1, vm.RowCount);
        Assert.StartsWith("О'КОННОР", vm.Rows[0].FullName);
    }

    [Fact]
    public void RoomsViewModel_Search_FindsOccupantWithTypographicApostrophe()
    {
        using var db = new TestDb();
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
            var room = new Room { Building = "Корпус А", Number = "101", Capacity = 2 };
            var other = new Room { Building = "Корпус Б", Number = "202", Capacity = 2 };
            ctx.Rooms.AddRange(room, other);
            ctx.SaveChanges();
            var person = TemplateFixtures.Person(1, "О'КОННОР", "Шон");
            person.RoomId = room.Id;
            ctx.Recipients.Add(person);
            ctx.SaveChanges();
        }

        var vm = new RoomsViewModel(new RoomService(db.Factory, new FakeAuditLog(), new FakeCurrentUser()));
        Assert.Equal(2, vm.Rooms.Count);

        vm.SearchText = "О’Кон";

        Assert.Equal("101", Assert.Single(vm.Rooms).Number);
    }
}
