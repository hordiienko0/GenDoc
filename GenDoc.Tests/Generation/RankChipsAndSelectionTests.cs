using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Services.Recipients;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Generation;

namespace GenDoc.Tests.Generation;

public class RankChipsAndSelectionTests
{
    private static void Seed(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now });
        ctx.Recipients.AddRange(
            new Recipient { LastName = "ШЕВЧЕНКО", FirstName = "Тарас", Rank = "солдат" },
            new Recipient { LastName = "КОВАЛЬ", FirstName = "Оксана", Rank = "старший солдат" },
            new Recipient { LastName = "ФРАНКО", FirstName = "Іван", Rank = "сержант" },
            new Recipient { LastName = "ОФІЦЕР", FirstName = "Курсовий", Rank = "майор" });
        ctx.SaveChanges();
    }

    private static async Task<GenerationViewModel> CreateViewModelAsync(TestDb db)
    {
        var vm = new GenerationViewModel(
            TestServices.Generation(db),
            new NoDialogs(),
            null!,
            TestServices.Completeness(db, 1),
            new RecipientService(db.Factory, new FakeAuditLog(), new FakeCurrentUser()),
            new NoManualTags(),
            new OutputFolderService(db.Factory),
            TestServices.UserSettings(db, 1),
            new FakeIntakeAccessor(),
            new WeakReferenceMessenger());
        await vm.InitialLoad;
        return vm;
    }

    private static RecipientCheckRowViewModel Row(GenerationViewModel vm, string lastName)
        => vm.RecipientOptions.Single(r => r.FullName.StartsWith(lastName));

    private static RankCategoryChipViewModel Chip(GenerationViewModel vm, RankCategory category)
        => vm.RankCategoryChips.Single(c => c.Category == category);

    private static RankOptionViewModel Option(GenerationViewModel vm, string rank)
        => vm.RankOptions.Single(o => o.Rank == rank);

    [Fact]
    public async Task RankFilter_HidesRowsButKeepsTheirChecks()
    {
        using var db = new TestDb();
        Seed(db);
        var vm = await CreateViewModelAsync(db);
        vm.UseAllRecipients = false;
        Row(vm, "ШЕВЧЕНКО").IsChecked = true;
        Row(vm, "ОФІЦЕР").IsChecked = true;
        Assert.Equal(2, vm.SelectedRecipientsCount);

        Chip(vm, RankCategory.Officers).IsChecked = true;

        Assert.False(Row(vm, "ШЕВЧЕНКО").IsVisible);
        Assert.True(Row(vm, "ШЕВЧЕНКО").IsChecked);
        Assert.Equal(1, vm.SelectedRecipientsCount);
        Assert.Equal("Обрано 1 з 1", vm.SelectedRecipientsCountLabel);

        Chip(vm, RankCategory.Officers).IsChecked = false;

        Assert.True(Row(vm, "ШЕВЧЕНКО").IsVisible);
        Assert.Equal(2, vm.SelectedRecipientsCount);
        Assert.Equal("Обрано 2 з 4", vm.SelectedRecipientsCountLabel);
    }

    [Fact]
    public async Task CategoryChip_ClickedFromPartialState_ChecksTheWholeCategory()
    {
        using var db = new TestDb();
        Seed(db);
        var vm = await CreateViewModelAsync(db);

        Option(vm, "солдат").IsChecked = true;
        Assert.Null(Chip(vm, RankCategory.Soldiers).IsChecked);

        Chip(vm, RankCategory.Soldiers).IsChecked = false;

        Assert.True(Chip(vm, RankCategory.Soldiers).IsChecked);
        Assert.True(Option(vm, "солдат").IsChecked);
        Assert.True(Option(vm, "старший солдат").IsChecked);
        Assert.False(Option(vm, "сержант").IsChecked);
    }

    [Fact]
    public async Task CategoryChip_ClickedFromCheckedState_UnchecksTheWholeCategory()
    {
        using var db = new TestDb();
        Seed(db);
        var vm = await CreateViewModelAsync(db);
        Chip(vm, RankCategory.Soldiers).IsChecked = true;
        Assert.True(Option(vm, "старший солдат").IsChecked);

        Chip(vm, RankCategory.Soldiers).IsChecked = false;

        Assert.False(Option(vm, "солдат").IsChecked);
        Assert.False(Option(vm, "старший солдат").IsChecked);
        Assert.False(Chip(vm, RankCategory.Soldiers).IsChecked);
    }

    [Fact]
    public async Task CheckAllRecipients_ChecksOnlyRowsMatchingTheSearch()
    {
        using var db = new TestDb();
        Seed(db);
        var vm = await CreateViewModelAsync(db);
        vm.UseAllRecipients = false;
        vm.RecipientSearchText = "шевч";

        vm.CheckAllRecipientsCommand.Execute(null);

        Assert.True(Row(vm, "ШЕВЧЕНКО").IsChecked);
        Assert.False(Row(vm, "КОВАЛЬ").IsChecked);
        Assert.Equal(1, vm.SelectedRecipientsCount);

        vm.RecipientSearchText = string.Empty;
        vm.CheckAllRecipientsCommand.Execute(null);

        Assert.Equal(4, vm.SelectedRecipientsCount);
    }

    [Fact]
    public async Task CheckAllRecipients_SkipsRowsHiddenByTheRankFilter()
    {
        using var db = new TestDb();
        Seed(db);
        var vm = await CreateViewModelAsync(db);
        vm.UseAllRecipients = false;
        Chip(vm, RankCategory.Sergeants).IsChecked = true;

        vm.CheckAllRecipientsCommand.Execute(null);

        Assert.True(Row(vm, "ФРАНКО").IsChecked);
        Assert.False(Row(vm, "ШЕВЧЕНКО").IsChecked);
        Assert.Equal(1, vm.SelectedRecipientsCount);
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
