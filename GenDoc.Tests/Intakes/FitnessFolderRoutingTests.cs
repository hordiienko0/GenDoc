using GenDoc.Models;
using GenDoc.Services.Intakes;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Intakes;

// Папки набору - це класифікація за придатністю, тож у кожної категорії мусить
// бути свій дім, а людина мусить переїжджати за зміною статусу. Раніше
// розкладання відбувалося лише при імпорті, і то в три папки: усе, що не
// «придатний»/«обмежено придатний», осідало в «Усіх» (рішення користувача
// 2026-08-31 - завести окрему папку для незаповненої категорії).
public class FitnessFolderRoutingTests
{
    [Theory]
    [InlineData("придатний", IntakeFolderNames.Fit)]
    [InlineData("обмежено придатний", IntakeFolderNames.LimitedFit)]
    [InlineData("непридатний", IntakeFolderNames.Unfit)]
    [InlineData(null, IntakeFolderNames.NoCategory)]
    [InlineData("", IntakeFolderNames.NoCategory)]
    [InlineData("   ", IntakeFolderNames.NoCategory)]
    public void EachCategoryHasItsOwnFolder(string? category, string expected)
        => Assert.Equal(expected, IntakeFitnessFolders.FolderNameFor(category));

    // Регістр у картках плаває, а порівняння кирилиці мусить іти в пам'яті.
    [Theory]
    [InlineData("Придатний", IntakeFolderNames.Fit)]
    [InlineData("ОБМЕЖЕНО ПРИДАТНИЙ", IntakeFolderNames.LimitedFit)]
    public void CategoryMatchingIgnoresCase(string category, string expected)
        => Assert.Equal(expected, IntakeFitnessFolders.FolderNameFor(category));

    // Незнайоме значення - не привід загубити людину: воно поводиться як
    // незаповнене.
    [Fact]
    public void AnUnknownCategoryBehavesLikeAnEmptyOne()
        => Assert.Equal(IntakeFolderNames.NoCategory, IntakeFitnessFolders.FolderNameFor("щось інше"));

    // ─── Пошук і створення папки в базі ──────────────────────────────────────

    private static (int IntakeId, int AllNodeId) SeedIntakeWithAllFolderOnly(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();

        var root = new OrgNode { Name = "Корінь", Depth = 0, SortOrder = 0, Path = "/" };
        ctx.OrgNodes.Add(root);
        ctx.SaveChanges();
        root.Path = $"/{root.Id}/";

        var intake = new Intake { Number = 14, DisplayNumber = "Набір №14" };
        ctx.Intakes.Add(intake);
        ctx.SaveChanges();

        var intakeRoot = new OrgNode
        {
            Name = intake.DisplayNumber, ParentId = root.Id, Depth = 1, SortOrder = 0, IntakeId = intake.Id
        };
        ctx.OrgNodes.Add(intakeRoot);
        ctx.SaveChanges();
        intakeRoot.Path = $"{root.Path}{intakeRoot.Id}/";

        var all = new OrgNode
        {
            Name = IntakeFolderNames.All, ParentId = intakeRoot.Id, Depth = 2, SortOrder = 0, IntakeId = intake.Id
        };
        ctx.OrgNodes.Add(all);
        ctx.SaveChanges();
        all.Path = $"{intakeRoot.Path}{all.Id}/";
        ctx.SaveChanges();

        return (intake.Id, all.Id);
    }

    // Наявні набори створювалися до появи цих папок. Замість разової міграції
    // папка дописується тоді, коли вона вперше знадобилась, - так старий набір
    // теж починає розкладати людей, без окремої дії користувача.
    [Fact]
    public void AMissingFolderIsCreatedOnFirstUse()
    {
        using var db = new TestDb();
        var (intakeId, allNodeId) = SeedIntakeWithAllFolderOnly(db);

        using var ctx = db.Factory.CreateDbContext();
        var folderId = IntakeFitnessFolders.Resolve(ctx, intakeId, "непридатний");

        Assert.NotNull(folderId);
        var created = ctx.OrgNodes.Single(n => n.Id == folderId);
        Assert.Equal(IntakeFolderNames.Unfit, created.Name);
        Assert.Equal(allNodeId, created.ParentId);
        Assert.Equal(intakeId, created.IntakeId);
    }

    [Fact]
    public void ACreatedFolderGetsAConsistentPathAndDepth()
    {
        using var db = new TestDb();
        var (intakeId, allNodeId) = SeedIntakeWithAllFolderOnly(db);

        using var ctx = db.Factory.CreateDbContext();
        var folderId = IntakeFitnessFolders.Resolve(ctx, intakeId, null);

        var parent = ctx.OrgNodes.Single(n => n.Id == allNodeId);
        var created = ctx.OrgNodes.Single(n => n.Id == folderId);
        Assert.Equal(parent.Depth + 1, created.Depth);
        Assert.Equal($"{parent.Path}{created.Id}/", created.Path);
    }

    [Fact]
    public void ResolvingTwiceDoesNotCreateASecondFolder()
    {
        using var db = new TestDb();
        var (intakeId, _) = SeedIntakeWithAllFolderOnly(db);

        using var ctx = db.Factory.CreateDbContext();
        var first = IntakeFitnessFolders.Resolve(ctx, intakeId, "непридатний");
        var second = IntakeFitnessFolders.Resolve(ctx, intakeId, "непридатний");

        Assert.Equal(first, second);
        Assert.Single(ctx.OrgNodes.Where(n => n.Name == IntakeFolderNames.Unfit).ToList());
    }

    // Набір без папки «Всі» - неможливий у нормі, але база могла постраждати.
    // Краще нічого не робити, ніж розкидати людей по корені дерева.
    [Fact]
    public void AnIntakeWithoutTheAllFolderResolvesToNothing()
    {
        using var db = new TestDb();
        int intakeId;
        using (var seed = db.Factory.CreateDbContext())
        {
            var intake = new Intake { Number = 1, DisplayNumber = "Набір №1" };
            seed.Intakes.Add(intake);
            seed.SaveChanges();
            intakeId = intake.Id;
        }

        using var ctx = db.Factory.CreateDbContext();
        Assert.Null(IntakeFitnessFolders.Resolve(ctx, intakeId, "придатний"));
    }
}
