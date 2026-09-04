using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Services.Intakes;
using GenDoc.Services.OrgTree;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Personnel;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.Tests.Personnel;

public class OrgTreeDeleteIntakeRootTests
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
        return services.BuildServiceProvider();
    }

    private static int SeedRoot(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
        var root = new OrgNode { Name = "Курс", Depth = 0, SortOrder = 0, Path = "/" };
        ctx.OrgNodes.Add(root);
        ctx.SaveChanges();
        root.Path = $"/{root.Id}/";
        ctx.SaveChanges();
        return root.Id;
    }

    [Fact]
    public async Task DeletingTheIntakeRoot_DropsTheActiveIntake_AndAnnouncesTheChange()
    {
        using var db = new TestDb();
        var rootId = SeedRoot(db);
        var provider = BuildProvider(db);
        var intake = await provider.GetRequiredService<IIntakeService>().CreateAsync(new IntakeCreateRequest(
            "Набір №1", rootId,
            DateOnly.FromDateTime(DateTime.Today),
            DateOnly.FromDateTime(DateTime.Today.AddMonths(3))));
        var state = provider.GetRequiredService<ActiveIntakeState>();
        await state.RefreshAsync();
        Assert.True(state.HasActive);

        var tree = provider.GetRequiredService<OrgTreeViewModel>();
        await tree.EnsureLoadedAsync();
        var intakeRoot = tree.FindById(intake.RootOrgNodeId)!;
        Assert.True(intakeRoot.IsIntakeRoot);

        var recipient = new object();
        var counts = 0;
        WeakReferenceMessenger.Default.Register<CountsChangedMessage>(recipient, (_, _) => counts++);
        try
        {
            await tree.DeleteConfirmedAsync(intakeRoot);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
        }

        Assert.False(state.HasActive);
        Assert.Equal("Активного набору немає", state.StatusText);
        Assert.True(counts >= 1);
        Assert.Null(tree.FindById(intake.RootOrgNodeId));
    }
}
