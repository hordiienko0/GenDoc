using System.Windows;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Services.Intakes;
using GenDoc.Services.Navigation;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Archive;
using GenDoc.ViewModels.Generation;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.Tests.Archive;

public class ArchiveViewModelRefreshTests
{
    private sealed record Seeded(int FirstIntakeId, int SecondIntakeId, int PackageId, int ExportTemplateId);

    private static Seeded Seed(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now });
        var first = new Intake
        {
            Number = 1, DisplayNumber = "Набір №1", Status = IntakeStatus.Active, StatusIsPinned = true,
            DateStart = DateOnly.FromDateTime(DateTime.Today), DateEnd = DateOnly.FromDateTime(DateTime.Today.AddMonths(3))
        };
        var second = new Intake
        {
            Number = 2, DisplayNumber = "Набір №2", Status = IntakeStatus.Planned, StatusIsPinned = true,
            DateStart = DateOnly.FromDateTime(DateTime.Today.AddMonths(4)), DateEnd = DateOnly.FromDateTime(DateTime.Today.AddMonths(7))
        };
        ctx.Intakes.AddRange(first, second);
        var package = new GenerationPackage { Name = "Пакет" };
        ctx.GenerationPackages.Add(package);
        var export = new ExportTemplate { Name = "Залік", OriginalFileName = "z.xlsx", UploadedAt = DateTime.Now };
        ctx.ExportTemplates.Add(export);
        ctx.SaveChanges();
        return new Seeded(first.Id, second.Id, package.Id, export.Id);
    }

    private static int AddRun(TestDb db, int packageId, int? intakeId)
    {
        using var ctx = db.Factory.CreateDbContext();
        var run = new GenerationPackageRun
        {
            GenerationPackageId = packageId, RunAt = DateTime.Now, RunByUserId = 1, IntakeId = intakeId, GeneratedCount = 1
        };
        ctx.GenerationPackageRuns.Add(run);
        ctx.SaveChanges();
        return run.Id;
    }

    private static async Task<(ArchiveViewModel Vm, ActiveIntakeState State)> CreateAsync(TestDb db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db.Factory);
        services.AddSingleton<IAuditLogService>(new FakeAuditLog());
        services.AddSingleton<ICurrentUserContext>(new FakeCurrentUser());
        services.AddSingleton<IUserSettingsService>(TestServices.UserSettings(db, 1));
        services.AddSingleton<GenDoc.Services.Completeness.ICompletenessService>(_ => TestServices.Completeness(db, 1));
        services.AddSingleton<IIntakeService, IntakeService>();
        services.AddSingleton<ActiveIntakeState>();
        var provider = services.BuildServiceProvider();

        var state = provider.GetRequiredService<ActiveIntakeState>();
        await state.RefreshAsync();

        var vm = new ArchiveViewModel(
            TestServices.Archive(db), new NoDialogs(), state, new NoManualTags(),
            new OutputFolderService(db.Factory), TestServices.UserSettings(db, 1), new FakeCurrentUser());
        return (vm, state);
    }

    [Fact]
    public async Task EnteringAgain_RefreshesTheIntakeLabel_AndKeepsTheChosenFilter()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var (vm, _) = await CreateAsync(db);
        await vm.InitializeAsync();
        Assert.Equal(s.FirstIntakeId, vm.SelectedIntake!.Id);
        vm.SelectedIntake = vm.IntakeOptions.Single(o => o.Id == s.SecondIntakeId);
        await vm.FilterReload;

        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Intakes.Single(i => i.Id == s.SecondIntakeId).Status = IntakeStatus.Completed;
            ctx.SaveChanges();
        }

        await vm.InitializeAsync();

        Assert.Equal(s.SecondIntakeId, vm.SelectedIntake!.Id);
        Assert.Contains("завершений", vm.SelectedIntake.Label);
    }

    [Fact]
    public async Task EnteringAgain_OnTheRunsTab_ReloadsRuns()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var (vm, _) = await CreateAsync(db);
        await vm.InitializeAsync();
        await vm.SetTabCommand.ExecuteAsync("1");
        Assert.Empty(vm.Runs);

        AddRun(db, s.PackageId, null);
        await vm.InitializeAsync();

        Assert.Single(vm.Runs);
        Assert.True(vm.IsRunsTab);
    }

    [Fact]
    public async Task EnteringAgain_OnTheGroupTab_ReloadsTemplateOptions()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var (vm, _) = await CreateAsync(db);
        await vm.InitializeAsync();
        await vm.SetTabCommand.ExecuteAsync("2");
        Assert.Single(vm.GroupTemplateOptions);

        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.GeneratedGroupDocuments.Add(new GeneratedGroupDocument
            {
                ExportTemplateId = s.ExportTemplateId, IntakeId = s.FirstIntakeId, GeneratedAt = DateTime.Now,
                GeneratedByUserId = 1, FileName = "z.xlsx", Version = 1, IsCurrent = true, HasContent = true
            });
            ctx.SaveChanges();
        }
        await vm.InitializeAsync();

        Assert.Equal(2, vm.GroupTemplateOptions.Count);
        Assert.Single(vm.GroupRows);
    }

    [Fact]
    public async Task ShowInArchive_BeforeTheFirstLoad_OpensTheRunExpanded_WithItsIntakeInTheFilter()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var runId = AddRun(db, s.PackageId, s.SecondIntakeId);
        var (vm, state) = await CreateAsync(db);
        Assert.Equal(s.FirstIntakeId, state.Current!.Id);

        await vm.ApplyNavigationPayloadAsync(new ArchiveRunNavigationPayload(runId));

        Assert.True(vm.IsRunsTab);
        Assert.Equal(s.SecondIntakeId, vm.SelectedIntake!.Id);
        var run = Assert.Single(vm.Runs);
        Assert.True(run.IsExpanded);

        await vm.InitializeAsync();

        Assert.Equal(s.SecondIntakeId, vm.SelectedIntake!.Id);
        Assert.True(Assert.Single(vm.Runs).IsExpanded);
    }

    [Fact]
    public async Task ChangingAFilter_OnTheRunsTab_ReloadsOnlyRuns()
    {
        using var db = new TestDb(recordSql: true);
        var s = Seed(db);
        var (vm, _) = await CreateAsync(db);
        await vm.InitializeAsync();
        await vm.SetTabCommand.ExecuteAsync("1");
        db.ClearSql();

        vm.SelectedIntake = vm.IntakeOptions.Single(o => o.Id == s.SecondIntakeId);
        await vm.FilterReload;

        Assert.True(db.SelectCount("GenerationPackageRuns") >= 1);
        Assert.Equal(0, db.SelectCount("GeneratedDocuments"));
    }

    private sealed class NoDialogs : IDialogService
    {
        public bool? ShowDialog<TViewModel>(TViewModel viewModel, Window? owner = null) where TViewModel : notnull => null;
    }

    private sealed class NoManualTags : IManualTagFormBuilder
    {
        public Task<ManualTagFormViewModel> BuildAsync(IReadOnlyList<string> tags, string contextKey, bool needsCourseOfficer = false)
            => throw new NotSupportedException();

        public Task SaveAsync(string contextKey, ManualTagFormViewModel form) => Task.CompletedTask;
    }
}
