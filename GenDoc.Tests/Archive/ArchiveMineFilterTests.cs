using GenDoc.Models;
using GenDoc.Services.Documents;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Archive;

public class ArchiveMineFilterTests
{
    private static (int U1, int U2) Seed(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        var u1 = new UserProfile { FullName = "Перший", PasswordHash = "x", CreatedAt = DateTime.Now };
        var u2 = new UserProfile { FullName = "Другий", PasswordHash = "x", CreatedAt = DateTime.Now };
        ctx.Users.AddRange(u1, u2);
        ctx.SaveChanges();

        ctx.GeneratedGroupDocuments.AddRange(
            new GeneratedGroupDocument { GeneratedAt = DateTime.Now, GeneratedByUserId = u1.Id, FileName = "a.xlsx", Version = 1, IsCurrent = true, RecipientCount = 1 },
            new GeneratedGroupDocument { GeneratedAt = DateTime.Now, GeneratedByUserId = u2.Id, FileName = "b.xlsx", Version = 1, IsCurrent = true, RecipientCount = 1 });
        ctx.GenerationPackageRuns.AddRange(
            new GenerationPackageRun { RunAt = DateTime.Now, RunByUserId = u1.Id },
            new GenerationPackageRun { RunAt = DateTime.Now, RunByUserId = u2.Id });
        ctx.SaveChanges();
        return (u1.Id, u2.Id);
    }

    [Fact]
    public async Task QueryGroup_MineOnly_FiltersByAuthor()
    {
        using var db = new TestDb();
        var (u1, _) = Seed(db);
        var svc = TestServices.Archive(db);

        var mine = await svc.QueryGroupAsync(new GroupArchiveFilter(null, null, null, 0, 50, UserId: u1));
        var all = await svc.QueryGroupAsync(new GroupArchiveFilter(null, null, null, 0, 50));

        Assert.Single(mine);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task GetRuns_MineOnly_FiltersByRunAuthor()
    {
        using var db = new TestDb();
        var (u1, _) = Seed(db);
        var svc = TestServices.Archive(db);

        var mine = await svc.GetRunsAsync(null, null, u1);
        var all = await svc.GetRunsAsync(null, null);

        Assert.Single(mine);
        Assert.Equal(2, all.Count);
    }
}
