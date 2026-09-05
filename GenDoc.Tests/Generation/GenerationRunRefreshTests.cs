using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Services.Recipients;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Generation;

namespace GenDoc.Tests.Generation;

public class GenerationRunRefreshTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-run-{Guid.NewGuid():N}");
    public void Dispose() { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }

    private static int Seed(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now });
        ctx.OrganizationSettings.Add(new OrganizationSettings
        {
            UnitNumber = "А1234", City = "Львів", CommanderRank = "полковник", CommanderFullName = "І. ПЕТРЕНКО",
            CommanderPosition = "начальник", HrOfficerFullName = "К. КАДРОВ", UnitFullName = "Коледж"
        });
        var template = new Template
        {
            Name = "Рапорт", OriginalFileName = "rapport.docx",
            Content = TemplateFixtures.Bytes(TemplateFixtures.RaportIndividualDocx),
            UploadedAt = DateTime.Now, Kind = GenDoc.Models.Enums.TemplateKind.PerRecipient
        };
        ctx.Templates.Add(template);
        ctx.Recipients.AddRange(TemplateFixtures.Roster(2));
        ctx.SaveChanges();
        foreach (var tag in new[] { "{{звання_зв}}", "{{піб_зв}}", "{{прибув}}", "{{таким}}" })
        {
            var (sourceType, fieldName) = GenDoc.Services.Templates.PlaceholderTagMaps.Classify(tag);
            ctx.TemplateFieldMappings.Add(new TemplateFieldMapping
            { TemplateId = template.Id, PlaceholderTag = tag, SourceType = sourceType, FieldName = fieldName });
        }
        var package = new GenerationPackage { Name = "Пакет" };
        package.Templates.Add(new GenerationPackageTemplate { TemplateId = template.Id, SortOrder = 0 });
        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();
        return package.Id;
    }

    private static async Task<GenerationViewModel> CreateViewModelAsync(TestDb db, IMessenger messenger)
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
            messenger);
        await vm.InitialLoad;
        return vm;
    }

    [Fact]
    public async Task GenerateAll_AnnouncesTheMatrixChange_RefreshesLastRun_AndDropsTheOldResult()
    {
        using var db = new TestDb();
        var packageId = Seed(db);
        var messenger = new WeakReferenceMessenger();
        var vm = await CreateViewModelAsync(db, messenger);
        await vm.SelectPackageCommand.ExecuteAsync(vm.Packages.Single(p => p.Id == packageId));
        vm.OutputFolder = _folder;
        vm.LastResult = new GenerationResultViewModel(new RunResult(0, 0, 0, 0, 0, 0, 0, 0, 0), _folder);
        Assert.False(vm.HasLastRun);

        var received = 0;
        messenger.Register<MatrixChangedMessage>(this, (_, _) => received++);
        var results = new List<GenerationResultViewModel?>();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.LastResult)) results.Add(vm.LastResult); };

        await vm.GenerateAllCommand.ExecuteAsync(null);

        Assert.Equal(1, received);
        Assert.True(vm.HasLastRun);
        Assert.Equal("Пакет", vm.LastRunPackageName);
        Assert.Contains("згенеровано 2", vm.LastRunSummary);
        Assert.Null(results[0]);
        Assert.NotNull(vm.LastResult);
        Assert.StartsWith("Згенеровано 2", vm.LastResult!.SummaryText);
    }

    private sealed class NoDialogs : IDialogService
    {
        public bool? ShowDialog<TViewModel>(TViewModel viewModel, Window? owner = null) where TViewModel : notnull => null;
    }

    private sealed class NoManualTags : IManualTagFormBuilder
    {
        public Task<ManualTagFormViewModel> BuildAsync(IReadOnlyList<string> tags, string contextKey, bool needsCourseOfficer = false, int? intakeId = null)
            => throw new NotSupportedException();

        public Task SaveAsync(string contextKey, ManualTagFormViewModel form) => Task.CompletedTask;
    }
}
