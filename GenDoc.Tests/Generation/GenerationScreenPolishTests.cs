using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Models;
using GenDoc.Models.Enums;
using TemplateKind = GenDoc.Models.Enums.TemplateKind;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Services.Recipients;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Generation;

namespace GenDoc.Tests.Generation;

public class GenerationScreenPolishTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-polish-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        if (File.Exists(_folder)) File.Delete(_folder);
    }

    private static readonly IProgress<string> NoProgress = new Progress<string>(_ => { });

    private sealed class Seeded
    {
        public int PackageId;
        public int OtherPackageId;
    }

    private static Seeded Seed(TestDb db, TemplateKind kind = TemplateKind.PerRecipient, bool withGrades = false)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now });
        ctx.OrganizationSettings.Add(new OrganizationSettings { UnitNumber = "А1234", City = "Львів" });
        ctx.Recipients.AddRange(TemplateFixtures.Roster(3));

        var template = new Template
        {
            Name = "Рапорт", OriginalFileName = "rapport.docx",
            Content = TemplateFixtures.Bytes(TemplateFixtures.RaportIndividualDocx),
            UploadedAt = DateTime.Now, Kind = kind
        };
        template.FieldMappings.Add(new TemplateFieldMapping
        {
            PlaceholderTag = "{{номер}}", SourceType = MappingSourceType.Recipient, FieldName = "RowNumber",
            IsInsideRepeatingBlock = kind == TemplateKind.Group
        });
        template.FieldMappings.Add(new TemplateFieldMapping
        {
            PlaceholderTag = "{{підстава}}", SourceType = MappingSourceType.Manual
        });
        ctx.Templates.Add(template);

        var export = new ExportTemplate
        {
            Name = "Залік", OriginalFileName = "zalik.xlsx",
            Content = TemplateFixtures.Bytes(TemplateFixtures.RozdavalnaXlsx),
            UsesPlaceholders = true, TemplateRowIndex = 5, UploadedAt = DateTime.Now
        };
        export.ColumnMappings.Add(new ExportTemplateColumnMapping
        {
            ColumnIndex = 1, HeaderText = string.Empty, PlaceholderTag = "{{піб}}",
            FieldKey = nameof(ExportFieldKey.FullNameFormatted), SourceType = MappingSourceType.Recipient
        });
        if (withGrades)
        {
            export.ColumnMappings.Add(new ExportTemplateColumnMapping
            {
                ColumnIndex = 2, HeaderText = string.Empty, PlaceholderTag = "{{оцінка_1}}",
                FieldKey = nameof(ExportFieldKey.GradeRandom34), SourceType = MappingSourceType.Recipient
            });
        }
        ctx.ExportTemplates.Add(export);
        ctx.SaveChanges();

        var package = new GenerationPackage { Name = "Пакет" };
        package.Templates.Add(new GenerationPackageTemplate { TemplateId = template.Id, SortOrder = 0 });
        package.ExportTemplates.Add(new GenerationPackageExportTemplate
        {
            ExportTemplateId = export.Id, SortOrder = 0, FitnessFilter = FitnessFilter.All
        });
        var other = new GenerationPackage { Name = "Інший" };
        other.Templates.Add(new GenerationPackageTemplate { TemplateId = template.Id, SortOrder = 0 });
        ctx.GenerationPackages.AddRange(package, other);
        ctx.SaveChanges();
        return new Seeded { PackageId = package.Id, OtherPackageId = other.Id };
    }

    private sealed class NoDialogs : IDialogService
    {
        public bool? ShowDialog<TViewModel>(TViewModel viewModel, Window? owner = null) where TViewModel : notnull => null;
    }

    private sealed class TextRowForms : IManualTagFormBuilder
    {
        public int Builds { get; private set; }
        public ManualTagFormViewModel? LastSaved { get; private set; }

        public Task<ManualTagFormViewModel> BuildAsync(IReadOnlyList<string> tags, string contextKey, bool needsCourseOfficer = false, int? intakeId = null)
        {
            Builds++;
            var rows = new ObservableCollection<ManualTagRowViewModel>(tags.Select(t => new ManualTagRowViewModel(t, null)));
            return Task.FromResult(new ManualTagFormViewModel(rows, null));
        }

        public Task SaveAsync(string contextKey, ManualTagFormViewModel form)
        {
            LastSaved = form;
            return Task.CompletedTask;
        }
    }

    private static async Task<GenerationViewModel> CreateViewModelAsync(TestDb db, IManualTagFormBuilder forms)
    {
        var vm = new GenerationViewModel(
            TestServices.Generation(db),
            new NoDialogs(),
            null!,
            TestServices.Completeness(db, 1),
            new RecipientService(db.Factory, new FakeAuditLog(), new FakeCurrentUser()),
            forms,
            new OutputFolderService(db.Factory),
            TestServices.UserSettings(db, 1),
            new FakeIntakeAccessor(),
            new WeakReferenceMessenger());
        await vm.InitialLoad;
        return vm;
    }

    [Fact]
    public void GetManualTags_PerRecipientDocxWithRowNumber_OffersNomerAsManualField()
    {
        using var db = new TestDb();
        var seeded = Seed(db, TemplateKind.PerRecipient);

        var tags = TestServices.Generation(db).GetManualTags(seeded.PackageId);

        Assert.Contains("{{номер}}", tags);
        Assert.Contains("{{підстава}}", tags);
    }

    [Fact]
    public void GetManualTags_GroupDocxWithRowNumberInsideBlock_DoesNotOfferNomer()
    {
        using var db = new TestDb();
        var seeded = Seed(db, TemplateKind.Group);

        var tags = TestServices.Generation(db).GetManualTags(seeded.PackageId);

        Assert.DoesNotContain("{{номер}}", tags);
    }

    [Fact]
    public void PackageUsesAutoGrades_ReflectsGradeMappingsOfTheSheet()
    {
        using var db = new TestDb();
        var plain = Seed(db);
        Assert.False(TestServices.Generation(db).PackageUsesAutoGrades(plain.PackageId));

        using var graded = new TestDb();
        var withGrades = Seed(graded, withGrades: true);
        Assert.True(TestServices.Generation(graded).PackageUsesAutoGrades(withGrades.PackageId));
    }

    [Fact]
    public async Task SelectPackage_ShowsAutoGradesHintOnlyForPackagesWithGradeTags()
    {
        using var db = new TestDb();
        var seeded = Seed(db, withGrades: true);
        var vm = await CreateViewModelAsync(db, new TextRowForms());

        await vm.SelectPackageCommand.ExecuteAsync(vm.Packages.Single(p => p.Id == seeded.PackageId));
        Assert.True(vm.PackageUsesAutoGrades);

        await vm.SelectPackageCommand.ExecuteAsync(vm.Packages.Single(p => p.Id == seeded.OtherPackageId));
        Assert.False(vm.PackageUsesAutoGrades);
    }

    [Fact]
    public void AutoGradesHint_NamesTheRule()
        => Assert.Equal("Оцінки в цій відомості проставляються автоматично (3 або 4)", GenerationViewModel.AutoGradesHint);

    [Fact]
    public void RunPackage_OutputFolderIsAFile_ThrowsBeforeRecordingARun()
    {
        using var db = new TestDb();
        var seeded = Seed(db);
        File.WriteAllText(_folder, "x");

        Assert.ThrowsAny<IOException>(() => TestServices.Generation(db).RunPackage(
            seeded.PackageId, _folder, new Dictionary<string, string>(), false, RosterSelection.Everyone, NoProgress));

        using var ctx = db.Factory.CreateDbContext();
        Assert.Empty(ctx.GenerationPackageRuns.ToList());
    }

    [Fact]
    public void OutputFolderProblem_UnusableFolder_ExplainsAndPointsToChange()
    {
        File.WriteAllText(_folder, "x");

        var problem = GenerationViewModel.OutputFolderProblem(_folder);

        Assert.NotNull(problem);
        Assert.Contains(_folder, problem);
        Assert.Contains("Змінити…", problem);
    }

    [Fact]
    public void OutputFolderProblem_CreatableFolder_IsNull()
        => Assert.Null(GenerationViewModel.OutputFolderProblem(Path.Combine(_folder, "sub")));

    [Fact]
    public void OutputFolderChangeQuestion_WarnsThatTheSettingIsShared()
    {
        var question = GenerationViewModel.OutputFolderChangeQuestion(@"D:\Docs");

        Assert.Contains(@"D:\Docs", question);
        Assert.Contains("всіх користувачів", question);
    }

    [Fact]
    public async Task WhileBusy_LeftPanelCommandsAreDisabled()
    {
        using var db = new TestDb();
        var seeded = Seed(db);
        var vm = await CreateViewModelAsync(db, new TextRowForms());
        var package = vm.Packages.Single(p => p.Id == seeded.PackageId);
        await vm.SelectPackageCommand.ExecuteAsync(package);
        var other = vm.Packages.Single(p => p.Id == seeded.OtherPackageId);

        vm.IsBusy = true;

        Assert.False(vm.SelectPackageCommand.CanExecute(other));
        Assert.False(vm.DeletePackageCommand.CanExecute(other));
        Assert.False(vm.OpenRequirementsCommand.CanExecute(null));
        Assert.False(vm.PickOutputFolderCommand.CanExecute(null));
        Assert.False(vm.ShowCreatePackageCommand.CanExecute(null));

        vm.IsBusy = false;

        Assert.True(vm.SelectPackageCommand.CanExecute(other));
        Assert.True(vm.DeletePackageCommand.CanExecute(other));
        Assert.True(vm.OpenRequirementsCommand.CanExecute(null));
        Assert.True(vm.PickOutputFolderCommand.CanExecute(null));
        Assert.True(vm.ShowCreatePackageCommand.CanExecute(null));
    }

    [Fact]
    public async Task SelectPackage_SameAgain_KeepsFormInstanceAndRosterChoice()
    {
        using var db = new TestDb();
        var seeded = Seed(db);
        var forms = new TextRowForms();
        var vm = await CreateViewModelAsync(db, forms);
        var package = vm.Packages.Single(p => p.Id == seeded.PackageId);

        await vm.SelectPackageCommand.ExecuteAsync(package);
        var form = vm.ManualTagForm;
        Assert.NotNull(form);
        form.Rows.Single(r => r.Tag == "{{підстава}}").Value = "наказ 12";
        vm.UseAllRecipients = false;
        vm.RecipientOptions[0].IsChecked = true;
        var builds = forms.Builds;

        await vm.SelectPackageCommand.ExecuteAsync(package);

        Assert.Same(form, vm.ManualTagForm);
        Assert.Equal(builds, forms.Builds);
        Assert.False(vm.UseAllRecipients);
        Assert.True(vm.RecipientOptions[0].IsChecked);
        Assert.True(package.IsSelected);
    }

    [Fact]
    public async Task RefreshPackageContent_AfterRequirements_RebuildsTagsButKeepsEnteredValuesAndRoster()
    {
        using var db = new TestDb();
        var seeded = Seed(db);
        var forms = new TextRowForms();
        var vm = await CreateViewModelAsync(db, forms);
        var package = vm.Packages.Single(p => p.Id == seeded.PackageId);
        await vm.SelectPackageCommand.ExecuteAsync(package);
        vm.ManualTagForm!.Rows.Single(r => r.Tag == "{{підстава}}").Value = "наказ 12";
        vm.UseAllRecipients = false;
        vm.RecipientOptions[1].IsChecked = true;

        using (var ctx = db.Factory.CreateDbContext())
        {
            var template = ctx.Templates.Single();
            ctx.TemplateFieldMappings.Add(new TemplateFieldMapping
            {
                TemplateId = template.Id, PlaceholderTag = "{{причина}}", SourceType = MappingSourceType.Manual
            });
            ctx.SaveChanges();
        }

        await vm.RefreshPackageContentAsync();

        Assert.Contains(vm.ManualTagForm!.Rows, r => r.Tag == "{{причина}}");
        Assert.Equal("наказ 12", vm.ManualTagForm.Rows.Single(r => r.Tag == "{{підстава}}").Value);
        Assert.False(vm.UseAllRecipients);
        Assert.True(vm.RecipientOptions[1].IsChecked);
    }

    [Fact]
    public async Task GenerateAll_SavesTheFormThatWasOnScreenWhenTheRunStarted()
    {
        using var db = new TestDb();
        var seeded = Seed(db);
        var forms = new TextRowForms();
        var vm = await CreateViewModelAsync(db, forms);
        await vm.SelectPackageCommand.ExecuteAsync(vm.Packages.Single(p => p.Id == seeded.PackageId));
        vm.OutputFolder = _folder;
        var form = vm.ManualTagForm;
        var swapped = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.IsBusy) && vm.IsBusy && !swapped)
            {
                swapped = true;
                vm.ManualTagForm = null;
            }
        };

        await vm.GenerateAllCommand.ExecuteAsync(null);

        Assert.Same(form, forms.LastSaved);
    }

    [Fact]
    public async Task CourseOfficerPicker_ShowsTheSignatureLineThatGoesIntoTheSheet()
    {
        using var db = new TestDb();
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Users.Add(new UserProfile { FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now });
            var unit = new Unit { Name = "1 навчальна рота" };
            ctx.Units.Add(unit);
            ctx.SaveChanges();
            ctx.Recipients.Add(new Recipient
            {
                LastName = "Ковальчук", FirstName = "Василь", MiddleName = "Богданович", Rank = "капітан",
                IsCourseOfficer = true, IntakeId = null, UnitId = unit.Id
            });
            ctx.SaveChanges();
        }
        var builder = TestServices.ManualTagForm(
            db, TestServices.Staff(db, 1), new FakeCurrentUser(), new FakeIntakeAccessor(), 1);

        var form = await builder.BuildAsync(Array.Empty<string>(), "pkg:1", needsCourseOfficer: true);

        var picker = form.CourseOfficer;
        Assert.NotNull(picker);
        Assert.Equal("капітан Василь КОВАЛЬЧУК", picker.Selected!.DisplayLabel);
        Assert.Equal("Курсовий офіцер 1 навчальна рота капітан Ковальчук В. Б.", picker.SelectedSignaturePreview);
    }

    [Fact]
    public void SignerPicker_PreviewFollowsSelection()
    {
        var a = new StaffPickerOption(1, "капітан", "Василь КОВАЛЬЧУК", "капітан Василь КОВАЛЬЧУК", "Курсовий офіцер капітан Ковальчук В. Б.");
        var b = new StaffPickerOption(2, "майор", "Олег ТКАЧЕНКО", "майор Олег ТКАЧЕНКО", "Курсовий офіцер майор Ткаченко О.");
        var picker = new SignerPickerViewModel(new[] { a, b }, a);
        var changes = new List<string?>();
        picker.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        picker.Selected = b;

        Assert.Equal("Курсовий офіцер майор Ткаченко О.", picker.SelectedSignaturePreview);
        Assert.Contains(nameof(SignerPickerViewModel.SelectedSignaturePreview), changes);
        Assert.True(picker.HasSignaturePreview);
    }
}
