using System.Windows;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Completeness;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Completeness;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.Tests.Completeness;

public class CompletenessPolishTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-completeness-{Guid.NewGuid():N}");

    public CompletenessPolishTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private sealed class NoDialog : IDialogService
    {
        public bool? ShowDialog<TViewModel>(TViewModel viewModel, Window? owner = null) where TViewModel : notnull => null;
    }

    private sealed record Seeded(int IntakeId, int PackageId, int FitId, int LimitedId, int TemplateId, int GroupTemplateId, int SheetId);

    private static Seeded Seed(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
        ctx.AppSettings.Add(new AppSettings { DetectStaleDocuments = true });

        var intake = new Intake { Number = 1, DisplayNumber = "Набір №1", Status = IntakeStatus.Active };
        ctx.Intakes.Add(intake);

        var fit = TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас");
        fit.FitnessCategory = "придатний";
        var limited = TemplateFixtures.Person(2, "ФРАНКО", "Іван");
        limited.FitnessCategory = "обмежено придатний";
        ctx.Recipients.AddRange(fit, limited);

        var personal = new Template
        {
            Name = "Рапорт", OriginalFileName = "a.docx", Content = new byte[] { 1 },
            UploadedAt = DateTime.Now, Kind = TemplateKind.PerRecipient
        };
        var group = new Template
        {
            Name = "Наказ", OriginalFileName = "n.docx", Content = new byte[] { 1 },
            UploadedAt = DateTime.Now, Kind = TemplateKind.Group
        };
        ctx.Templates.AddRange(personal, group);
        var sheet = new ExportTemplate
        {
            Name = "Роздавальна", OriginalFileName = "r.xlsx", Content = new byte[] { 1 },
            UploadedAt = DateTime.Now, UsesPlaceholders = true
        };
        ctx.ExportTemplates.Add(sheet);
        ctx.SaveChanges();

        fit.IntakeId = intake.Id;
        limited.IntakeId = intake.Id;

        var package = new GenerationPackage { Name = "Пакет" };
        package.Templates.Add(new GenerationPackageTemplate
        {
            TemplateId = personal.Id, SortOrder = 0,
            RequirementRegular = TemplateRequirement.Required, RequirementLimited = TemplateRequirement.Required
        });
        package.Templates.Add(new GenerationPackageTemplate
        {
            TemplateId = group.Id, SortOrder = 1,
            RequirementRegular = TemplateRequirement.Required, RequirementLimited = TemplateRequirement.Optional
        });
        package.ExportTemplates.Add(new GenerationPackageExportTemplate
        {
            ExportTemplateId = sheet.Id, SortOrder = 0, FitnessFilter = FitnessFilter.All
        });
        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();

        ctx.TemplateFieldMappings.Add(new TemplateFieldMapping
        {
            TemplateId = personal.Id, PlaceholderTag = "{{прізвище}}", SourceType = MappingSourceType.Recipient, FieldName = "LastName"
        });
        ctx.SaveChanges();

        return new Seeded(intake.Id, package.Id, fit.Id, limited.Id, personal.Id, group.Id, sheet.Id);
    }

    private static async Task<CompletenessViewModel> CreateViewModelAsync(TestDb db)
    {
        var provider = new ServiceCollection()
            .AddSingleton<IGenerationService>(TestServices.Generation(db))
            .AddSingleton<IOutputFolderService>(new OutputFolderService(db.Factory))
            .BuildServiceProvider();

        var vm = new CompletenessViewModel(
            TestServices.Completeness(db, 1),
            TestServices.Archive(db),
            new NoDialog(),
            provider,
            TestServices.ManualTagForm(db, TestServices.Staff(db, 1), new FakeCurrentUser(), new FakeIntakeAccessor(), 1));
        await vm.InitializeAsync();
        return vm;
    }

    [Fact]
    public async Task GroupDocumentFromTheMatrix_IncludesEveryoneForWhomItIsNotExcluded()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var vm = await CreateViewModelAsync(db);

        var group = vm.CollectMissingTargets().Groups.Single(g => g.Column.TemplateId == s.GroupTemplateId && !g.Column.IsExport);

        Assert.Contains(s.FitId, group.RecipientIds);
        Assert.Contains(s.LimitedId, group.RecipientIds);
    }

    [Fact]
    public async Task EmptyStates_SearchWithoutHits_AndNoPackages()
    {
        using var db = new TestDb();
        Seed(db);
        var vm = await CreateViewModelAsync(db);

        Assert.False(vm.HasNoPackages);
        Assert.False(vm.SearchIsEmpty);

        vm.SearchText = "Коцюбинський";
        vm.ApplySearch();

        Assert.True(vm.SearchIsEmpty);
        Assert.Contains("Коцюбинський", vm.SearchEmptyText);
    }

    [Fact]
    public async Task NoPackages_ShowsAHint()
    {
        using var db = new TestDb();
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
            ctx.Intakes.Add(new Intake { Number = 1, DisplayNumber = "Набір №1", Status = IntakeStatus.Active });
            ctx.SaveChanges();
        }
        var vm = await CreateViewModelAsync(db);

        Assert.True(vm.HasNoPackages);
        Assert.False(vm.ShowMatrix);
    }

    [Fact]
    public void MatrixRow_ShowsTheFitnessCategory()
    {
        var person = TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас");
        person.FitnessCategory = "обмежено придатний";

        var row = new MatrixRowViewModel(person);

        Assert.Contains("обмежено придатний", row.SubText);
    }

    [Fact]
    public void GroupCell_OffersRegenerateAndHistory()
    {
        var cell = new MatrixCellViewModel(new NoopCoordinator(), 1, 2, "придатний", TemplateRequirement.Required, isGroupColumn: true);
        cell.Initialize(new MatrixDocDto(10, 1, 2, 3, true, false, DocumentSourceType.Generated, IsGroup: true));

        Assert.True(cell.CanRegenerate);
        Assert.True(cell.CanHistory);
        Assert.False(cell.CanSaveAs);
    }

    [Fact]
    public void SaveAsFileName_KeepsTheDocumentExtension()
    {
        Assert.Equal("ШЕВЧЕНКО Тарас - Скан.pdf", CompletenessViewModel.SaveAsFileName("ШЕВЧЕНКО Тарас", "Скан", "s/скан.pdf"));
        Assert.Equal("ШЕВЧЕНКО Тарас - Рапорт.docx", CompletenessViewModel.SaveAsFileName("ШЕВЧЕНКО Тарас", "Рапорт", ""));
    }

    [Fact]
    public async Task ExportPackages_IncludesGroupDocumentsOfTheParticipant_AndWarnsAboutStale()
    {
        using var db = new TestDb();
        var s = Seed(db);
        using (var ctx = db.Factory.CreateDbContext())
        {
            var mappings = ctx.TemplateFieldMappings.Where(m => m.TemplateId == s.TemplateId).ToList();
            var fit = ctx.Recipients.First(r => r.Id == s.FitId);
            ctx.GeneratedDocuments.Add(new GeneratedDocument
            {
                RecipientId = s.FitId, TemplateId = s.TemplateId, IntakeId = s.IntakeId,
                FileName = "rapport.docx", GeneratedAt = DateTime.Now, GeneratedByUserId = 1,
                Version = 1, IsCurrent = true, HasContent = true, SizeBytes = 3,
                SourceHash = new GenDoc.Services.Documents.DocumentHashService().ComputeSourceHash(mappings, fit, null),
                Content = new GeneratedDocumentContent { Content = new byte[] { 1, 2, 3 } }
            });
            var sheetDoc = new GeneratedGroupDocument
            {
                ExportTemplateId = s.SheetId, IntakeId = s.IntakeId, GeneratedAt = DateTime.Now, GeneratedByUserId = 1,
                FileName = "rozdavalna.xlsx", Version = 1, IsCurrent = true, HasContent = true, RecipientCount = 1, SizeBytes = 2,
                Content = new GeneratedGroupDocumentContent { Content = new byte[] { 7, 7 } }
            };
            sheetDoc.Recipients.Add(new GeneratedGroupDocumentRecipient { RecipientId = s.FitId });
            ctx.GeneratedGroupDocuments.Add(sheetDoc);
            ctx.SaveChanges();
        }

        var fresh = await TestServices.Completeness(db, 1).ExportPackagesAsync(new[] { s.FitId }, s.PackageId, _folder);
        Assert.Equal(2, fresh.Files);
        Assert.DoesNotContain(fresh.Warnings, w => w.Contains("застарів"));
        Assert.Single(Directory.EnumerateFiles(_folder, "*.xlsx", SearchOption.AllDirectories));

        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Recipients.First(r => r.Id == s.FitId).LastName = "ГРУШЕВСЬКИЙ";
            ctx.SaveChanges();
        }

        var stale = await TestServices.Completeness(db, 1).ExportPackagesAsync(new[] { s.FitId }, s.PackageId, _folder);
        Assert.Contains(stale.Warnings, w => w.Contains("застарів"));
    }

    [Fact]
    public async Task Requirements_NoTemplateIsPreselected_SortOrderRenumbered_SheetsCountForValidation()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var vm = new PackageRequirementsViewModel(TestServices.Completeness(db), TestServices.Generation(db), db.Factory);
        await vm.InitializeAsync(s.PackageId, s.IntakeId);

        Assert.Null(vm.SelectedTemplateToAdd);
        Assert.Null(vm.SelectedExportTemplateToAdd);

        foreach (var row in vm.Rows) row.Limited = TemplateRequirement.NotApplicable;
        Assert.True(vm.CanSave);

        vm.RemoveTemplateCommand.Execute(vm.Rows[0]);
        await vm.SaveCommand.ExecuteAsync(null);

        var links = await TestServices.Completeness(db).GetPackageLinksAsync(s.PackageId);
        var remaining = Assert.Single(links.Where(l => !l.IsExport));
        Assert.Equal(s.GroupTemplateId, remaining.TemplateId);
        Assert.Equal(0, remaining.SortOrder);
    }

    [Fact]
    public async Task Requirements_WithoutSheets_LimitedCategoryMustHaveATemplate()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var vm = new PackageRequirementsViewModel(TestServices.Completeness(db), TestServices.Generation(db), db.Factory);
        await vm.InitializeAsync(s.PackageId, s.IntakeId);

        vm.ExportRows.Clear();
        foreach (var row in vm.Rows) row.Limited = TemplateRequirement.NotApplicable;

        Assert.False(vm.CanSave);
        Assert.NotNull(vm.ValidationError);
    }

    private sealed class NoopCoordinator : ICellActionCoordinator
    {
        public Task OpenAsync(MatrixCellViewModel cell) => Task.CompletedTask;
        public Task GenerateAsync(MatrixCellViewModel cell) => Task.CompletedTask;
        public Task RegenerateAsync(MatrixCellViewModel cell) => Task.CompletedTask;
        public Task HistoryAsync(MatrixCellViewModel cell) => Task.CompletedTask;
        public Task SaveAsAsync(MatrixCellViewModel cell) => Task.CompletedTask;
    }
}
