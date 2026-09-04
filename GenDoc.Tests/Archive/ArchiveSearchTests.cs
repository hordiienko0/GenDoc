using System.Windows;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Documents;
using GenDoc.Services.Generation;
using GenDoc.Services.Intakes;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Archive;
using GenDoc.ViewModels.Generation;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.Tests.Archive;

public class ArchiveSearchTests
{
    private static void Seed(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now });

        var koval = TemplateFixtures.Person(1, "КОВАЛЬ", "Петро");
        var shevchenko = TemplateFixtures.Person(2, "ШЕВЧЕНКО", "Тарас");
        var oconnor = TemplateFixtures.Person(3, "О'КОННОР", "Шон");
        ctx.Recipients.AddRange(koval, shevchenko, oconnor);

        var template = new Template
        {
            Name = "Рапорт", OriginalFileName = "r.docx", Content = Array.Empty<byte>(), UploadedAt = DateTime.Now
        };
        ctx.Templates.Add(template);
        ctx.SaveChanges();

        GeneratedDocument Doc(int recipientId, DateTime at, string file) => new()
        {
            RecipientId = recipientId, TemplateId = template.Id,
            GeneratedAt = at, GeneratedByUserId = 1,
            FileName = file, SizeBytes = 1024, Version = 1, IsCurrent = true,
            SourceType = DocumentSourceType.Generated, HasContent = true
        };

        ctx.GeneratedDocuments.AddRange(
            Doc(koval.Id, new DateTime(2026, 1, 1), "Коваль - Рапорт.docx"),
            Doc(shevchenko.Id, new DateTime(2026, 2, 1), "Шевченко - Рапорт.docx"),
            Doc(oconnor.Id, new DateTime(2026, 3, 1), "О'Коннор - Рапорт.docx"));
        ctx.SaveChanges();
    }

    [Fact]
    public async Task Search_FindsDocumentsBeyondTheFirstPage()
    {
        using var db = new TestDb();
        Seed(db);
        var service = TestServices.Archive(db);

        var firstPage = await service.QueryAsync(new ArchiveFilter(null, null, null, null, null, 0, 2));
        Assert.DoesNotContain(firstPage, r => r.LastName == "КОВАЛЬ");

        var found = await service.QueryAsync(new ArchiveFilter(null, null, null, null, null, 0, 2, "коваль"));
        Assert.Equal("КОВАЛЬ", Assert.Single(found).LastName);

        var stats = await service.GetStatsAsync(new ArchiveFilter(null, null, null, null, null, 0, 2, "коваль"));
        Assert.Equal(1, stats.Count);
        Assert.Equal(1024, stats.TotalBytes);
    }

    [Fact]
    public async Task Search_TreatsTypographicApostropheAsPlain()
    {
        using var db = new TestDb();
        Seed(db);
        var service = TestServices.Archive(db);

        var typographic = await service.QueryAsync(new ArchiveFilter(null, null, null, null, null, 0, 50, "О’Кон"));
        Assert.Equal("О'КОННОР", Assert.Single(typographic).LastName);

        var modifier = await service.QueryAsync(new ArchiveFilter(null, null, null, null, null, 0, 50, "ОʼКон"));
        Assert.Single(modifier);

        var byFileName = await service.QueryAsync(new ArchiveFilter(null, null, null, null, null, 0, 50, "Шевченко - Рапорт"));
        Assert.Equal("ШЕВЧЕНКО", Assert.Single(byFileName).LastName);
    }

    [Fact]
    public async Task StatsLine_ShowsFoundOutOfTotal_WhileSearching()
    {
        using var db = new TestDb();
        Seed(db);
        var vm = await CreateAsync(db);
        await vm.InitializeAsync();

        Assert.Equal(3, vm.RowCount);
        Assert.StartsWith("3 документів", vm.StatsText);

        vm.SearchText = "О’Кон";
        await vm.ApplySearchAsync();

        Assert.Equal(1, vm.RowCount);
        Assert.StartsWith("показано 1 з 3 документів", vm.StatsText);
        Assert.False(vm.IsFilteredEmpty);

        vm.SearchText = "";
        await vm.ApplySearchAsync();

        Assert.Equal(3, vm.RowCount);
        Assert.StartsWith("3 документів", vm.StatsText);
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
