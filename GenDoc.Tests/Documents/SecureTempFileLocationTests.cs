using System.Reflection;
using GenDoc.Services.Documents;

namespace GenDoc.Tests.Documents;

public class SecureTempFileLocationTests
{
    private static string TempRoot()
        => (string)typeof(SecureTempFileService)
            .GetProperty("TempRoot", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public)!
            .GetValue(null)!;

    [Fact]
    public void TempFilesLiveInTheUserProfile_NotNextToTheDatabase()
    {
        var root = TempRoot();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(localAppData, root, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), root);
    }

    [Fact]
    public void TempRootIsNamedAfterTheApp()
    {
        var root = TempRoot();
        Assert.Contains("GenDoc", root);
        Assert.EndsWith("_temp", root);
    }

    [Fact]
    public async Task Cleanup_AlsoRemovesTheLegacyFolderNextToTheApplication()
    {
        var legacy = Path.Combine(AppContext.BaseDirectory, "_temp");
        var stray = Path.Combine(legacy, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stray);
        var file = Path.Combine(stray, "старий-документ.docx");
        await File.WriteAllBytesAsync(file, new byte[] { 1, 2, 3 });
        File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);

        await new SecureTempFileService().CleanupAsync();

        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task Cleanup_RemovesTheCurrentTempFolder()
    {
        var root = TempRoot();
        var stray = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stray);
        var file = Path.Combine(stray, "документ.docx");
        await File.WriteAllBytesAsync(file, new byte[] { 1, 2, 3 });
        File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);

        await new SecureTempFileService().CleanupAsync();

        Assert.False(File.Exists(file));
    }
}
