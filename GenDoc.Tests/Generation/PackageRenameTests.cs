using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Completeness;
using GenDoc.Services.Generation;
using GenDoc.Services.Recipients;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Generation;
using TemplateKind = GenDoc.Models.Enums.TemplateKind;

namespace GenDoc.Tests.Generation;

public class PackageRenameTests
{
    private const string FolderDescription = "Зібрано з теки «D:\\шаблони\\Котлове забезпечення»";

    private sealed record Seeded(int PackageId, int OtherPackageId, int LinkedTemplateId, int FreeTemplateId, int ExportLinkId, int FreeExportId);

    private static Seeded Seed(TestDb db, string? description = FolderDescription)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now });

        var linked = new Template
        {
            Name = "Рапорт груповий", OriginalFileName = "a.docx", Content = new byte[] { 1 },
            UploadedAt = DateTime.Now, Kind = TemplateKind.Group
        };
        var free = new Template
        {
            Name = "Довідка групова", OriginalFileName = "b.docx", Content = new byte[] { 1 },
            UploadedAt = DateTime.Now, Kind = TemplateKind.Group
        };
        var perRecipient = new Template
        {
            Name = "Рапорт на кожного", OriginalFileName = "c.docx", Content = new byte[] { 1 },
            UploadedAt = DateTime.Now, Kind = TemplateKind.PerRecipient
        };
        var linkedExport = new ExportTemplate { Name = "Відомість", OriginalFileName = "v.xlsx", UploadedAt = DateTime.Now, UsesPlaceholders = true };
        var freeExport = new ExportTemplate { Name = "Залік", OriginalFileName = "z.xlsx", UploadedAt = DateTime.Now, UsesPlaceholders = true };
        ctx.Templates.AddRange(linked, free, perRecipient);
        ctx.ExportTemplates.AddRange(linkedExport, freeExport);
        ctx.SaveChanges();

        var package = new GenerationPackage { Name = "Котлове забезпечення", Description = description };
        package.Templates.Add(new GenerationPackageTemplate { TemplateId = linked.Id, SortOrder = 0 });
        var exportLink = new GenerationPackageExportTemplate { ExportTemplateId = linkedExport.Id, SortOrder = 0 };
        package.ExportTemplates.Add(exportLink);
        var other = new GenerationPackage { Name = "Стройовий облік" };
        ctx.GenerationPackages.AddRange(package, other);
        ctx.SaveChanges();

        return new Seeded(package.Id, other.Id, linked.Id, free.Id, exportLink.Id, freeExport.Id);
    }

    private static GenerationPackage Package(TestDb db, int id)
    {
        using var ctx = db.Factory.CreateDbContext();
        return ctx.GenerationPackages.Single(p => p.Id == id);
    }

    [Fact]
    public void Rename_ChangesTheName_AndDetachesThePackageFromItsFolder()
    {
        using var db = new TestDb();
        var s = Seed(db);

        var error = TestServices.Generation(db).RenamePackage(s.PackageId, "  Котлове (весна)  ");

        Assert.Null(error);
        var package = Package(db, s.PackageId);
        Assert.Equal("Котлове (весна)", package.Name);
        Assert.Null(package.Description);
    }

    [Fact]
    public void Rename_KeepsADescriptionThatIsNotAFolderLink()
    {
        using var db = new TestDb();
        var s = Seed(db, description: "Для весняного набору");

        TestServices.Generation(db).RenamePackage(s.PackageId, "Котлове (весна)");

        Assert.Equal("Для весняного набору", Package(db, s.PackageId).Description);
    }

    [Fact]
    public void Rename_ToTheSameName_KeepsTheFolderLink()
    {
        using var db = new TestDb();
        var s = Seed(db);

        var error = TestServices.Generation(db).RenamePackage(s.PackageId, " Котлове забезпечення ");

        Assert.Null(error);
        Assert.Equal(FolderDescription, Package(db, s.PackageId).Description);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_ToAnEmptyName_IsRefused(string name)
    {
        using var db = new TestDb();
        var s = Seed(db);

        var error = TestServices.Generation(db).RenamePackage(s.PackageId, name);

        Assert.NotNull(error);
        Assert.Equal("Котлове забезпечення", Package(db, s.PackageId).Name);
    }

    [Fact]
    public void Rename_ToANameOfAnotherPackage_IsRefused()
    {
        using var db = new TestDb();
        var s = Seed(db);

        var error = TestServices.Generation(db).RenamePackage(s.PackageId, "СТРОЙОВИЙ ОБЛІК");

        Assert.NotNull(error);
        Assert.Contains("Стройовий облік", error, StringComparison.Ordinal);
        Assert.Equal(FolderDescription, Package(db, s.PackageId).Description);
    }

    [Fact]
    public void AddingAnExportTemplate_DetachesThePackageFromItsFolder()
    {
        using var db = new TestDb();
        var s = Seed(db);

        TestServices.Generation(db).SaveExportTemplates(s.PackageId, new()
        {
            (s.ExportLinkId, 0, 0, FitnessFilter.All),
            (null, s.FreeExportId, 1, FitnessFilter.All)
        });

        Assert.Null(Package(db, s.PackageId).Description);
    }

    [Fact]
    public void ChangingOnlyTheFitnessFilterOfAnExport_KeepsTheFolderLink()
    {
        using var db = new TestDb();
        var s = Seed(db);

        TestServices.Generation(db).SaveExportTemplates(s.PackageId, new()
        {
            (s.ExportLinkId, 0, 0, FitnessFilter.RegularOnly)
        });

        Assert.Equal(FolderDescription, Package(db, s.PackageId).Description);
    }

    [Fact]
    public async Task AddingATemplateInRequirements_DetachesThePackageFromItsFolder()
    {
        using var db = new TestDb();
        var s = Seed(db);
        int linkId;
        using (var ctx = db.Factory.CreateDbContext())
            linkId = ctx.GenerationPackageTemplates.Single(t => t.GenerationPackageId == s.PackageId).Id;

        await TestServices.Completeness(db, 1).SaveRequirementsAsync(s.PackageId, new[]
        {
            new RequirementRow(linkId, s.LinkedTemplateId, TemplateRequirement.Required, TemplateRequirement.Required, 0),
            new RequirementRow(null, s.FreeTemplateId, TemplateRequirement.Required, TemplateRequirement.Optional, 1)
        });

        Assert.Null(Package(db, s.PackageId).Description);
    }

    [Fact]
    public async Task ChangingOnlyRequirements_KeepsTheFolderLink()
    {
        using var db = new TestDb();
        var s = Seed(db);
        int linkId;
        using (var ctx = db.Factory.CreateDbContext())
            linkId = ctx.GenerationPackageTemplates.Single(t => t.GenerationPackageId == s.PackageId).Id;

        await TestServices.Completeness(db, 1).SaveRequirementsAsync(s.PackageId, new[]
        {
            new RequirementRow(linkId, s.LinkedTemplateId, TemplateRequirement.Optional, TemplateRequirement.NotApplicable, 0)
        });

        Assert.Equal(FolderDescription, Package(db, s.PackageId).Description);
    }

    [Fact]
    public void FolderImportDescription_IsRecognisedAsAFolderLink()
    {
        Assert.True(PackageFolderOrigin.IsFromFolder(PackageFolderOrigin.Describe(@"D:\шаблони\Стрільби")));
        Assert.False(PackageFolderOrigin.IsFromFolder("Для весняного набору"));
        Assert.False(PackageFolderOrigin.IsFromFolder(null));
    }

    private static List<(int TemplateId, int SortOrder, TemplateRequirement Regular, TemplateRequirement Limited)> Links(TestDb db, int packageId)
    {
        using var ctx = db.Factory.CreateDbContext();
        return ctx.GenerationPackageTemplates.Where(t => t.GenerationPackageId == packageId)
            .OrderBy(t => t.SortOrder)
            .Select(t => new { t.TemplateId, t.SortOrder, t.RequirementRegular, t.RequirementLimited })
            .AsEnumerable()
            .Select(t => (t.TemplateId, t.SortOrder, t.RequirementRegular, t.RequirementLimited))
            .ToList();
    }

    [Fact]
    public void AddingTemplates_AppendsOnlyTheNewOnesAfterTheExisting_AndDetachesTheFolder()
    {
        using var db = new TestDb();
        var s = Seed(db);

        var added = TestServices.Generation(db).AddTemplatesToPackage(
            s.PackageId, new[] { s.FreeTemplateId, s.LinkedTemplateId }, new[] { s.FreeExportId });

        Assert.Equal(2, added);
        Assert.Equal(new[] { (s.LinkedTemplateId, 0), (s.FreeTemplateId, 1) },
            Links(db, s.PackageId).Select(l => (l.TemplateId, l.SortOrder)));
        using (var ctx = db.Factory.CreateDbContext())
        {
            var exports = ctx.GenerationPackageExportTemplates.Where(t => t.GenerationPackageId == s.PackageId)
                .OrderBy(t => t.SortOrder).Select(t => new { t.ExportTemplateId, t.SortOrder }).ToList();
            Assert.Equal(s.FreeExportId, exports.Last().ExportTemplateId);
            Assert.Equal(1, exports.Last().SortOrder);
        }
        Assert.Null(Package(db, s.PackageId).Description);
    }

    [Fact]
    public void AddingOnlyTemplatesAlreadyInThePackage_ChangesNothing_AndKeepsTheFolderLink()
    {
        using var db = new TestDb();
        var s = Seed(db);

        var added = TestServices.Generation(db).AddTemplatesToPackage(
            s.PackageId, new[] { s.LinkedTemplateId }, Array.Empty<int>());

        Assert.Equal(0, added);
        Assert.Single(Links(db, s.PackageId));
        Assert.Equal(FolderDescription, Package(db, s.PackageId).Description);
    }

    [Fact]
    public void AddedTemplateNamedForOneFitnessCategory_IsNotRequiredFromTheOther()
    {
        using var db = new TestDb();
        var s = Seed(db);
        int limitedId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var limited = new Template
            {
                Name = "Рапорт про прибуття (обмежено придатні)", OriginalFileName = "l.docx", Content = new byte[] { 1 },
                UploadedAt = DateTime.Now, Kind = TemplateKind.Group
            };
            ctx.Templates.Add(limited);
            ctx.SaveChanges();
            limitedId = limited.Id;
        }

        TestServices.Generation(db).AddTemplatesToPackage(s.PackageId, new[] { limitedId }, Array.Empty<int>());

        var link = Links(db, s.PackageId).Single(l => l.TemplateId == limitedId);
        Assert.Equal(TemplateRequirement.NotApplicable, link.Regular);
        Assert.Equal(TemplateRequirement.Required, link.Limited);
    }

    private static async Task<GenerationViewModel> ViewModelAsync(TestDb db)
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
    public async Task RenamingFromTheCard_ShowsTheNewNameWithoutTheFolderLine_AndKeepsTheSelection()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var vm = await ViewModelAsync(db);
        await vm.SelectPackageCommand.ExecuteAsync(vm.Packages.Single(p => p.Id == s.PackageId));

        var card = vm.Packages.Single(p => p.Id == s.PackageId);
        vm.StartEditPackageCommand.Execute(card);
        Assert.True(card.IsEditing);
        Assert.Equal("Котлове забезпечення", card.EditName);

        card.EditName = "Котлове (весна)";
        await vm.ConfirmEditPackageCommand.ExecuteAsync(card);

        var renamed = vm.Packages.Single(p => p.Id == s.PackageId);
        Assert.Equal("Котлове (весна)", renamed.Name);
        Assert.Null(renamed.Description);
        Assert.False(renamed.IsEditing);
        Assert.Same(renamed, vm.SelectedPackage);
    }

    [Fact]
    public async Task CancellingARename_LeavesTheNameAsItWas()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var vm = await ViewModelAsync(db);

        var card = vm.Packages.Single(p => p.Id == s.PackageId);
        vm.StartEditPackageCommand.Execute(card);
        card.EditName = "Щось інше";
        vm.CancelEditPackageCommand.Execute(card);

        Assert.False(card.IsEditing);
        Assert.Equal("Котлове забезпечення", Package(db, s.PackageId).Name);
    }

    [Fact]
    public async Task EditingACard_OffersGroupTemplatesAndSheetsNotYetInThePackage()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var vm = await ViewModelAsync(db);

        var card = vm.Packages.Single(p => p.Id == s.PackageId);
        vm.StartEditPackageCommand.Execute(card);

        Assert.True(card.HasAddableTemplates);
        Assert.Equal(new[] { ("Довідка групова", "docx"), ("Залік", "xlsx") },
            card.AddableTemplates.Select(t => (t.Name, t.KindBadge)));
    }

    [Fact]
    public async Task SavingAnEditWithCheckedTemplates_AddsThemToTheSelectedPackage()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var vm = await ViewModelAsync(db);
        await vm.SelectPackageCommand.ExecuteAsync(vm.Packages.Single(p => p.Id == s.PackageId));

        var card = vm.Packages.Single(p => p.Id == s.PackageId);
        vm.StartEditPackageCommand.Execute(card);
        card.AddableTemplates.Single(t => t.Name == "Довідка групова").IsChecked = true;
        await vm.ConfirmEditPackageCommand.ExecuteAsync(card);

        var edited = vm.Packages.Single(p => p.Id == s.PackageId);
        Assert.False(edited.IsEditing);
        Assert.Equal(3, edited.TemplateCount);
        Assert.Equal("Котлове забезпечення", edited.Name);
        Assert.Same(edited, vm.SelectedPackage);
        Assert.Contains(vm.PackageTemplates, t => t.Name == "Довідка групова");
    }

    [Fact]
    public async Task CancellingAnEdit_DoesNotAddTheCheckedTemplates()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var vm = await ViewModelAsync(db);

        var card = vm.Packages.Single(p => p.Id == s.PackageId);
        vm.StartEditPackageCommand.Execute(card);
        card.AddableTemplates.Single(t => t.Name == "Довідка групова").IsChecked = true;
        vm.CancelEditPackageCommand.Execute(card);

        Assert.Single(Links(db, s.PackageId));
        Assert.Equal(FolderDescription, Package(db, s.PackageId).Description);
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
