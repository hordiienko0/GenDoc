using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Services.Intakes;
using GenDoc.Services.OrgTree;
using GenDoc.Services.Personnel;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Archive;
using GenDoc.ViewModels.Intakes;
using GenDoc.ViewModels.Personnel;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.Tests.Intakes;

public class IntakeDisplayNumberTests
{
    private sealed class NoDialogs : IDialogService
    {
        public bool? ShowDialog<TViewModel>(TViewModel viewModel, Window? owner = null) where TViewModel : notnull => null;
    }

    private static IntakeOverview Overview(int number, string displayNumber) => new(
        1, number, displayNumber, IntakeStatus.Active,
        DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today.AddMonths(2)), null,
        10, 6, 50, 2, null);

    [Fact]
    public void Label_UsesTheOfficersNumber_AndFallsBackToTheInternalOne()
    {
        Assert.Equal("Набір №5", IntakeLabel.Of(1, "Набір №5"));
        Assert.Equal("2026-А", IntakeLabel.Of(1, "  2026-А "));
        Assert.Equal("Набір №1", IntakeLabel.Of(1, ""));
        Assert.Equal("Набір №1", IntakeLabel.Of(1, null));
    }

    [Fact]
    public void IntakeCard_ShowsTheOfficersNumberOnce()
    {
        Assert.Equal("Набір №5", new IntakeCardViewModel(Overview(1, "Набір №5")).TitleText);
        Assert.Equal("Набір №1", new IntakeCardViewModel(Overview(1, "")).TitleText);
    }

    private static (int IntakeId, int RootId, int PackageId) SeedIntake(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
        var intake = new Intake
        {
            Number = 1, DisplayNumber = "Набір №5", Status = IntakeStatus.Active, StatusIsPinned = true,
            DateStart = DateOnly.FromDateTime(DateTime.Today), DateEnd = DateOnly.FromDateTime(DateTime.Today.AddDays(9))
        };
        ctx.Intakes.Add(intake);
        var package = new GenerationPackage { Name = "Пакет" };
        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();

        var root = new OrgNode { Name = "Набір №5", Depth = 0, SortOrder = 0, Path = "/", IntakeId = intake.Id };
        ctx.OrgNodes.Add(root);
        ctx.SaveChanges();
        root.Path = $"/{root.Id}/";
        intake.RootOrgNodeId = root.Id;

        var person = TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас");
        person.OrgNodeId = root.Id;
        person.IntakeId = intake.Id;
        ctx.Recipients.Add(person);

        ctx.GenerationPackageRuns.Add(new GenerationPackageRun
        {
            GenerationPackageId = package.Id, RunAt = DateTime.Now, RunByUserId = 1, IntakeId = intake.Id, GeneratedCount = 1
        });
        ctx.SaveChanges();
        return (intake.Id, root.Id, package.Id);
    }

    [Fact]
    public async Task ArchiveFilterLabels_RunChips_AndStatusBar_UseTheOfficersNumber()
    {
        using var db = new TestDb();
        SeedIntake(db);
        var archive = TestServices.Archive(db);

        var options = await archive.GetFilterOptionsAsync();
        Assert.Equal("Набір №5 · активний", Assert.Single(options.Intakes).Label);

        var run = Assert.Single(await archive.GetRunsAsync(null, null));
        Assert.Equal("Набір №5", new RunGroupViewModel(run).IntakeChip);

        var services = new ServiceCollection();
        services.AddSingleton(db.Factory);
        services.AddSingleton<IAuditLogService>(new FakeAuditLog());
        services.AddSingleton<ICurrentUserContext>(new FakeCurrentUser());
        services.AddSingleton<GenDoc.Services.Completeness.ICompletenessService>(_ => TestServices.Completeness(db, 1));
        services.AddSingleton<IIntakeService, IntakeService>();
        services.AddSingleton<ActiveIntakeState>();
        var state = services.BuildServiceProvider().GetRequiredService<ActiveIntakeState>();
        await state.RefreshAsync();

        Assert.StartsWith("Активний набір: Набір №5 · день 1 з 10", state.StatusText);
    }

    [Fact]
    public async Task PersonnelFooter_NamesTheIntakeByTheOfficersNumber()
    {
        using var db = new TestDb();
        var (_, rootId, _) = SeedIntake(db);

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

        Assert.Equal("1 записів у гілці «Набір №5» · 1 у наборі «Набір №5»", vm.FooterText);
    }
}
