using System.Windows;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Staff;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.Tests.Staff;

public class StaffPolishTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-staff-{Guid.NewGuid():N}");

    public StaffPolishTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private sealed class NoDialogs : IDialogService
    {
        public bool? ShowDialog<TViewModel>(TViewModel viewModel, Window? owner = null) where TViewModel : notnull => null;
    }

    private static (int OnSite, int Away) SeedStaff(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
        ctx.AppSettings.Add(new AppSettings());
        var onSite = TemplateFixtures.Person(0, "ПЕТРЕНКО", "Іван");
        var away = TemplateFixtures.Person(0, "СИДОРЕНКО", "Петро");
        ctx.Recipients.AddRange(onSite, away);
        ctx.SaveChanges();
        var today = DateOnly.FromDateTime(DateTime.Today);
        ctx.StaffEvents.Add(new StaffEvent
        {
            RecipientId = away.Id, Kind = StaffEventKind.BusinessTrip, DateStart = today, DateEnd = today.AddDays(3),
            CreatedAt = DateTime.Now
        });
        ctx.SaveChanges();
        return (onSite.Id, away.Id);
    }

    [Fact]
    public async Task Selection_SurvivesFilterChanges()
    {
        using var db = new TestDb();
        var (onSiteId, _) = SeedStaff(db);
        var vm = StaffVm(db);
        await vm.InitializeCommand.ExecuteAsync(null);

        vm.Rows.Single(r => r.Id == onSiteId).IsChecked = true;
        Assert.Equal(1, vm.SelectedCount);

        vm.SelectedState = vm.StateOptions.Single(o => o.Filter == StaffStateFilter.BusinessTrip);
        Assert.DoesNotContain(vm.Rows, r => r.Id == onSiteId);
        Assert.Equal(1, vm.SelectedCount);
        Assert.Equal("Видалити обраних (1)", vm.DeleteSelectedLabel);

        vm.SelectedState = vm.StateOptions[0];
        Assert.True(vm.Rows.Single(r => r.Id == onSiteId).IsChecked);
        Assert.Equal(1, vm.SelectedCount);

        vm.ClearSelectionCommand.Execute(null);
        Assert.Equal(0, vm.SelectedCount);
        Assert.DoesNotContain(vm.Rows, r => r.IsChecked);
    }

    private static int AddTemplate(TestDb db, byte[] content)
    {
        using var ctx = db.Factory.CreateDbContext();
        var template = new Template
        {
            Name = "Посвідчення", OriginalFileName = "p.docx", Content = content,
            UploadedAt = DateTime.Now, Kind = TemplateKind.PerRecipient, Audience = TemplateAudience.PermanentStaff
        };
        ctx.Templates.Add(template);
        ctx.SaveChanges();
        return template.Id;
    }

    [Fact]
    public async Task IssueDocuments_DoesNotRecordTheEvent_WhenNothingWasGenerated()
    {
        using var db = new TestDb();
        var (onSiteId, _) = SeedStaff(db);
        var templateId = AddTemplate(db, new byte[] { 1 });
        var today = DateOnly.FromDateTime(DateTime.Today);

        var generated = await TestServices.Staff(db, 1).IssueDocumentsAsync(
            StaffEventKind.Leave, new[] { onSiteId }, new[] { templateId }, today, today.AddDays(5), null,
            new Dictionary<string, string>());

        Assert.Equal(0, generated);
        using var ctx = db.Factory.CreateDbContext();
        Assert.DoesNotContain(ctx.StaffEvents, e => e.RecipientId == onSiteId);
    }

    [Fact]
    public async Task IssueDocuments_RecordsTheEvent_OnceADocumentExists()
    {
        using var db = new TestDb();
        var (onSiteId, _) = SeedStaff(db);
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.AppSettings.First().DefaultOutputFolder = _folder;
            ctx.SaveChanges();
        }
        var templateId = AddTemplate(db, TemplateFixtures.Bytes(TemplateFixtures.RaportIndividualDocx));
        var today = DateOnly.FromDateTime(DateTime.Today);

        var generated = await TestServices.Staff(db, 1).IssueDocumentsAsync(
            StaffEventKind.Leave, new[] { onSiteId }, new[] { templateId }, today, today.AddDays(5), "відпустка",
            new Dictionary<string, string>());

        Assert.Equal(1, generated);
        using var ctx2 = db.Factory.CreateDbContext();
        var evt = Assert.Single(ctx2.StaffEvents.Where(e => e.RecipientId == onSiteId));
        Assert.Equal(StaffEventKind.Leave, evt.Kind);
    }

    private static int AddTemplate(TestDb db, string name, TemplateAudience audience,
        TemplateKind kind = TemplateKind.PerRecipient, StaffEventKind? staffEvent = null)
    {
        using var ctx = db.Factory.CreateDbContext();
        var template = new Template
        {
            Name = name, OriginalFileName = $"{name}.docx", Content = new byte[] { 1 },
            UploadedAt = DateTime.Now, Kind = kind, Audience = audience, StaffEvent = staffEvent
        };
        ctx.Templates.Add(template);
        ctx.SaveChanges();
        return template.Id;
    }

    private static StaffViewModel StaffVm(TestDb db)
        => new(TestServices.Staff(db, 1), new NoDialogs(), new ServiceCollection().BuildServiceProvider(),
               TestServices.Generation(db));

    [Fact]
    public async Task QuickPanel_ShowsPermanentStaffTemplatesAndSkipsIntakeOnes()
    {
        using var db = new TestDb();
        SeedStaff(db);
        AddTemplate(db, "Рапорт про приступлення", TemplateAudience.PermanentStaff);
        AddTemplate(db, "Рапорт на відпустку для лікування", TemplateAudience.Everyone);
        AddTemplate(db, "Рапорт про зарахування на котлове", TemplateAudience.Intake);
        AddTemplate(db, "Груповий рапорт", TemplateAudience.PermanentStaff, TemplateKind.Group);

        var vm = StaffVm(db);
        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.Equal(
            new[] { "Рапорт на відпустку для лікування", "Рапорт про приступлення" },
            vm.QuickTemplates.Select(t => t.Name).ToArray());
        Assert.Equal(
            new[] { "для всіх", "Постійний склад" },
            vm.QuickTemplates.Select(t => t.AudienceCaption).ToArray());
        Assert.True(vm.HasQuickTemplates);
    }

    [Fact]
    public async Task QuickPanel_WithoutTemplates_ExplainsWhy()
    {
        using var db = new TestDb();
        SeedStaff(db);

        var vm = StaffVm(db);
        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.Empty(vm.QuickTemplates);
        Assert.False(vm.HasQuickTemplates);
        Assert.Contains("Постійний склад", StaffDocDialogViewModel.NoTemplatesHint);
    }

    [Fact]
    public async Task QuickPanel_DeleteButtonCountsSelectedPeople()
    {
        using var db = new TestDb();
        var (onSiteId, _) = SeedStaff(db);

        var vm = StaffVm(db);
        await vm.InitializeCommand.ExecuteAsync(null);
        Assert.Equal("Видалити обраних (0)", vm.DeleteSelectedLabel);

        vm.Rows.Single(r => r.Id == onSiteId).IsChecked = true;
        Assert.Equal("Видалити обраних (1)", vm.DeleteSelectedLabel);
    }

    [Fact]
    public async Task QuickPanel_ShowsOnlyTheEventsThatHaveTemplates()
    {
        using var db = new TestDb();
        SeedStaff(db);
        AddTemplate(db, "Рапорт на відпустку", TemplateAudience.PermanentStaff,
            staffEvent: StaffEventKind.Leave);
        AddTemplate(db, "Довідка про хворобу", TemplateAudience.Everyone,
            staffEvent: StaffEventKind.Sick);
        AddTemplate(db, "Рапорт набору", TemplateAudience.Intake,
            staffEvent: StaffEventKind.BusinessTrip);

        var vm = StaffVm(db);
        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.False(vm.HasTripTemplates);
        Assert.True(vm.HasLeaveTemplates);
        Assert.True(vm.HasSickTemplates);
    }

    [Fact]
    public async Task QuickPanel_ListsOnlyTemplatesWithoutAnEvent()
    {
        using var db = new TestDb();
        SeedStaff(db);
        AddTemplate(db, "Звернення", TemplateAudience.PermanentStaff);
        AddTemplate(db, "Рапорт на відпустку", TemplateAudience.PermanentStaff,
            staffEvent: StaffEventKind.Leave);

        var vm = StaffVm(db);
        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "Звернення" }, vm.QuickTemplates.Select(t => t.Name).ToArray());
    }

    [Fact]
    public void DocDialog_ForAnEvent_TakesItsTemplatesAlreadyChecked()
    {
        using var db = new TestDb();
        SeedStaff(db);
        AddTemplate(db, "Рапорт на відпустку", TemplateAudience.PermanentStaff,
            staffEvent: StaffEventKind.Leave);
        AddTemplate(db, "Довідка про використані дні", TemplateAudience.PermanentStaff,
            staffEvent: StaffEventKind.Leave);
        AddTemplate(db, "Рапорт про хворобу", TemplateAudience.PermanentStaff,
            staffEvent: StaffEventKind.Sick);
        AddTemplate(db, "Звернення без події", TemplateAudience.PermanentStaff);

        var vm = new StaffDocDialogViewModel(
            TestServices.Generation(db), TestServices.Archive(db), null!, TestServices.Staff(db, 1));

        vm.Initialize(StaffEventKind.Leave, new List<(int Id, string FullName)> { (1, "ПЕТРЕНКО І.П.") });

        Assert.Equal(
            new[] { "Довідка про використані дні", "Рапорт на відпустку" },
            vm.Templates.Select(t => t.Name).ToArray());
        Assert.All(vm.Templates, t => Assert.True(t.IsChecked));
    }

    [Fact]
    public void DocDialog_TakesTheDatesAsTypedText()
    {
        using var db = new TestDb();
        SeedStaff(db);
        AddTemplate(db, "Рапорт на відпустку", TemplateAudience.PermanentStaff,
            staffEvent: StaffEventKind.Leave);

        var vm = new StaffDocDialogViewModel(
            TestServices.Generation(db), TestServices.Archive(db), null!, TestServices.Staff(db, 1));
        vm.Initialize(StaffEventKind.Leave, new List<(int Id, string FullName)> { (1, "ПЕТРЕНКО І.П.") });

        Assert.Equal(DateTime.Today.ToString("dd.MM.yyyy"), vm.DateStartText);

        vm.DateStartText = "01.08.2026";
        vm.DateEndText = "27.08.2026";
        Assert.Equal(new DateTime(2026, 8, 1), vm.DateStart);
        Assert.Equal(new DateTime(2026, 8, 27), vm.DateEnd);
        Assert.True(vm.ConfirmCommand.CanExecute(null));

        vm.DateEndText = "коли вийде";
        Assert.Null(vm.DateEnd);
        Assert.False(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void DocDialog_WithoutEventTemplates_SaysWhichEventToMark()
    {
        using var db = new TestDb();
        SeedStaff(db);

        var vm = new StaffDocDialogViewModel(
            TestServices.Generation(db), TestServices.Archive(db), null!, TestServices.Staff(db, 1));

        vm.Initialize(StaffEventKind.Sick, new List<(int Id, string FullName)> { (1, "ПЕТРЕНКО І.П.") });

        Assert.False(vm.HasTemplates);
        Assert.Contains("Хвороба", vm.NoTemplatesText);
    }

    [Fact]
    public void DocDialog_PreselectsTheTemplateClickedInTheQuickPanel()
    {
        using var db = new TestDb();
        SeedStaff(db);
        var chosen = AddTemplate(db, "Рапорт про приступлення", TemplateAudience.PermanentStaff);
        AddTemplate(db, "Звернення на отримання допомоги", TemplateAudience.PermanentStaff);

        var vm = new StaffDocDialogViewModel(
            TestServices.Generation(db), TestServices.Archive(db), null!, TestServices.Staff(db, 1));

        vm.Initialize(null, new List<(int Id, string FullName)> { (1, "ПЕТРЕНКО І.П.") }, chosen);

        Assert.Equal(2, vm.Templates.Count);
        Assert.Equal(new[] { chosen }, vm.Templates.Where(t => t.IsChecked).Select(t => t.Id).ToArray());
    }

    [Fact]
    public void DocDialog_WithoutTemplates_ExplainsWhy()
    {
        using var db = new TestDb();
        SeedStaff(db);
        var vm = new StaffDocDialogViewModel(
            TestServices.Generation(db), TestServices.Archive(db), null!, TestServices.Staff(db, 1));

        vm.Initialize(StaffEventKind.BusinessTrip, new List<(int Id, string FullName)> { (1, "ПЕТРЕНКО І.П.") });

        Assert.False(vm.HasTemplates);
        Assert.Contains("Постійний склад", StaffDocDialogViewModel.NoTemplatesHint);
    }
}
