using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Completeness;
using GenDoc.Services.Documents;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Archive;
using GenDoc.ViewModels.Completeness;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.Tests.Completeness;

public class CompletenessListsRefreshTests
{
    private sealed record Seeded(int FirstIntakeId, int SecondIntakeId, int PersonId, int TemplateId, int AlphaId, int BetaId);

    private static Seeded Seed(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
        var first = new Intake { Number = 1, DisplayNumber = "Набір №1", Status = IntakeStatus.Active };
        var second = new Intake { Number = 2, DisplayNumber = "Набір №2", Status = IntakeStatus.Planned };
        ctx.Intakes.AddRange(first, second);
        var person = TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас");
        person.FitnessCategory = "придатний";
        ctx.Recipients.Add(person);
        var template = new Template
        {
            Name = "Акт", OriginalFileName = "a.docx", Content = new byte[] { 1 },
            UploadedAt = DateTime.Now, Kind = TemplateKind.PerRecipient
        };
        ctx.Templates.Add(template);
        ctx.SaveChanges();
        person.IntakeId = first.Id;

        var alpha = new GenerationPackage { Name = "Альфа" };
        alpha.Templates.Add(new GenerationPackageTemplate { TemplateId = template.Id, SortOrder = 0 });
        var beta = new GenerationPackage { Name = "Бета" };
        beta.Templates.Add(new GenerationPackageTemplate { TemplateId = template.Id, SortOrder = 0 });
        ctx.GenerationPackages.AddRange(alpha, beta);
        ctx.SaveChanges();
        return new Seeded(first.Id, second.Id, person.Id, template.Id, alpha.Id, beta.Id);
    }

    private static CompletenessViewModel CreateViewModel(TestDb db, IDialogService dialogs, ICompletenessService? completeness = null)
    {
        var provider = new ServiceCollection()
            .AddSingleton<IGenerationService>(TestServices.Generation(db))
            .AddSingleton<IOutputFolderService>(new OutputFolderService(db.Factory))
            .BuildServiceProvider();

        return new CompletenessViewModel(
            completeness ?? TestServices.Completeness(db, 1),
            TestServices.Archive(db),
            dialogs,
            provider,
            TestServices.ManualTagForm(db, TestServices.Staff(db, 1), new FakeCurrentUser(), new FakeIntakeAccessor(), 1));
    }

    [Fact]
    public async Task EnteringAgain_RefreshesIntakeAndPackageLists_AndKeepsAValidSelection()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var vm = CreateViewModel(db, new NoDialog());
        await vm.InitializeAsync();
        vm.SelectedIntake = vm.IntakeOptions.Single(o => o.Id == s.SecondIntakeId);
        vm.SelectedPackage = vm.PackageOptions.Single(o => o.Id == s.BetaId);

        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Intakes.Single(i => i.Id == s.SecondIntakeId).Status = IntakeStatus.Completed;
            ctx.GenerationPackages.Add(new GenerationPackage { Name = "Гамма" });
            ctx.SaveChanges();
        }
        TestServices.Generation(db).DeletePackage(s.BetaId);

        await vm.InitializeAsync();

        Assert.Equal(s.SecondIntakeId, vm.SelectedIntake!.Id);
        Assert.Contains("завершений", vm.SelectedIntake.Label);
        Assert.Contains(vm.PackageOptions, o => o.Label == "Гамма");
        Assert.DoesNotContain(vm.PackageOptions, o => o.Id == s.BetaId);
        Assert.NotNull(vm.SelectedPackage);
        Assert.NotEqual(s.BetaId, vm.SelectedPackage!.Id);
    }

    [Fact]
    public async Task EnteringAgain_KeepsTheChosenPackage_WhenItStillExists()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var vm = CreateViewModel(db, new NoDialog());
        await vm.InitializeAsync();
        vm.SelectedPackage = vm.PackageOptions.Single(o => o.Id == s.BetaId);

        await vm.InitializeAsync();

        Assert.Equal(s.BetaId, vm.SelectedPackage!.Id);
        Assert.Equal(s.FirstIntakeId, vm.SelectedIntake!.Id);
    }

    [Fact]
    public async Task PackageLinks_OfADeletedPackage_AreEmpty()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var service = TestServices.Completeness(db, 1);
        Assert.Single(await service.GetPackageLinksAsync(s.BetaId));

        TestServices.Generation(db).DeletePackage(s.BetaId);

        Assert.Empty(await service.GetPackageLinksAsync(s.BetaId));
    }

    [Fact]
    public async Task VersionHistoryWithChanges_AnnouncesTheMatrixChange()
    {
        using var db = new TestDb();
        var s = Seed(db);
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.GeneratedDocuments.Add(new GeneratedDocument
            {
                RecipientId = s.PersonId, TemplateId = s.TemplateId, IntakeId = s.FirstIntakeId,
                FileName = "a.docx", GeneratedAt = DateTime.Now, GeneratedByUserId = 1,
                Version = 1, IsCurrent = true, HasContent = true
            });
            ctx.SaveChanges();
        }
        var vm = CreateViewModel(db, new HistoryWithChanges());
        await vm.InitializeAsync();
        vm.SelectedPackage = vm.PackageOptions.Single(o => o.Id == s.AlphaId);
        await vm.RebuildObservedAsync();
        var cell = vm.Rows.Single().Cells.Single(c => c.DocumentId is not null);

        var recipient = new object();
        var received = 0;
        WeakReferenceMessenger.Default.Register<MatrixChangedMessage>(recipient, (_, _) => received++);
        try
        {
            await vm.HistoryAsync(cell);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
        }

        Assert.True(received >= 1);
    }

    [Fact]
    public async Task ABackgroundRebuildThatFails_ReportsInTheFooterInsteadOfThrowing()
    {
        using var db = new TestDb();
        Seed(db);
        var vm = CreateViewModel(db, new NoDialog(), new BrokenCompleteness());

        await vm.RebuildObservedAsync();

        Assert.StartsWith("Не вдалося побудувати матрицю", vm.FooterText);
        Assert.Contains("база недоступна", vm.FooterText);
        Assert.False(vm.IsBusy);
    }

    private sealed class NoDialog : IDialogService
    {
        public bool? ShowDialog<TViewModel>(TViewModel viewModel, Window? owner = null) where TViewModel : notnull => null;
    }

    private sealed class HistoryWithChanges : IDialogService
    {
        public bool? ShowDialog<TViewModel>(TViewModel viewModel, Window? owner = null) where TViewModel : notnull
        {
            if (viewModel is not VersionHistoryViewModel history) return null;
            history.HasChanges = true;
            return true;
        }
    }

    private sealed class BrokenCompleteness : ICompletenessService
    {
        private static Exception Broken() => new InvalidOperationException("база недоступна");

        public Task<MatrixData> BuildAsync(int intakeId, int packageId) => throw Broken();
        public Task<(int Percent, int IncompletePeople, int RequiredCells, int SatisfiedCells)> GetIntakeSummaryAsync(int intakeId, int packageId) => throw Broken();
        public Task<MatrixDocDto?> GetCellAsync(int recipientId, int templateId) => throw Broken();
        public Task<ArchiveOpResult> GenerateForPairAsync(int recipientId, int templateId, Dictionary<string, string> manualValues, int? courseOfficerId = null) => throw Broken();
        public Task<(int People, int Files, List<string> Warnings)> ExportPackagesAsync(IReadOnlyList<int> recipientIds, int packageId, string targetFolder) => throw Broken();
        public Task<int> GetBadgeCountAsync() => throw Broken();
        public Task<(int Missing, int Stale)> GetBadgeBreakdownAsync() => throw Broken();
        public Task<int?> GetDefaultPackageIdAsync() => throw Broken();
        public Task<int?> GetDefaultPackageIdAsync(int intakeId) => throw Broken();
        public Task<List<RecipientDocStatus>> GetRecipientStatusAsync(int recipientId, int packageId) => throw Broken();
        public Task<List<PackageGroupDocumentStatus>> GetPackageGroupDocumentsAsync(int packageId, int? intakeId, int? recipientId = null) => throw Broken();
        public Task<(int Generated, int Skipped, List<string> Errors)> GenerateMissingForRecipientAsync(int recipientId, int packageId, Dictionary<string, string> manualValues) => throw Broken();
        public Task<List<MatrixTemplateInfo>> GetPackageLinksAsync(int packageId) => throw Broken();
        public Task<List<(int Id, string Name)>> GetTemplatesNotInPackageAsync(int packageId) => throw Broken();
        public Task<List<int>> GetTemplateIdsWithDocumentsAsync(IReadOnlyList<int> templateIds) => throw Broken();
        public Task SaveRequirementsAsync(int packageId, IReadOnlyList<RequirementRow> rows) => throw Broken();
    }
}
