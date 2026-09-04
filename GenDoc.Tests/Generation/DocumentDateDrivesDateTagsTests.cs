using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Services.Recipients;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Archive;
using GenDoc.ViewModels.Generation;

namespace GenDoc.Tests.Generation;

public class DocumentDateDrivesDateTagsTests
{
    private const int UserId = 1;

    private static ManualTagFormBuilder Build(TestDb db)
    {
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Users.Add(new UserProfile { FullName = "Курсовий", PasswordHash = "x", CreatedAt = DateTime.Now });
            ctx.SaveChanges();
        }
        return TestServices.ManualTagForm(
            db, TestServices.Staff(db, UserId), new FakeCurrentUser(), new FakeIntakeAccessor(), UserId);
    }

    [Theory]
    [InlineData("{{дата}}")]
    [InlineData("{{дата_рапорту}}")]
    [InlineData("{{дата_аркуша}}")]
    public void Classify_DocumentDateTags_AreDatesDrivenByTheDocumentDate(string tag)
    {
        Assert.Equal(ManualTagKind.Date, ManualTagClassifier.Classify(tag));
        Assert.True(ManualTagClassifier.IsDocumentDate(tag));
        Assert.Equal("06.08.2026", ManualTagClassifier.FormatDate(tag, new DateOnly(2026, 8, 6)));
    }

    [Theory]
    [InlineData("{{дата_прибуття}}")]
    [InlineData("{{дата_зарахування}}")]
    [InlineData("{{калібр}}")]
    public void Classify_IntakeDatesAndText_AreNotDocumentDate(string tag)
    {
        Assert.False(ManualTagClassifier.IsDocumentDate(tag));
    }

    [Fact]
    public void DocumentDateRow_WritesEveryDrivenTagInDayMonthYear()
    {
        var row = ManualTagRowViewModel.DocumentDate(new[] { "{{дата_рапорту}}", "{{дата_аркуша}}" }, new DateOnly(2026, 8, 6));
        var form = new ManualTagFormViewModel(new ObservableCollection<ManualTagRowViewModel> { row }, signer: null);

        Assert.True(row.IsDocumentDate);
        Assert.Equal("Дата документа", row.Label);

        var values = form.GetValues();

        Assert.Equal("06.08.2026", values["{{дата_рапорту}}"]);
        Assert.Equal("06.08.2026", values["{{дата_аркуша}}"]);
        Assert.Equal("06.08.2026", values["{{дата}}"]);
    }

    [Fact]
    public void SetDocumentDate_UpdatesTheDrivenTags()
    {
        var row = ManualTagRowViewModel.DocumentDate(new[] { "{{дата_рапорту}}" }, new DateOnly(2026, 8, 6));
        var form = new ManualTagFormViewModel(new ObservableCollection<ManualTagRowViewModel> { row }, signer: null);

        form.SetDocumentDate(new DateTime(2026, 9, 1));

        Assert.Equal("01.09.2026", form.GetValues()["{{дата_рапорту}}"]);
        Assert.Equal(new DateTime(2026, 9, 1), row.DateValue);
    }

    [Fact]
    public async Task BuildAsync_MergesDocumentDateTagsIntoOneRow_KeepsIntakeDatesSeparate()
    {
        using var db = new TestDb();

        var form = await Build(db).BuildAsync(
            new[] { "{{калібр}}", "{{дата_рапорту}}", "{{дата_аркуша}}", "{{дата_прибуття}}" }, "pkg:1");

        var documentDate = Assert.Single(form.Rows, r => r.IsDocumentDate);
        Assert.Same(documentDate, form.Rows[0]);
        Assert.Equal(ManualTagKind.Date, documentDate.Kind);
        Assert.Equal(DateTime.Today, documentDate.DateValue);
        Assert.Contains(form.Rows, r => r.Tag == "{{дата_прибуття}}" && !r.IsDocumentDate);
        Assert.Contains(form.Rows, r => r.Tag == "{{калібр}}");
        Assert.DoesNotContain(form.Rows, r => r.Tag == "{{дата_аркуша}}");
        Assert.Equal(3, form.Rows.Count);

        var values = form.GetValues();
        Assert.Equal(DateTime.Today.ToString("dd.MM.yyyy"), values["{{дата_рапорту}}"]);
        Assert.Equal(DateTime.Today.ToString("dd.MM.yyyy"), values["{{дата_аркуша}}"]);
    }

    [Fact]
    public async Task BuildAsync_WithoutDocumentDateTags_HasNoDocumentDateRow()
    {
        using var db = new TestDb();

        var form = await Build(db).BuildAsync(new[] { "{{калібр}}" }, "pkg:1");

        Assert.DoesNotContain(form.Rows, r => r.IsDocumentDate);
        Assert.DoesNotContain("{{дата}}", form.GetValues().Keys);
    }

    [Fact]
    public void ManualValuesDialog_ReusesTheDocumentDateRow_AsItsOnlyDateSource()
    {
        var row = ManualTagRowViewModel.DocumentDate(new[] { "{{дата_рапорту}}", "{{дата_аркуша}}" }, new DateOnly(2026, 8, 6));
        var dialog = new ManualValuesDialogViewModel(
            new ManualTagFormViewModel(new ObservableCollection<ManualTagRowViewModel> { row }, signer: null));

        row.DateValue = new DateTime(2026, 9, 1);
        var values = dialog.GetValues();

        Assert.Equal("01.09.2026", values["{{дата_рапорту}}"]);
        Assert.Equal("01.09.2026", values["{{дата_аркуша}}"]);
        Assert.Equal("01.09.2026", values["{{дата}}"]);
    }

    [Fact]
    public async Task GenerationScreen_DocumentDatePicker_DrivesTheFormRow()
    {
        using var db = new TestDb();
        Build(db);
        var vm = await CreateViewModelAsync(db);
        vm.DocumentDate = new DateTime(2026, 9, 1);

        var row = ManualTagRowViewModel.DocumentDate(new[] { "{{дата_рапорту}}" }, new DateOnly(2026, 8, 6));
        vm.ManualTagForm = new ManualTagFormViewModel(new ObservableCollection<ManualTagRowViewModel> { row }, signer: null);

        Assert.Equal(new DateTime(2026, 9, 1), row.DateValue);

        vm.DocumentDate = new DateTime(2026, 9, 2);

        Assert.Equal("02.09.2026", vm.ManualTagForm.GetValues()["{{дата_рапорту}}"]);

        vm.DocumentDate = null;

        Assert.Equal(DateTime.Today, row.DateValue);
    }

    private static async Task<GenerationViewModel> CreateViewModelAsync(TestDb db)
    {
        var vm = new GenerationViewModel(
            TestServices.Generation(db),
            new NoDialogs(),
            null!,
            TestServices.Completeness(db, UserId),
            new RecipientService(db.Factory, new FakeAuditLog(), new FakeCurrentUser()),
            new NoManualTags(),
            new OutputFolderService(db.Factory),
            TestServices.UserSettings(db, UserId),
            new FakeIntakeAccessor(),
            new WeakReferenceMessenger());
        await vm.InitialLoad;
        return vm;
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
