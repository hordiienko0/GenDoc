using System.Reflection;
using GenDoc.Services.Documents;

namespace GenDoc.Tests.Documents;

// Тимчасові копії - це РОЗШИФРОВАНІ персональні дані. Вони лежали в теці
// застосунку (`AppContext.BaseDirectory/_temp`) поруч із базою: та тека часто
// живе на мережевому диску або на флешці, звідки її ніхто не витирає, і
// потрапляє в резервні копії дистрибутива цілком. Місце - профіль користувача
// (аудит 2026-08-28).
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

    // Прибирання мусить зачепити й стару теку: інсталяції, що вже працювали,
    // мають розшифровані документи в теці застосунку, і переїзд сам собою їх
    // там і залишив би назавжди.
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
