using GenDoc.Services.Generation;

namespace GenDoc.Tests.Generation;

public class RunStampTests
{
    private static string NewFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "gendoc-runstamp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void FirstRunOfTheDay_UsesPlainDate()
    {
        var folder = NewFolder();
        try
        {
            Assert.Equal(DateTime.Now.ToString("yyyy-MM-dd"), GenerationService.ResolveRunStamp(folder));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void SecondRunOfTheDay_AfterPersonalFolder_AddsTime()
    {
        var folder = NewFolder();
        try
        {
            Directory.CreateDirectory(Path.Combine(
                folder, "Набір №1", "Акт", DateTime.Now.ToString("yyyy-MM-dd")));

            Assert.NotEqual(DateTime.Now.ToString("yyyy-MM-dd"), GenerationService.ResolveRunStamp(folder));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void SecondRunOfTheDay_AfterGroupFile_AddsTime()
    {
        var folder = NewFolder();
        try
        {
            var dir = Path.Combine(folder, "Спільні", "Залік Додаток 8");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, DateTime.Now.ToString("yyyy-MM-dd") + ".xlsx"), "x");

            Assert.NotEqual(DateTime.Now.ToString("yyyy-MM-dd"), GenerationService.ResolveRunStamp(folder));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void GroupFileFromAnotherDay_DoesNotAddTime()
    {
        var folder = NewFolder();
        try
        {
            var dir = Path.Combine(folder, "Спільні", "Залік Додаток 8");
            Directory.CreateDirectory(dir);
            File.WriteAllText(
                Path.Combine(dir, DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd") + ".xlsx"), "x");

            Assert.Equal(DateTime.Now.ToString("yyyy-MM-dd"), GenerationService.ResolveRunStamp(folder));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
