using System.Windows;
using GenDoc.Models;

using GenDoc.Services;
using GenDoc.Services.Documents;
using GenDoc.Services.Generation;
using GenDoc.Services.Intakes;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Archive;
using GenDoc.ViewModels.Generation;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.Tests.Archive;

public class ArchiveViewModelPolishTests
{
    private static RunGroupViewModel Run(int generated, int skipped, int errors)
        => new(new RunDto(1, DateTime.Now, "Зброя", 4, null, generated, skipped, errors));

    [Fact]
    public void RunHeader_ShowsGeneratedSkippedAndErrors_WithUkrainianPlurals()
    {
        Assert.Equal("згенеровано 9 · пропущено 2", Run(9, 2, 0).DocsText);
        Assert.Equal("згенеровано 9", Run(9, 0, 0).DocsText);
        Assert.Equal("без помилок", Run(9, 0, 0).ErrorsText);
        Assert.Equal("1 помилка", Run(9, 0, 1).ErrorsText);
        Assert.Equal("3 помилки", Run(9, 0, 3).ErrorsText);
        Assert.Equal("11 помилок", Run(9, 0, 11).ErrorsText);
    }

    [Fact]
    public void RunHeader_PackageInTrash_IsLabelled()
    {
        var run = new RunGroupViewModel(new RunDto(1, DateTime.Now, "Зброя", 4, null, 1, 0, 0, PackageInTrash: true));
        Assert.Equal("пакет «Зброя» (у кошику)", run.PackageText);
    }

    [Fact]
    public void RunItem_GroupDocument_OpensAsGroup()
    {
        var item = new RunItemRowViewModel(new RunItemDto("3 особи", "Залік", "згенеровано", false, 5, 7, true, "z.xlsx", IsGroup: true));
        Assert.True(item.CanOpen);
        Assert.True(item.IsGroup);
    }

    [Fact]
    public void ArchiveRow_Stale_ShowsChip()
    {
        var dto = new ArchiveRowDto(1, 1, 1, "ШЕВЧЕНКО", "Тарас", null, "Рапорт", true, 2, null, null, null,
            DateTime.Now, "Тест", 0, true, GenDoc.Models.Enums.DocumentSourceType.Generated, "r.docx", 3, IsStale: true);
        var row = new ArchiveRowViewModel(dto);
        Assert.True(row.IsStale);
        Assert.Equal("в.2 · застарів", row.VersionText);
    }

    [Fact]
    public void GroupRow_TemplateInTrash_HasHint()
    {
        var dto = new GroupDocumentRowDto(1, 1, null, "Залік", false, 1, 3, DateTime.Now, "Тест", true, "z.xlsx", 5);
        var row = new GroupDocumentRowViewModel(dto);
        Assert.Contains("шаблон у кошику", row.TemplateDisplay);
    }

    private sealed record Seeded(int IntakeId, int TemplateId, int ExportTemplateId, int RecipientId);

    private static Seeded Seed(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now });
        var intake = new Intake { Number = 1, DisplayNumber = "Набір №1", Status = GenDoc.Models.Enums.IntakeStatus.Active, StatusIsPinned = true };
        ctx.Intakes.Add(intake);
        var template = new Template
        {
            Name = "Рапорт", OriginalFileName = "r.docx", Content = new byte[] { 1 },
            UploadedAt = DateTime.Now, Kind = GenDoc.Models.Enums.TemplateKind.PerRecipient
        };
        ctx.Templates.Add(template);
        var export = new ExportTemplate { Name = "Залік", OriginalFileName = "z.xlsx", UploadedAt = DateTime.Now };
        ctx.ExportTemplates.Add(export);
        var person = TemplateFixtures.Person(0, "ШЕВЧЕНКО", "Тарас");
        ctx.Recipients.Add(person);
        ctx.SaveChanges();
        return new Seeded(intake.Id, template.Id, export.Id, person.Id);
    }

    private static void AddDocument(TestDb db, Seeded s, GenDoc.Models.Enums.DocumentSourceType sourceType)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.GeneratedDocuments.Add(new GeneratedDocument
        {
            RecipientId = s.RecipientId, TemplateId = s.TemplateId, IntakeId = s.IntakeId,
            FileName = "r.docx", GeneratedAt = DateTime.Now, GeneratedByUserId = 1, Version = 1,
            IsCurrent = true, HasContent = true, SizeBytes = 3, SourceType = sourceType,
            Content = new GeneratedDocumentContent { Content = new byte[] { 1, 2, 3 } }
        });
        ctx.SaveChanges();
    }

    private static void AddGroupDocuments(TestDb db, Seeded s, int count, int year)
    {
        using var ctx = db.Factory.CreateDbContext();
        for (var i = 1; i <= count; i++)
        {
            ctx.GeneratedGroupDocuments.Add(new GeneratedGroupDocument
            {
                ExportTemplateId = s.ExportTemplateId, IntakeId = s.IntakeId,
                GeneratedAt = new DateTime(year, 1, 1).AddMinutes(i), GeneratedByUserId = 1,
                FileName = $"z{i}.xlsx", SizeBytes = 1, RecipientCount = 1, Version = 1, IsCurrent = true, HasContent = false
            });
        }
        ctx.SaveChanges();
    }

    private static async Task<ArchiveViewModel> CreateAsync(TestDb db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db.Factory);
        services.AddSingleton<IAuditLogService>(new FakeAuditLog());
        services.AddSingleton<ICurrentUserContext>(new FakeCurrentUser());
        services.AddSingleton<IUserSettingsService>(TestServices.UserSettings(db, 1));
        services.AddSingleton<GenDoc.Services.Completeness.ICompletenessService>(_ => TestServices.Completeness(db, 1));
        services.AddSingleton<IIntakeService, IntakeService>();
        services.AddSingleton<ActiveIntakeState>();
        var provider = services.BuildServiceProvider();
        var state = provider.GetRequiredService<ActiveIntakeState>();
        await state.RefreshAsync();

        return new ArchiveViewModel(
            TestServices.Archive(db), new NoDialogs(), state, new NoManualTags(),
            new OutputFolderService(db.Factory), TestServices.UserSettings(db, 1), new FakeCurrentUser());
    }

    [Fact]
    public async Task Regenerate_IsAllowedWhenTheCurrentVersionWasUploadedManually()
    {
        using var db = new TestDb();
        var s = Seed(db);
        AddDocument(db, s, GenDoc.Models.Enums.DocumentSourceType.ManualUpload);
        var vm = await CreateAsync(db);
        await vm.InitializeAsync();

        vm.Rows.Single().IsChecked = true;

        Assert.True(vm.CanRegenerate);
    }

    [Fact]
    public async Task GroupTab_YearFilterApplies_AndPagesLoadMore()
    {
        using var db = new TestDb();
        var s = Seed(db);
        AddGroupDocuments(db, s, ArchiveViewModel.PageSize + 1, 2025);
        AddGroupDocuments(db, s, 2, 2024);
        var vm = await CreateAsync(db);
        await vm.InitializeAsync();
        await vm.SetTabCommand.ExecuteAsync("2");

        Assert.Equal(ArchiveViewModel.PageSize, vm.GroupRows.Count);
        Assert.True(vm.GroupCanLoadMore);

        await vm.LoadMoreGroupCommand.ExecuteAsync(null);
        Assert.Equal(ArchiveViewModel.PageSize + 3, vm.GroupRows.Count);
        Assert.False(vm.GroupCanLoadMore);

        vm.SelectedYear = vm.YearOptions.Single(o => o.Id == 2024);
        await vm.FilterReload;
        Assert.Equal(2, vm.GroupRows.Count);
    }

    [Fact]
    public async Task StatsAndDeleteConfirmation_UsePlurals()
    {
        using var db = new TestDb();
        var s = Seed(db);
        AddDocument(db, s, GenDoc.Models.Enums.DocumentSourceType.Generated);
        var vm = await CreateAsync(db);
        await vm.InitializeAsync();

        Assert.StartsWith("1 документ ·", vm.StatsText);
        Assert.Equal("Перемістити 1 документ у кошик?", ArchiveViewModel.DeleteConfirmationText(1, 1, null));
        Assert.Equal("Перемістити 3 документи у кошик?", ArchiveViewModel.DeleteConfirmationText(3, 3, null));
        Assert.Equal(
            "Буде видалено в.2; актуальною стане в.1.",
            ArchiveViewModel.DeleteConfirmationText(1, 2, 1));
        Assert.Equal("Перемістити 5 відомостей у кошик?", ArchiveViewModel.DeleteGroupConfirmationText(5));
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
