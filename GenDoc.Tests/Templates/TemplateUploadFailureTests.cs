using GenDoc.Services;
using GenDoc.Services.Templates;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Templates;

public class TemplateUploadFailureTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    private string TempFile(string extension, byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, content);
        _tempFiles.Add(path);
        return path;
    }

    private static TemplateService BuildService(TestDb db)
        => new(db.Factory, new FakeAuditLog(), new FakeCurrentUser());

    [Fact]
    public void Upload_FileThatIsNotADocx_SaysSo()
    {
        using var db = new TestDb();
        var path = TempFile(".docx", new byte[] { 0x01, 0x02, 0x03 });

        var result = BuildService(db).Upload(path);

        Assert.False(result.Success);
        Assert.Contains("не є документом Word", result.ErrorMessage);
    }

    // Відсутній файл — це НЕ «не документ Word»; повідомлення має називати причину.
    [Fact]
    public void Upload_MissingFile_ReportsThatFileWasNotFound()
    {
        using var db = new TestDb();
        var missing = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.docx");

        var result = BuildService(db).Upload(missing);

        Assert.False(result.Success);
        Assert.Contains("не знайдено", result.ErrorMessage);
        Assert.DoesNotContain("не є документом Word", result.ErrorMessage);
    }
}
