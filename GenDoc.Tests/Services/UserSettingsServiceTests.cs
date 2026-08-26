using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Tests.Services;

public class UserSettingsServiceTests
{
    private static int SeedUser(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        var user = new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now };
        ctx.Users.Add(user);
        ctx.SaveChanges();
        return user.Id;
    }

    [Fact]
    public async Task GetForCurrentUser_CreatesOnce_SecondCallReturnsSameRow()
    {
        using var db = new TestDb();
        var userId = SeedUser(db);
        var svc = TestServices.UserSettings(db, userId);

        var first = await svc.GetForCurrentUserAsync();
        var second = await svc.GetForCurrentUserAsync();

        Assert.Equal(first.Id, second.Id);
        using var ctx = db.Factory.CreateDbContext();
        Assert.Equal(1, await ctx.UserSettings.CountAsync());
        Assert.Equal(userId, first.UserProfileId);
        Assert.False(first.ArchiveMineOnly);
    }

    [Fact]
    public async Task Update_PersistsEveryField()
    {
        using var db = new TestDb();
        var userId = SeedUser(db);
        var svc = TestServices.UserSettings(db, userId);

        await svc.UpdateAsync(s =>
        {
            s.ActiveIntakeId = 7;
            s.LastPackageId = 3;
            s.ArchiveMineOnly = true;
            s.LastManualValuesJson = "{\"а\":\"б\"}";
            s.LastSignerByTemplateJson = "{\"ш\":1}";
        });

        var loaded = await svc.GetForCurrentUserAsync();
        Assert.Equal(7, loaded.ActiveIntakeId);
        Assert.Equal(3, loaded.LastPackageId);
        Assert.True(loaded.ArchiveMineOnly);
        Assert.Equal("{\"а\":\"б\"}", loaded.LastManualValuesJson);
        Assert.Equal("{\"ш\":1}", loaded.LastSignerByTemplateJson);
    }

    [Fact]
    public async Task WithoutUser_ReadReturnsDefaults_WriteIsNoop()
    {
        using var db = new TestDb();
        var svc = TestServices.UserSettings(db, userId: null);

        var settings = await svc.GetForCurrentUserAsync();
        await svc.UpdateAsync(s => s.ArchiveMineOnly = true);

        Assert.False(settings.ArchiveMineOnly);
        using var ctx = db.Factory.CreateDbContext();
        Assert.Equal(0, await ctx.UserSettings.CountAsync());
    }

    [Fact]
    public async Task DeletingProfile_CascadesSettings()
    {
        using var db = new TestDb();
        var userId = SeedUser(db);
        var svc = TestServices.UserSettings(db, userId);
        await svc.GetForCurrentUserAsync();

        using (var ctx = db.Factory.CreateDbContext())
        {
            var user = await ctx.Users.FirstAsync(u => u.Id == userId);
            ctx.Users.Remove(user);
            await ctx.SaveChangesAsync();
        }

        using var check = db.Factory.CreateDbContext();
        Assert.Equal(0, await check.UserSettings.CountAsync());
    }
}
