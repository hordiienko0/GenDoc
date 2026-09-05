using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Intakes;
using GenDoc.Services.OrgTree;
using GenDoc.Services.Personnel;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Personnel;
using GenDoc.ViewModels.Trash;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.Tests.Trash;

public class TrashPeopleTests
{
    private sealed class NoDialogs : IDialogService
    {
        public bool? ShowDialog<TViewModel>(TViewModel viewModel, Window? owner = null) where TViewModel : notnull => null;
    }

    private sealed record Seeded(int RootId, int CourseId, int IntakeAllId, int PersonId);

    private static Seeded Seed(TestDb db, bool withIntake = false)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
        var root = new OrgNode { Name = "Військова частина", Depth = 0, SortOrder = 0, Path = "/" };
        ctx.OrgNodes.Add(root);
        ctx.SaveChanges();
        root.Path = $"/{root.Id}/";

        var course = new OrgNode { Name = "Курс", Depth = 1, SortOrder = 0, ParentId = root.Id, Path = "/" };
        ctx.OrgNodes.Add(course);
        ctx.SaveChanges();
        course.Path = $"{root.Path}{course.Id}/";

        var intakeAllId = 0;
        int? intakeId = null;
        if (withIntake)
        {
            var intake = new Intake
            {
                Number = 5, DisplayNumber = "Набір №5", Status = IntakeStatus.Active,
                DateStart = DateOnly.FromDateTime(DateTime.Today), DateEnd = DateOnly.FromDateTime(DateTime.Today.AddMonths(2))
            };
            ctx.Intakes.Add(intake);
            ctx.SaveChanges();
            var intakeRoot = new OrgNode { Name = "Набір №5", Depth = 1, SortOrder = 1, ParentId = root.Id, Path = "/", IntakeId = intake.Id };
            ctx.OrgNodes.Add(intakeRoot);
            ctx.SaveChanges();
            intakeRoot.Path = $"{root.Path}{intakeRoot.Id}/";
            var all = new OrgNode { Name = IntakeFolderNames.All, Depth = 2, SortOrder = 0, ParentId = intakeRoot.Id, Path = "/", IntakeId = intake.Id };
            ctx.OrgNodes.Add(all);
            ctx.SaveChanges();
            all.Path = $"{intakeRoot.Path}{all.Id}/";
            intake.RootOrgNodeId = intakeRoot.Id;
            course.IntakeId = intake.Id;
            intakeAllId = all.Id;
            intakeId = intake.Id;
        }

        var person = TemplateFixtures.Person(1, "ТКАЧУК", "Олег");
        person.OrgNodeId = course.Id;
        person.IntakeId = intakeId;
        ctx.Recipients.Add(person);
        ctx.SaveChanges();
        return new Seeded(root.Id, course.Id, intakeAllId, person.Id);
    }

    private static PersonnelService Service(TestDb db, FakeAuditLog? audit = null)
        => new(db.Factory, audit ?? new FakeAuditLog(), new FakeCurrentUser());

    [Fact]
    public async Task DeletedPerson_AppearsInTrash_AndRestoresToItsFolder()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var audit = new FakeAuditLog();
        var service = Service(db, audit);

        await service.DeleteManyAsync(new[] { s.PersonId });

        var deleted = Assert.Single(await service.GetDeletedAsync());
        Assert.Equal("ТКАЧУК Олег Петрович", deleted.FullName);
        Assert.Equal("майор", deleted.Rank);
        Assert.Equal("Курс", deleted.Folder);
        Assert.True(deleted.FolderAlive);
        Assert.Equal("Тест Тестович", deleted.DeletedBy);

        var result = await service.RestoreAsync(s.PersonId);

        Assert.True(result.Success);
        Assert.Null(result.Message);
        Assert.Empty(await service.GetDeletedAsync());
        Assert.Contains("Відновлено:Recipient:" + s.PersonId, audit.Entries);

        using var ctx = db.Factory.CreateDbContext();
        var person = await ctx.Recipients.SingleAsync(r => r.Id == s.PersonId);
        Assert.Null(person.DeletedAt);
        Assert.Null(person.DeletedBy);
        Assert.Equal(s.CourseId, person.OrgNodeId);
    }

    [Fact]
    public async Task Restore_ReturnsPersonToIntakeAllFolder_WhenTheirFolderIsGone()
    {
        using var db = new TestDb();
        var s = Seed(db, withIntake: true);
        var service = Service(db);
        await service.DeleteManyAsync(new[] { s.PersonId });
        using (var ctx = db.Factory.CreateDbContext())
        {
            var course = await ctx.OrgNodes.SingleAsync(n => n.Id == s.CourseId);
            course.DeletedAt = DateTime.Now;
            await ctx.SaveChangesAsync();
        }

        var deleted = Assert.Single(await service.GetDeletedAsync());
        Assert.False(deleted.FolderAlive);

        var result = await service.RestoreAsync(s.PersonId);

        Assert.True(result.Success);
        Assert.Equal("Папку «Курс» видалено - особу повернуто до «Всі»", result.Message);
        using var check = db.Factory.CreateDbContext();
        Assert.Equal(s.IntakeAllId, (await check.Recipients.SingleAsync(r => r.Id == s.PersonId)).OrgNodeId);
    }

    [Fact]
    public async Task Restore_ReturnsPersonToRoot_WhenThereIsNoIntake()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var service = Service(db);
        await service.DeleteManyAsync(new[] { s.PersonId });
        using (var ctx = db.Factory.CreateDbContext())
        {
            var course = await ctx.OrgNodes.SingleAsync(n => n.Id == s.CourseId);
            course.DeletedAt = DateTime.Now;
            await ctx.SaveChangesAsync();
        }

        var result = await service.RestoreAsync(s.PersonId);

        Assert.True(result.Success);
        Assert.Equal("Папку «Курс» видалено - особу повернуто до «Військова частина»", result.Message);
        using var check = db.Factory.CreateDbContext();
        Assert.Equal(s.RootId, (await check.Recipients.SingleAsync(r => r.Id == s.PersonId)).OrgNodeId);
    }

    [Fact]
    public async Task Restore_Refuses_WhenAnAlivePersonHasTheSameServiceNumber()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var service = Service(db);
        await service.DeleteManyAsync(new[] { s.PersonId });
        using (var ctx = db.Factory.CreateDbContext())
        {
            var twin = TemplateFixtures.Person(2, "ТКАЧУК", "Олег");
            twin.ServiceNumber = "СН0001";
            twin.OrgNodeId = s.CourseId;
            ctx.Recipients.Add(twin);
            await ctx.SaveChangesAsync();
        }

        var result = await service.RestoreAsync(s.PersonId);

        Assert.False(result.Success);
        Assert.Equal("Особовий номер СН0001 вже використовує ТКАЧУК Олег - спершу змініть номер у тій картці", result.Message);
        Assert.Single(await service.GetDeletedAsync());
    }

    [Fact]
    public async Task TrashViewModel_ListsDeletedPeople_AndRestoreEmptiesTheSection()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var service = Service(db);
        await service.DeleteManyAsync(new[] { s.PersonId });

        var services = new ServiceCollection();
        services.AddSingleton(db.Factory);
        services.AddSingleton<IAuditLogService>(new FakeAuditLog());
        services.AddSingleton<ICurrentUserContext>(new FakeCurrentUser());
        services.AddSingleton<IUserSettingsService, UserSettingsService>();
        services.AddSingleton<IIntakeService, IntakeService>();
        services.AddSingleton<IOrgTreeService, OrgTreeService>();
        services.AddSingleton<ICountService, CountService>();
        services.AddSingleton<IDialogService, NoDialogs>();
        services.AddSingleton<ActiveIntakeState>();
        services.AddSingleton<OrgTreeViewModel>();
        var provider = services.BuildServiceProvider();

        var vm = new TrashViewModel(
            provider.GetRequiredService<IOrgTreeService>(),
            TestServices.Archive(db),
            new NoDialogs(),
            provider.GetRequiredService<OrgTreeViewModel>(),
            service,
            new WeakReferenceMessenger());
        await vm.LoadAsync();

        Assert.True(vm.HasPeople);
        var row = Assert.Single(vm.People);
        Assert.Equal("ТКАЧУК Олег Петрович", row.Title);
        Assert.Equal("майор · Курс", row.Subtitle);
        Assert.False(row.FolderDead);

        await vm.RestorePersonCommand.ExecuteAsync(row);

        Assert.False(vm.HasPeople);
        Assert.Equal("Відновлено: ТКАЧУК Олег Петрович", vm.Notice);
        Assert.False(vm.NoticeIsError);
    }
}
