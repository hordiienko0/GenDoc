using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Services.Intakes;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Intakes;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.Tests.Intakes;

public class IntakePackageLinkTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-link-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static ServiceProvider BuildProvider(TestDb db, int userId)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db.Factory);
        services.AddSingleton<IAuditLogService>(new FakeAuditLog());
        services.AddSingleton<ICurrentUserContext>(new FakeCurrentUser(userId));
        services.AddSingleton<IUserSettingsService>(TestServices.UserSettings(db, userId));
        services.AddSingleton<GenDoc.Services.Completeness.ICompletenessService>(
            _ => TestServices.Completeness(db, userId));
        services.AddSingleton<IIntakeService, IntakeService>();
        services.AddSingleton<ActiveIntakeState>();
        return services.BuildServiceProvider();
    }

    private sealed record Seeded(int UserId, int IntakeId, int PersonId, int TemplateId, ServiceProvider Provider);

    private static async Task<Seeded> SeedIntakeBeforeAnyPackageAsync(TestDb db)
    {
        int userId, rootId, templateId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var user = new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now };
            ctx.Users.Add(user);
            var root = new OrgNode { Name = "Курс", Depth = 0, SortOrder = 0, Path = "/" };
            ctx.OrgNodes.Add(root);
            var template = new Template
            {
                Name = "Акт", OriginalFileName = "a.docx", Content = new byte[] { 1 },
                UploadedAt = DateTime.Now, Kind = TemplateKind.PerRecipient
            };
            ctx.Templates.Add(template);
            ctx.SaveChanges();
            root.Path = $"/{root.Id}/";
            ctx.SaveChanges();
            userId = user.Id;
            rootId = root.Id;
            templateId = template.Id;
        }

        var provider = BuildProvider(db, userId);
        var intake = await provider.GetRequiredService<IIntakeService>().CreateAsync(new IntakeCreateRequest(
            "Набір №1", rootId,
            DateOnly.FromDateTime(DateTime.Today),
            DateOnly.FromDateTime(DateTime.Today.AddMonths(3))));
        Assert.Null(intake.DefaultPackageId);

        int personId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var allNode = ctx.OrgNodes.Single(n => n.IntakeId == intake.Id && n.Name == IntakeFolderNames.All);
            var person = TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас");
            person.IntakeId = intake.Id;
            person.OrgNodeId = allNode.Id;
            person.FitnessCategory = "придатний";
            ctx.Recipients.Add(person);
            ctx.SaveChanges();
            personId = person.Id;
        }

        return new Seeded(userId, intake.Id, personId, templateId, provider);
    }

    private static int AddPackage(TestDb db, string name, int? templateId = null)
    {
        using var ctx = db.Factory.CreateDbContext();
        var package = new GenerationPackage { Name = name };
        if (templateId is int id)
            package.Templates.Add(new GenerationPackageTemplate
            {
                TemplateId = id, SortOrder = 0,
                RequirementRegular = TemplateRequirement.Required,
                RequirementLimited = TemplateRequirement.Required
            });
        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();
        return package.Id;
    }

    private static int? LinkOf(TestDb db, int intakeId)
    {
        using var ctx = db.Factory.CreateDbContext();
        return ctx.Intakes.Single(i => i.Id == intakeId).DefaultPackageId;
    }

    private static void SetLink(TestDb db, int intakeId, int? packageId)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Intakes.Single(i => i.Id == intakeId).DefaultPackageId = packageId;
        ctx.SaveChanges();
    }

    [Fact]
    public async Task APackageCreatedAfterTheIntakeBecomesItsPackage()
    {
        using var db = new TestDb();
        var s = await SeedIntakeBeforeAnyPackageAsync(db);

        TestServices.Generation(db).CreatePackage("Пакет", null, new List<int> { s.TemplateId },
            new List<(int, FitnessFilter)>());

        int packageId;
        using (var ctx = db.Factory.CreateDbContext())
            packageId = ctx.GenerationPackages.Single().Id;
        Assert.Equal(packageId, LinkOf(db, s.IntakeId));
    }

    [Fact]
    public async Task ASecondPackageDoesNotStealAnAlreadyLinkedIntake()
    {
        using var db = new TestDb();
        var s = await SeedIntakeBeforeAnyPackageAsync(db);
        var generation = TestServices.Generation(db);
        generation.CreatePackage("Перший", null, new List<int> { s.TemplateId }, new List<(int, FitnessFilter)>());
        var first = LinkOf(db, s.IntakeId);

        generation.CreatePackage("Другий", null, new List<int> { s.TemplateId }, new List<(int, FitnessFilter)>());

        Assert.Equal(first, LinkOf(db, s.IntakeId));
    }

    [Fact]
    public async Task TheResolverHealsAnIntakeThatWasNeverLinked()
    {
        using var db = new TestDb();
        var s = await SeedIntakeBeforeAnyPackageAsync(db);
        var packageId = AddPackage(db, "Пакет", s.TemplateId);
        Assert.Null(LinkOf(db, s.IntakeId));

        var resolved = await TestServices.Completeness(db, s.UserId).GetDefaultPackageIdAsync(s.IntakeId);

        Assert.Equal(packageId, resolved);
        Assert.Equal(packageId, LinkOf(db, s.IntakeId));
    }

    [Fact]
    public async Task TheIntakesOwnPackageBeatsTheUsersLastPackage()
    {
        using var db = new TestDb();
        var s = await SeedIntakeBeforeAnyPackageAsync(db);
        var own = AddPackage(db, "Свій", s.TemplateId);
        var last = AddPackage(db, "Останній", s.TemplateId);
        SetLink(db, s.IntakeId, own);
        await TestServices.UserSettings(db, s.UserId).UpdateAsync(u => u.LastPackageId = last);

        var service = TestServices.Completeness(db, s.UserId);

        Assert.Equal(last, await service.GetDefaultPackageIdAsync());
        Assert.Equal(own, await service.GetDefaultPackageIdAsync(s.IntakeId));
    }

    [Fact]
    public async Task ADeletedOwnPackageFallsBackToTheChainAndHeals()
    {
        using var db = new TestDb();
        var s = await SeedIntakeBeforeAnyPackageAsync(db);
        var own = AddPackage(db, "Свій", s.TemplateId);
        var other = AddPackage(db, "Інший", s.TemplateId);
        SetLink(db, s.IntakeId, own);
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.GenerationPackages.Single(p => p.Id == own).DeletedAt = DateTime.Now;
            ctx.SaveChanges();
        }

        var resolved = await TestServices.Completeness(db, s.UserId).GetDefaultPackageIdAsync(s.IntakeId);

        Assert.Equal(other, resolved);
        Assert.Equal(other, LinkOf(db, s.IntakeId));
    }

    [Fact]
    public async Task WithoutAnyPackageTheResolverReturnsNothingAndWritesNothing()
    {
        using var db = new TestDb();
        var s = await SeedIntakeBeforeAnyPackageAsync(db);

        Assert.Null(await TestServices.Completeness(db, s.UserId).GetDefaultPackageIdAsync(s.IntakeId));
        Assert.Null(LinkOf(db, s.IntakeId));
    }

    [Fact]
    public async Task CloseDialogWarnsAboutIncompletePeopleWhenTheLinkWasNeverWritten()
    {
        using var db = new TestDb();
        var s = await SeedIntakeBeforeAnyPackageAsync(db);
        var packageId = AddPackage(db, "Пакет", s.TemplateId);

        var info = await s.Provider.GetRequiredService<IIntakeService>().GetCloseInfoAsync(s.IntakeId);

        Assert.Equal(1, info.IncompletePeopleCount);
        Assert.Equal(packageId, info.PackageId);
    }

    [Fact]
    public async Task OverviewReportsThePackageWhenTheLinkWasNeverWritten()
    {
        using var db = new TestDb();
        var s = await SeedIntakeBeforeAnyPackageAsync(db);
        var packageId = AddPackage(db, "Пакет", s.TemplateId);

        var overview = Assert.Single(await s.Provider.GetRequiredService<IIntakeService>().GetOverviewsAsync());

        Assert.True(overview.HasPackage);
        Assert.Equal(packageId, overview.PackageId);
    }

    [Fact]
    public async Task OverviewWithoutAnyPackageSaysSo()
    {
        using var db = new TestDb();
        var s = await SeedIntakeBeforeAnyPackageAsync(db);

        var overview = Assert.Single(await s.Provider.GetRequiredService<IIntakeService>().GetOverviewsAsync());

        Assert.False(overview.HasPackage);
        Assert.Null(overview.PackageId);
    }

    [Fact]
    public async Task RunningAPackageForTheIntakeAdoptsIt()
    {
        using var db = new TestDb();
        var s = await SeedIntakeBeforeAnyPackageAsync(db);
        var packageId = AddPackage(db, "Пакет");

        TestServices.Generation(db).RunPackage(packageId, _folder, new Dictionary<string, string>(),
            regenerateExisting: false, RosterSelection.Everyone, new Progress<string>());

        Assert.Equal(packageId, LinkOf(db, s.IntakeId));
    }

    private static IntakeOverview Overview(int? packageId) => new(
        1, 1, "Набір №1", IntakeStatus.Active,
        DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today.AddMonths(3)), null,
        RootOrgNodeId: 1, PeopleCount: 5, CompletenessPercent: -1, IncompletePeopleCount: -1, PackageId: packageId);

    [Fact]
    public void CardWithoutAnyPackageIsNotStuckLoading()
    {
        var card = new IntakeCardViewModel(Overview(null));

        Assert.False(card.HasPackage);
        Assert.False(card.IsSummaryLoading);
        Assert.Equal("набір не сформовано", card.IncompleteText);
    }

    [Fact]
    public void CardWithAPackageWaitsForItsSummary()
    {
        var card = new IntakeCardViewModel(Overview(7));

        Assert.True(card.HasPackage);
        Assert.Equal(7, card.PackageId);
        Assert.True(card.IsSummaryLoading);
        Assert.Equal(string.Empty, card.IncompleteText);
    }
}
