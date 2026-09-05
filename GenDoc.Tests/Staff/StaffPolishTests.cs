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
        var vm = new StaffViewModel(TestServices.Staff(db, 1), new NoDialogs(), new ServiceCollection().BuildServiceProvider());
        await vm.InitializeCommand.ExecuteAsync(null);

        vm.Rows.Single(r => r.Id == onSiteId).IsChecked = true;
        Assert.Equal(1, vm.SelectedCount);

        vm.SelectedState = vm.StateOptions.Single(o => o.Filter == StaffStateFilter.BusinessTrip);
        Assert.DoesNotContain(vm.Rows, r => r.Id == onSiteId);
        Assert.Equal(1, vm.SelectedCount);
        Assert.Equal("Оформити відрядження для обраних (1)", vm.IssueTripLabel);

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
