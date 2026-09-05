using System.Windows;
using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Services.Completeness;
using GenDoc.Services.Documents;
using GenDoc.Services.Generation;
using GenDoc.Services.Intakes;
using GenDoc.Services.OrgTree;
using GenDoc.Services.Personnel;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Generation;
using GenDoc.ViewModels.Personnel;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.Tests.Personnel;

public class PersonnelRenameTests
{
    private sealed class RenamingDialog : IDialogService
    {
        public bool? ShowDialog<TViewModel>(TViewModel viewModel, Window? owner = null) where TViewModel : notnull
        {
            if (viewModel is not NodeEditDialogViewModel edit) return null;
            edit.NodeName = "Перший курс";
            return true;
        }
    }

    private sealed class NoManualTags : IManualTagFormBuilder
    {
        public Task<ManualTagFormViewModel> BuildAsync(IReadOnlyList<string> tags, string contextKey, bool needsCourseOfficer = false, int? intakeId = null)
            => throw new NotSupportedException();

        public Task SaveAsync(string contextKey, ManualTagFormViewModel form) => Task.CompletedTask;
    }

    private static ServiceProvider BuildProvider(TestDb db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db.Factory);
        services.AddSingleton<IAuditLogService>(new FakeAuditLog());
        services.AddSingleton<ICurrentUserContext>(new FakeCurrentUser());
        services.AddSingleton<IUserSettingsService, UserSettingsService>();
        services.AddSingleton<ICompletenessService>(_ => TestServices.Completeness(db));
        services.AddSingleton<IDocumentArchiveService>(_ => TestServices.Archive(db));
        services.AddSingleton<IGenerationService>(_ => TestServices.Generation(db));
        services.AddSingleton<IIntakeService, IntakeService>();
        services.AddSingleton<IOrgTreeService, OrgTreeService>();
        services.AddSingleton<ICountService, CountService>();
        services.AddSingleton<IPersonnelService, PersonnelService>();
        services.AddSingleton<IDialogService, RenamingDialog>();
        services.AddSingleton<IManualTagFormBuilder, NoManualTags>();
        services.AddSingleton<IOutputFolderService>(new OutputFolderService(db.Factory));
        services.AddSingleton<ActiveIntakeState>();
        services.AddSingleton<OrgTreeViewModel>();
        services.AddSingleton<PersonnelViewModel>();
        return services.BuildServiceProvider();
    }

    private static void SeedRoot(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        var root = new OrgNode { Name = "Курс", Depth = 0, SortOrder = 0, Path = "/" };
        ctx.OrgNodes.Add(root);
        ctx.SaveChanges();
        root.Path = $"/{root.Id}/";
        ctx.SaveChanges();
    }

    [Fact]
    public async Task RenamingTheSelectedFolder_UpdatesBreadcrumbFooterAndTooltip()
    {
        using var db = new TestDb();
        SeedRoot(db);
        var provider = BuildProvider(db);
        var personnel = provider.GetRequiredService<PersonnelViewModel>();
        await personnel.InitializeAsync();
        var node = personnel.Tree.SelectedNode!;
        Assert.Equal("Курс", Assert.Single(personnel.Breadcrumb).Text);
        Assert.Contains("«Курс»", personnel.FooterText);

        await personnel.Tree.RenameCommand.ExecuteAsync(node);

        Assert.Equal("Перший курс", Assert.Single(personnel.Breadcrumb).Text);
        Assert.Contains("«Перший курс»", personnel.FooterText);
        Assert.Equal("Перший курс", personnel.FullPathToolTip);
    }
}
