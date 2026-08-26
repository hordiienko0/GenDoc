using GenDoc.Models;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Completeness;

// Стартовий пакет: мій останній → глобальний типовий → перший за алфавітом.
public class DefaultPackageChainTests
{
    private static (int UserId, int PackageA, int PackageB) Seed(TestDb db, int? globalDefault = null)
    {
        using var ctx = db.Factory.CreateDbContext();
        var user = new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now };
        ctx.Users.Add(user);
        var a = new GenerationPackage { Name = "А-пакет" };
        var b = new GenerationPackage { Name = "Б-пакет" };
        ctx.GenerationPackages.AddRange(a, b);
        ctx.SaveChanges();
        if (globalDefault == -1) globalDefault = a.Id; // сентинел «глобальний = А»
        var settings = ctx.AppSettings.FirstOrDefault();
        if (settings is null)
        {
            settings = new AppSettings();
            ctx.AppSettings.Add(settings);
        }
        settings.DefaultGenerationPackageId = globalDefault;
        ctx.SaveChanges();
        return (user.Id, a.Id, b.Id);
    }

    [Fact]
    public async Task MyLastPackage_WinsWhenItExists()
    {
        using var db = new TestDb();
        var (userId, _, packageB) = Seed(db);
        await TestServices.UserSettings(db, userId).UpdateAsync(s => s.LastPackageId = packageB);

        Assert.Equal(packageB, await TestServices.Completeness(db, userId).GetDefaultPackageIdAsync());
    }

    [Fact]
    public async Task DeletedLastPackage_FallsBackToGlobalDefault()
    {
        using var db = new TestDb();
        var (userId, packageA, _) = Seed(db, globalDefault: -1);
        await TestServices.UserSettings(db, userId).UpdateAsync(s => s.LastPackageId = 999_999);

        Assert.Equal(packageA, await TestServices.Completeness(db, userId).GetDefaultPackageIdAsync());
    }

    [Fact]
    public async Task NothingRemembered_FirstAlphabetically()
    {
        using var db = new TestDb();
        var (userId, packageA, _) = Seed(db);

        Assert.Equal(packageA, await TestServices.Completeness(db, userId).GetDefaultPackageIdAsync());
    }
}
