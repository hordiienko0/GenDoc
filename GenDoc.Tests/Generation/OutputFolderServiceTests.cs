using GenDoc.Models;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Generation;

public class OutputFolderServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"gendoc-out-{Guid.NewGuid():N}");
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public async Task SaveDefault_ThenGetDefault_RoundTrips()
    {
        using var db = new TestDb();
        using (var ctx = db.Factory.CreateDbContext()) { ctx.AppSettings.Add(new AppSettings()); ctx.SaveChanges(); }
        var svc = new OutputFolderService(db.Factory);

        await svc.SaveDefaultAsync(_dir);

        Assert.Equal(_dir, await svc.GetDefaultAsync());
    }

    [Fact]
    public async Task ResolveOnDisk_ReturnsPathOnlyWhenFileExists()
    {
        using var db = new TestDb();
        using (var ctx = db.Factory.CreateDbContext()) { ctx.AppSettings.Add(new AppSettings { DefaultOutputFolder = _dir }); ctx.SaveChanges(); }
        var svc = new OutputFolderService(db.Factory);
        Directory.CreateDirectory(Path.Combine(_dir, "Набір"));
        var relative = Path.Combine("Набір", "ШЕВЧЕНКО Тарас.docx");
        File.WriteAllText(Path.Combine(_dir, relative), "x");

        Assert.Equal(Path.Combine(_dir, relative), await svc.ResolveOnDiskAsync(relative));
        Assert.Null(await svc.ResolveOnDiskAsync(Path.Combine("Набір", "немає.docx")));
    }
}
