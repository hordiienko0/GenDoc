using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Services.Recipients;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Generation;

namespace GenDoc.Tests.Generation;

public class GenerationViewModelRosterScopeTests
{
    private sealed record Seeded(Intake ActiveIntake, int[] CadetIds, int StaffId, int OtherIntakeMemberId, int PackageId);

    private static Seeded Seed(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now });

        var active = new Intake { Number = 5, DisplayNumber = "Набір №5", Status = IntakeStatus.Active };
        var other = new Intake { Number = 4, DisplayNumber = "Набір №4", Status = IntakeStatus.Completed };
        ctx.Intakes.AddRange(active, other);
        ctx.SaveChanges();

        var cadets = new[]
        {
            new Recipient { LastName = "ШЕВЧЕНКО", FirstName = "Тарас", Rank = "солдат", IntakeId = active.Id },
            new Recipient { LastName = "КОВАЛЬ", FirstName = "Оксана", Rank = "солдат", IntakeId = active.Id }
        };
        var staff = new Recipient { LastName = "ОФІЦЕР", FirstName = "Курсовий", Rank = "майор", IntakeId = null };
        var otherMember = new Recipient { LastName = "МИНУЛИЙ", FirstName = "Набір", Rank = "солдат", IntakeId = other.Id };
        ctx.Recipients.AddRange(cadets[0], cadets[1], staff, otherMember);

        var export = new ExportTemplate { Name = "Залік", OriginalFileName = "z.xlsx", UploadedAt = DateTime.Now, UsesPlaceholders = true };
        ctx.ExportTemplates.Add(export);
        ctx.SaveChanges();

        var package = new GenerationPackage { Name = "Пакет" };
        package.ExportTemplates.Add(new GenerationPackageExportTemplate
        {
            ExportTemplateId = export.Id, SortOrder = 0, FitnessFilter = FitnessFilter.All
        });
        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();

        return new Seeded(active, cadets.Select(c => c.Id).ToArray(), staff.Id, otherMember.Id, package.Id);
    }

    private static async Task<GenerationViewModel> CreateViewModelAsync(TestDb db, Intake? activeIntake)
    {
        var vm = new GenerationViewModel(
            TestServices.Generation(db),
            new NoDialogs(),
            null!,
            TestServices.Completeness(db, 1, activeIntake),
            new RecipientService(db.Factory, new FakeAuditLog(), new FakeCurrentUser()),
            new NoManualTags(),
            new OutputFolderService(db.Factory),
            TestServices.UserSettings(db, 1),
            activeIntake is null ? new FakeIntakeAccessor() : new FakeIntakeAccessor(activeIntake),
            new WeakReferenceMessenger());

        await vm.InitialLoad;
        return vm;
    }

    [Fact]
    public async Task Roster_WithActiveIntake_ListsOnlyItsMembers()
    {
        using var db = new TestDb();
        var seeded = Seed(db);

        var vm = await CreateViewModelAsync(db, seeded.ActiveIntake);

        Assert.Equal(seeded.CadetIds.OrderBy(i => i), vm.RecipientOptions.Select(r => r.Id).OrderBy(i => i));
        Assert.Equal(2, vm.RecipientCount);
        Assert.True(vm.HasActiveIntake);
        Assert.False(vm.IncludePermanentStaff);
        Assert.Equal("Згенерувати всім (Набір №5: 2)", vm.GenerateButtonText);
        Assert.Equal("У списку — Набір №5, без постійного складу.", vm.RosterScopeHint);
    }

    [Fact]
    public async Task Roster_IncludePermanentStaff_AddsStaffButNotOtherIntakes()
    {
        using var db = new TestDb();
        var seeded = Seed(db);
        var vm = await CreateViewModelAsync(db, seeded.ActiveIntake);

        vm.IncludePermanentStaff = true;

        Assert.Contains(seeded.StaffId, vm.RecipientOptions.Select(r => r.Id));
        Assert.DoesNotContain(seeded.OtherIntakeMemberId, vm.RecipientOptions.Select(r => r.Id));
        Assert.Equal(3, vm.RecipientCount);
        Assert.Equal("Згенерувати всім (Набір №5: 3)", vm.GenerateButtonText);
        Assert.Equal("У списку — Набір №5 і постійний склад.", vm.RosterScopeHint);
    }

    [Fact]
    public async Task Roster_WithoutActiveIntake_FallsBackToEveryoneWithHint()
    {
        using var db = new TestDb();
        Seed(db);

        var vm = await CreateViewModelAsync(db, null);

        Assert.Equal(4, vm.RecipientOptions.Count);
        Assert.Equal(4, vm.RecipientCount);
        Assert.False(vm.HasActiveIntake);
        Assert.Equal("Згенерувати всім (4)", vm.GenerateButtonText);
        Assert.Equal("Активного набору немає — у списку всі люди бази, включно з постійним складом.", vm.RosterScopeHint);
    }

    [Fact]
    public async Task Roster_ReactsOnlyToItsOwnMessenger_NotToTheGlobalOne()
    {
        using var db = new TestDb();
        var seeded = Seed(db);
        var messenger = new WeakReferenceMessenger();
        var vm = new GenerationViewModel(
            TestServices.Generation(db),
            new NoDialogs(),
            null!,
            TestServices.Completeness(db, 1, seeded.ActiveIntake),
            new RecipientService(db.Factory, new FakeAuditLog(), new FakeCurrentUser()),
            new NoManualTags(),
            new OutputFolderService(db.Factory),
            TestServices.UserSettings(db, 1),
            new FakeIntakeAccessor(seeded.ActiveIntake),
            messenger);
        await vm.InitialLoad;
        var hintChanges = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.RosterScopeHint)) hintChanges++; };

        WeakReferenceMessenger.Default.Send(new ActiveIntakeChangedMessage());
        Assert.Equal(0, hintChanges);

        messenger.Send(new ActiveIntakeChangedMessage());
        Assert.Equal(1, hintChanges);
    }

    [Fact]
    public async Task PackageChips_CountTheSameRosterAsTheGenerateButton()
    {
        using var db = new TestDb();
        var seeded = Seed(db);
        var vm = await CreateViewModelAsync(db, seeded.ActiveIntake);

        await vm.SelectPackageCommand.ExecuteAsync(vm.Packages.Single(p => p.Id == seeded.PackageId));
        var chip = vm.PackageTemplates.Single(t => t.IsXlsx);
        Assert.Equal(2, chip.PersonCount);

        vm.UseAllRecipients = false;
        vm.RecipientOptions.First().IsChecked = true;
        Assert.Equal(1, chip.PersonCount);
        Assert.Equal("усі · 1 осіб", chip.XlsxDetail);

        vm.UseAllRecipients = true;
        vm.IncludePermanentStaff = true;
        Assert.Equal(3, chip.PersonCount);
        Assert.Equal(vm.RecipientCount, chip.PersonCount);
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
