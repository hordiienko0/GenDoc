using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Services.Recipients;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Generation;
using TemplateKind = GenDoc.Models.Enums.TemplateKind;

namespace GenDoc.Tests.Generation;

public class ResultBlockPolishTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-result-{Guid.NewGuid():N}");
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
        var personal = new Template
        {
            Name = "Рапорт", OriginalFileName = "rapport.docx",
            Content = TemplateFixtures.Bytes(TemplateFixtures.RaportIndividualDocx),
            UploadedAt = DateTime.Now, Kind = TemplateKind.PerRecipient
        };
        var group = new Template
        {
            Name = "Список", OriginalFileName = "group.docx",
            Content = TemplateFixtures.Bytes(TemplateFixtures.RaportGroupDocx),
            UploadedAt = DateTime.Now, Kind = TemplateKind.Group
        };
        ctx.Templates.AddRange(personal, group);
        ctx.Recipients.AddRange(TemplateFixtures.Roster(2));
        ctx.SaveChanges();
        foreach (var tag in new[] { "{{звання_зв}}", "{{піб_зв}}", "{{прибув}}", "{{таким}}" })
        {
            var (sourceType, fieldName) = GenDoc.Services.Templates.PlaceholderTagMaps.Classify(tag);
            ctx.TemplateFieldMappings.Add(new TemplateFieldMapping
            { TemplateId = personal.Id, PlaceholderTag = tag, SourceType = sourceType, FieldName = fieldName });
        }
        var package = new GenerationPackage { Name = "Пакет" };
        package.Templates.Add(new GenerationPackageTemplate { TemplateId = personal.Id, SortOrder = 0 });
        package.Templates.Add(new GenerationPackageTemplate { TemplateId = group.Id, SortOrder = 1 });
        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();
        return package.Id;
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
    public void RunPackage_ReportsTheFirstGeneratedFile_InsideTheOutputFolder()
    {
        using var db = new TestDb();
        var packageId = Seed(db);

        var result = TestServices.Generation(db).RunPackage(
            packageId, _folder, new Dictionary<string, string>(), false,
            new RosterSelection(true, Array.Empty<int>(), GenDoc.Models.Enums.FitnessFilter.All, false,
                Array.Empty<RankCategory>(), Array.Empty<string>()),
            new Progress<string>());

        Assert.True(result.Generated > 0);
        Assert.NotNull(result.FirstGeneratedPath);
        Assert.True(File.Exists(result.FirstGeneratedPath));
        Assert.StartsWith(_folder, result.FirstGeneratedPath!, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(_folder.TrimEnd('\\'), Path.GetDirectoryName(result.FirstGeneratedPath)!.TrimEnd('\\'));
    }

    [Fact]
    public void FolderToOpen_UsesTheFolderOfTheFirstGeneratedFile()
    {
        var runFolder = Path.Combine(_folder, "Набір №5", "Рапорт");
        Directory.CreateDirectory(runFolder);
        var file = Path.Combine(runFolder, "ШЕВЧЕНКО.docx");
        File.WriteAllText(file, "x");

        var vm = new GenerationResultViewModel(
            new RunResult(1, 0, 0, 0, 0, 0, 0, 0, 0, FirstGeneratedPath: file), _folder);

        Assert.Equal(runFolder, vm.FolderToOpen);
    }

    [Fact]
    public void FolderToOpen_FallsBackToTheOutputFolder_WithoutGeneratedFiles()
    {
        var missing = Path.Combine(_folder, "немає", "файл.docx");

        Assert.Equal(_folder, new GenerationResultViewModel(new RunResult(0, 2, 0, 0, 0, 0, 0, 0, 0), _folder).FolderToOpen);
        Assert.Equal(_folder, new GenerationResultViewModel(
            new RunResult(0, 0, 1, 0, 0, 0, 0, 0, 0, FirstGeneratedPath: missing), _folder).FolderToOpen);
    }

    [Fact]
    public void RunIssue_Subject_DropsTheEmptyPersonPrefix()
    {
        Assert.Equal("ШЕВЧЕНКО Тарас · Рапорт",
            new RunIssue(RunIssue.PhaseDocx, "ШЕВЧЕНКО Тарас", "Рапорт", "немає тегу").Subject);
        Assert.Equal("Залік", new RunIssue(RunIssue.PhaseXlsx, string.Empty, "Залік", "немає тегу").Subject);
        Assert.Equal("Залік", new RunIssue(RunIssue.PhaseXlsx, " ", "Залік", "немає тегу").Subject);
    }

    [Fact]
    public void RunIssue_Subject_IsNotSerialized()
    {
        var json = RunIssue.Serialize(new[] { new RunIssue(RunIssue.PhaseXlsx, string.Empty, "Залік", "текст") });

        Assert.DoesNotContain(nameof(RunIssue.Subject), json);
        Assert.True(RunIssue.TryDeserialize(json, out var issues));
        Assert.Equal("Залік", Assert.Single(issues).Subject);
    }

    [Fact]
    public async Task RankListToggleLabel_FlipsTheArrow()
    {
        using var db = new TestDb();
        Seed(db);
        var vm = await CreateViewModelAsync(db);
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        Assert.Equal("Окремі звання ▾", vm.RankListToggleLabel);

        vm.IsRankListExpanded = true;

        Assert.Equal("Окремі звання ▴", vm.RankListToggleLabel);
        Assert.Contains(nameof(GenerationViewModel.RankListToggleLabel), changes);
    }

    [Fact]
    public void GetPackageTemplates_ReportsTheTemplateKind()
    {
        using var db = new TestDb();
        var packageId = Seed(db);

        var templates = TestServices.Generation(db).GetPackageTemplates(packageId);

        Assert.Collection(templates,
            t => { Assert.Equal("Рапорт", t.TemplateName); Assert.Equal(TemplateKind.PerRecipient, t.Kind); },
            t => { Assert.Equal("Список", t.TemplateName); Assert.Equal(TemplateKind.Group, t.Kind); });
    }

    [Fact]
    public async Task PackageChips_MarkGroupDocxTemplates()
    {
        using var db = new TestDb();
        var packageId = Seed(db);
        var vm = await CreateViewModelAsync(db);

        await vm.SelectPackageCommand.ExecuteAsync(vm.Packages.Single(p => p.Id == packageId));

        var personal = vm.PackageTemplates.Single(t => t.Name == "Рапорт");
        var group = vm.PackageTemplates.Single(t => t.Name == "Список");
        Assert.False(personal.IsGroup);
        Assert.True(group.IsGroup);
        Assert.Equal("docx", group.KindBadge);
        Assert.False(new PackageTemplateSummaryItemViewModel("Залік", GenDoc.Models.Enums.FitnessFilter.All, 3).IsGroup);
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
