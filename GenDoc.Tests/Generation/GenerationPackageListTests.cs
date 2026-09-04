using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Services.Recipients;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Generation;

namespace GenDoc.Tests.Generation;

public class GenerationPackageListTests
{
    private sealed record Seeded(int TemplateId, int OtherTemplateId, int AlphaId, int BetaId);

    private static Seeded Seed(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now });
        var template = new Template
        {
            Name = "Рапорт", OriginalFileName = "a.docx", Content = new byte[] { 1 },
            UploadedAt = DateTime.Now, Kind = GenDoc.Models.Enums.TemplateKind.PerRecipient
        };
        var other = new Template
        {
            Name = "Довідка", OriginalFileName = "b.docx", Content = new byte[] { 1 },
            UploadedAt = DateTime.Now, Kind = GenDoc.Models.Enums.TemplateKind.PerRecipient
        };
        ctx.Templates.AddRange(template, other);
        ctx.SaveChanges();
        var alpha = new GenerationPackage { Name = "Альфа" };
        alpha.Templates.Add(new GenerationPackageTemplate { TemplateId = template.Id, SortOrder = 0 });
        var beta = new GenerationPackage { Name = "Бета" };
        beta.Templates.Add(new GenerationPackageTemplate { TemplateId = template.Id, SortOrder = 0 });
        ctx.GenerationPackages.AddRange(alpha, beta);
        ctx.SaveChanges();
        return new Seeded(template.Id, other.Id, alpha.Id, beta.Id);
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

    [Fact]
    public async Task RefreshPackages_KeepsTheSelectedPackageHighlighted_WithAFreshTemplateCount()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var vm = await CreateViewModelAsync(db);
        await vm.SelectPackageCommand.ExecuteAsync(vm.Packages.Single(p => p.Id == s.AlphaId));
        Assert.Equal("1 шабл.", vm.SelectedPackage!.TemplateCountDisplay);

        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.GenerationPackageTemplates.Add(new GenerationPackageTemplate
            { GenerationPackageId = s.AlphaId, TemplateId = s.OtherTemplateId, SortOrder = 1 });
            ctx.SaveChanges();
        }

        vm.RefreshPackages();

        var alpha = vm.Packages.Single(p => p.Id == s.AlphaId);
        Assert.Same(alpha, vm.SelectedPackage);
        Assert.True(alpha.IsSelected);
        Assert.Equal("2 шабл.", alpha.TemplateCountDisplay);
        Assert.False(vm.Packages.Single(p => p.Id == s.BetaId).IsSelected);
    }

    [Fact]
    public async Task CreatePackage_SelectsTheNewPackage()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var vm = await CreateViewModelAsync(db);
        vm.ShowCreatePackageCommand.Execute(null);
        vm.NewPackageName = "Гамма";
        vm.TemplateCheckItems.Single(t => t.Id == s.OtherTemplateId).IsChecked = true;

        await vm.CreatePackageCommand.ExecuteAsync(null);

        Assert.False(vm.IsCreatingPackage);
        Assert.Equal("Гамма", vm.SelectedPackage!.Name);
        Assert.True(vm.Packages.Single(p => p.Name == "Гамма").IsSelected);
        Assert.Equal("Довідка", Assert.Single(vm.PackageTemplates).Name);
    }

    [Fact]
    public async Task DeletingAnotherPackage_KeepsTheCurrentSelectionHighlighted()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var vm = await CreateViewModelAsync(db);
        await vm.SelectPackageCommand.ExecuteAsync(vm.Packages.Single(p => p.Id == s.AlphaId));

        vm.DeletePackageCore(vm.Packages.Single(p => p.Id == s.BetaId));

        Assert.DoesNotContain(vm.Packages, p => p.Id == s.BetaId);
        var alpha = Assert.Single(vm.Packages);
        Assert.Same(alpha, vm.SelectedPackage);
        Assert.True(alpha.IsSelected);
    }

    [Fact]
    public async Task DeletingTheSelectedPackage_ClearsTheSelection()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var vm = await CreateViewModelAsync(db);
        await vm.SelectPackageCommand.ExecuteAsync(vm.Packages.Single(p => p.Id == s.AlphaId));

        vm.DeletePackageCore(vm.Packages.Single(p => p.Id == s.AlphaId));

        Assert.Null(vm.SelectedPackage);
        Assert.Empty(vm.PackageTemplates);
        Assert.All(vm.Packages, p => Assert.False(p.IsSelected));
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
