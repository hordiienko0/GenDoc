using System.Windows;
using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Services.Import;
using GenDoc.Services.Intakes;
using GenDoc.Services.OrgTree;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Import;
using GenDoc.ViewModels.Personnel;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.Tests.Import;

public class ImportNewIntakeTargetTests
{
    private sealed class NoCounts : ICountService
    {
        public Task<Dictionary<int, int>> GetTreeCountsAsync() => Task.FromResult(new Dictionary<int, int>());
    }

    private sealed class WizardDialog : IDialogService
    {
        private readonly bool _confirm;

        public WizardDialog(bool confirm) => _confirm = confirm;

        public int Shown { get; private set; }

        public bool? ShowDialog<TViewModel>(TViewModel viewModel, Window? owner = null) where TViewModel : notnull
        {
            if (viewModel is not IntakeWizardViewModel wizard) return null;
            Shown++;
            if (!_confirm) return false;

            Task.Run(() => wizard.CreateCommand.ExecuteAsync(null)).GetAwaiter().GetResult();
            return wizard.CreatedIntake is not null;
        }
    }

    private static (ImportViewModel Vm, WizardDialog Dialog) Arrange(TestDb db, bool confirm)
    {
        using (var ctx = db.Factory.CreateDbContext())
        {
            var root = new OrgNode { Name = "Курс", Depth = 0, SortOrder = 0, Path = "/" };
            ctx.OrgNodes.Add(root);
            ctx.SaveChanges();
            root.Path = $"/{root.Id}/";
            ctx.SaveChanges();
        }

        var dialog = new WizardDialog(confirm);
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
        services.AddTransient<IntakeWizardViewModel>();
        var provider = services.BuildServiceProvider();

        var vm = new ImportViewModel(
            new ImportService(db.Factory, new FakeAuditLog()),
            provider.GetRequiredService<IIntakeService>(),
            provider.GetRequiredService<IOrgTreeService>(),
            dialog,
            provider);

        vm.HasFile = true;
        vm.CurrentStep = 2;
        vm.TargetExistingIntake = false;
        vm.TargetNewIntake = true;
        return (vm, dialog);
    }

    [Fact]
    public async Task Next_with_new_intake_chosen_creates_the_intake_and_targets_it()
    {
        using var db = new TestDb();
        var (vm, dialog) = Arrange(db, confirm: true);

        await vm.GoNextCommand.ExecuteAsync(null);

        Assert.Equal(1, dialog.Shown);
        Assert.Equal(3, vm.CurrentStep);
        using var ctx = db.Factory.CreateDbContext();
        var intake = Assert.Single(ctx.Intakes.ToList());
        var target = vm.BuildTarget();
        Assert.Equal(ImportTargetKind.Intake, target.Kind);
        Assert.Equal(intake.Id, target.IntakeId);
        Assert.True(vm.TargetExistingIntake);
        Assert.Equal(intake.Id, vm.SelectedIntake?.Id);
    }

    [Fact]
    public async Task Cancelling_the_wizard_stays_on_the_target_step_without_an_intake()
    {
        using var db = new TestDb();
        var (vm, dialog) = Arrange(db, confirm: false);

        await vm.GoNextCommand.ExecuteAsync(null);

        Assert.Equal(1, dialog.Shown);
        Assert.Equal(2, vm.CurrentStep);
        Assert.True(vm.TargetNewIntake);
        using var ctx = db.Factory.CreateDbContext();
        Assert.Empty(ctx.Intakes.ToList());
    }

    [Fact]
    public void New_intake_target_never_falls_back_to_the_file_target()
    {
        using var db = new TestDb();
        var (vm, _) = Arrange(db, confirm: true);

        Assert.NotEqual(ImportTargetKind.FromFile, vm.BuildTarget().Kind);
    }
}
