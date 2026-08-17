using GenDoc.Models;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Generation;

// Порожній правий бік «Генерації» казав лише «Оберіть пакет ліворуч». Найчастіша
// дія — знову запустити те, що запускали минулого разу, — вимагала спершу
// згадати, який то був пакет, і знайти його серед карток.
//
// Кнопки «повторити одним кліком» тут навмисно НЕ буде: тека виводу в запуску не
// зберігається, а повторна генерація перезаписує документи — таке в один клік
// робити не можна. Показуємо, що саме запускали, і відкриваємо той пакет.
public class LastRunTests
{
    private static int SeedPackage(TestDb db, string name)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { Id = 1, FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
        var package = new GenerationPackage { Name = name };
        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();
        return package.Id;
    }

    private static void AddRun(TestDb db, int packageId, DateTime runAt, int generated)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.GenerationPackageRuns.Add(new GenerationPackageRun
        {
            GenerationPackageId = packageId,
            RunAt = runAt,
            RunByUserId = 1,
            GeneratedCount = generated,
            SkippedCount = 0,
            ErrorCount = 0
        });
        ctx.SaveChanges();
    }

    [Fact]
    public void GetLastRun_WithoutAnyRuns_ReturnsNothing()
    {
        using var db = new TestDb();
        SeedPackage(db, "Зброя");

        Assert.Null(TestServices.Generation(db).GetLastRun());
    }

    [Fact]
    public void GetLastRun_ReturnsTheMostRecentRunWithItsPackage()
    {
        using var db = new TestDb();
        var weapons = SeedPackage(db, "Зброя");

        AddRun(db, weapons, new DateTime(2026, 8, 10, 9, 0, 0), generated: 12);
        AddRun(db, weapons, new DateTime(2026, 8, 17, 14, 30, 0), generated: 33);

        var last = TestServices.Generation(db).GetLastRun();

        Assert.NotNull(last);
        Assert.Equal(weapons, last!.PackageId);
        Assert.Equal("Зброя", last.PackageName);
        Assert.Equal(new DateTime(2026, 8, 17, 14, 30, 0), last.RunAt);
        Assert.Equal(33, last.GeneratedCount);
    }

    // Пакет міг бути видалений після запуску — запис прогону лишається, але
    // відкривати вже нічого. Такий прогін показувати не можна: кнопка вела б
    // у нікуди.
    [Fact]
    public void GetLastRun_IgnoresRunsWhosePackageIsGone()
    {
        using var db = new TestDb();
        var packageId = SeedPackage(db, "Зброя");
        AddRun(db, packageId, new DateTime(2026, 8, 17), generated: 5);

        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.GenerationPackages.Remove(ctx.GenerationPackages.First(p => p.Id == packageId));
            ctx.SaveChanges();
        }

        Assert.Null(TestServices.Generation(db).GetLastRun());
    }
}
