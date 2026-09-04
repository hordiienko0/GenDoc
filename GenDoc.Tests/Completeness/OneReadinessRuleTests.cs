using System.Windows;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Completeness;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.Tests.Completeness;

public class OneReadinessRuleTests
{
    private sealed class NoDialog : IDialogService
    {
        public bool? ShowDialog<TViewModel>(TViewModel viewModel, Window? owner = null) where TViewModel : notnull => null;
    }

    private sealed record Seeded(int IntakeId, int PackageId, int FitId, int LimitedId, int TemplateId, int SheetId);

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

        fit.IntakeId = intake.Id;
        limited.IntakeId = intake.Id;

        var package = new GenerationPackage { Name = "Пакет" };
        package.Templates.Add(new GenerationPackageTemplate
        {
            TemplateId = template.Id, SortOrder = 0,
            RequirementRegular = TemplateRequirement.Required, RequirementLimited = TemplateRequirement.Optional
        });
        package.ExportTemplates.Add(new GenerationPackageExportTemplate
        {
            ExportTemplateId = sheet.Id, SortOrder = 0, FitnessFilter = FitnessFilter.All
        });
        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();

        return new Seeded(intake.Id, package.Id, fit.Id, limited.Id, template.Id, sheet.Id);
    }

    private static void AddSheet(TestDb db, Seeded s, params int[] participants)
    {
        using var ctx = db.Factory.CreateDbContext();
        var doc = new GeneratedGroupDocument
        {
            ExportTemplateId = s.SheetId, IntakeId = s.IntakeId, GeneratedAt = DateTime.Now, GeneratedByUserId = 1,
            FileName = "r.xlsx", Version = 1, IsCurrent = true, HasContent = true, RecipientCount = participants.Length
        };
        foreach (var id in participants) doc.Recipients.Add(new GeneratedGroupDocumentRecipient { RecipientId = id });
        ctx.GeneratedGroupDocuments.Add(doc);
        ctx.SaveChanges();
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

    private static Intake ActiveIntake(TestDb db, int intakeId)
    {
        using var ctx = db.Factory.CreateDbContext();
        return ctx.Intakes.Single(i => i.Id == intakeId);
    }

    [Fact]
    public async Task AMissingSheetIsCountedOnce_ByTheButtonTheBadgeAndTheCard()
    {
        using var db = new TestDb();
        var s = Seed(db);

        var vm = await CreateViewModelAsync(db);
        var service = TestServices.Completeness(db, 1, ActiveIntake(db, s.IntakeId));
        var badge = await service.GetBadgeBreakdownAsync();
        var summary = await service.GetIntakeSummaryAsync(s.IntakeId, s.PackageId);

        Assert.Equal(2, vm.MissingRequiredCount);
        Assert.Equal(1, vm.MissingOptionalCount);
        Assert.Equal(2, badge.Missing);
        Assert.Equal(2, summary.IncompletePeople);
        Assert.Equal(3, summary.RequiredCells);
        Assert.Equal(0, summary.SatisfiedCells);
    }

    [Fact]
    public async Task ASheetThatCoversEveryoneLeavesOnlyThePersonalGaps()
    {
        using var db = new TestDb();
        var s = Seed(db);
        AddSheet(db, s, s.FitId, s.LimitedId);

        var vm = await CreateViewModelAsync(db);
        var service = TestServices.Completeness(db, 1, ActiveIntake(db, s.IntakeId));
        var badge = await service.GetBadgeBreakdownAsync();
        var summary = await service.GetIntakeSummaryAsync(s.IntakeId, s.PackageId);

        Assert.Equal(1, vm.MissingRequiredCount);
        Assert.Equal(1, badge.Missing);
        Assert.Equal(1, summary.IncompletePeople);
        Assert.Equal(2, summary.SatisfiedCells);
    }

    [Fact]
    public async Task ASheetMissingOnePersonStillCountsAsOneGap()
    {
        using var db = new TestDb();
        var s = Seed(db);
        AddSheet(db, s, s.FitId);

        var vm = await CreateViewModelAsync(db);
        var service = TestServices.Completeness(db, 1, ActiveIntake(db, s.IntakeId));
        var badge = await service.GetBadgeBreakdownAsync();
        var summary = await service.GetIntakeSummaryAsync(s.IntakeId, s.PackageId);

        Assert.Equal(2, vm.MissingRequiredCount);
        Assert.Equal(2, badge.Missing);
        Assert.Equal(2, summary.IncompletePeople);
    }

    [Fact]
    public async Task GeneratingOneCellFromTheMatrixUpdatesTheButtonWithoutARebuild()
    {
        using var db = new TestDb();
        var s = Seed(db);
        AddSheet(db, s, s.FitId, s.LimitedId);

        var vm = await CreateViewModelAsync(db);
        Assert.Equal(1, vm.MissingRequiredCount);

        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.GeneratedDocuments.Add(new GeneratedDocument
            {
                RecipientId = s.FitId, TemplateId = s.TemplateId, IntakeId = s.IntakeId,
                FileName = "a.docx", GeneratedAt = DateTime.Now, GeneratedByUserId = 1,
                Version = 1, IsCurrent = true, HasContent = true
            });
            ctx.SaveChanges();
        }

        var cell = vm.Rows.Single(r => r.RecipientId == s.FitId).Cells.Single(c => c.TemplateId == s.TemplateId && !c.IsGroupColumn);
        await vm.RefreshCellAsync(cell);

        Assert.Equal(0, vm.MissingRequiredCount);
        Assert.True(vm.IsFullyComplete);
    }
}
