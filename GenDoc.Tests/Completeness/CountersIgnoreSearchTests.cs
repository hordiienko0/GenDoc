using System.Windows;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Completeness;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.Tests.Completeness;

public class CountersIgnoreSearchTests
{
    private sealed class NoDialog : IDialogService
    {
        public bool? ShowDialog<TViewModel>(TViewModel viewModel, Window? owner = null) where TViewModel : notnull => null;
    }

    private sealed record Seeded(int IntakeId, int PackageId, int DoneId, int GapId, int TemplateId, int SheetId);

    private static Seeded Seed(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
        ctx.AppSettings.Add(new AppSettings { DetectStaleDocuments = true });

        var intake = new Intake { Number = 1, DisplayNumber = "Набір №1", Status = IntakeStatus.Active };
        ctx.Intakes.Add(intake);

        var done = TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас");
        done.FitnessCategory = "придатний";
        var gap = TemplateFixtures.Person(2, "ФРАНКО", "Іван");
        gap.FitnessCategory = "придатний";
        ctx.Recipients.AddRange(done, gap);

        var template = new Template
        {
            Name = "Рапорт", OriginalFileName = "a.docx", Content = new byte[] { 1 },
            UploadedAt = DateTime.Now, Kind = TemplateKind.PerRecipient
        };
        ctx.Templates.Add(template);
        var sheet = new ExportTemplate
        {
            Name = "Роздавальна", OriginalFileName = "r.xlsx", Content = new byte[] { 1 },
            UploadedAt = DateTime.Now, UsesPlaceholders = true
        };
        ctx.ExportTemplates.Add(sheet);
        ctx.SaveChanges();

        done.IntakeId = intake.Id;
        gap.IntakeId = intake.Id;

        var package = new GenerationPackage { Name = "Пакет" };
        package.Templates.Add(new GenerationPackageTemplate
        {
            TemplateId = template.Id, SortOrder = 0,
            RequirementRegular = TemplateRequirement.Required, RequirementLimited = TemplateRequirement.Required
        });
        package.ExportTemplates.Add(new GenerationPackageExportTemplate
        {
            ExportTemplateId = sheet.Id, SortOrder = 0, FitnessFilter = FitnessFilter.All
        });
        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();

        ctx.GeneratedDocuments.Add(new GeneratedDocument
        {
            RecipientId = done.Id, TemplateId = template.Id, IntakeId = intake.Id,
            FileName = "a.docx", GeneratedAt = DateTime.Now, GeneratedByUserId = 1,
            Version = 1, IsCurrent = true, HasContent = true
        });
        var sheetDoc = new GeneratedGroupDocument
        {
            ExportTemplateId = sheet.Id, IntakeId = intake.Id, GeneratedAt = DateTime.Now, GeneratedByUserId = 1,
            FileName = "r.xlsx", Version = 1, IsCurrent = true, HasContent = true, RecipientCount = 1
        };
        sheetDoc.Recipients.Add(new GeneratedGroupDocumentRecipient { RecipientId = done.Id });
        ctx.GeneratedGroupDocuments.Add(sheetDoc);
        ctx.SaveChanges();

        return new Seeded(intake.Id, package.Id, done.Id, gap.Id, template.Id, sheet.Id);
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
    public async Task SearchNarrowsTheTable_ButTheBannerAndButtonsStillCountEveryone()
    {
        using var db = new TestDb();
        Seed(db);
        var vm = await CreateViewModelAsync(db);

        Assert.Equal(2, vm.RowCount);
        Assert.Equal(2, vm.MissingRequiredCount);
        Assert.False(vm.IsFullyComplete);

        vm.SearchText = "Шевченко";
        vm.ApplySearch();

        Assert.Equal(1, vm.RowCount);
        Assert.Equal(2, vm.MissingRequiredCount);
        Assert.False(vm.IsFullyComplete);
        Assert.Equal("Згенерувати все, чого бракує (2)", vm.GenerateMissingLabel);
        Assert.StartsWith("показано 1 з 2 осіб", vm.FooterText);

        vm.SearchText = null;
        vm.ApplySearch();

        Assert.StartsWith("2 осіб", vm.FooterText);
    }

    [Fact]
    public async Task GenerateMissingTargets_IncludeThePeopleHiddenBySearch()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var vm = await CreateViewModelAsync(db);

        vm.SearchText = "Шевченко";
        vm.ApplySearch();

        var targets = vm.CollectMissingTargets();

        Assert.Contains((s.GapId, s.TemplateId), targets.Required);
        Assert.Single(targets.Groups);
        Assert.Equal(s.SheetId, targets.Groups[0].Column.TemplateId);
    }

    [Fact]
    public async Task HeaderCheckbox_FromPartialSelection_SelectsEveryVisibleRow()
    {
        using var db = new TestDb();
        Seed(db);
        var vm = await CreateViewModelAsync(db);

        vm.Rows[0].IsChecked = true;
        Assert.Null(vm.HeaderChecked);

        vm.HeaderChecked = false;

        Assert.True(vm.Rows.All(r => r.IsChecked));
        Assert.True(vm.HeaderChecked);

        vm.HeaderChecked = false;

        Assert.DoesNotContain(vm.Rows, r => r.IsChecked);
        Assert.False(vm.HeaderChecked);
    }
}
