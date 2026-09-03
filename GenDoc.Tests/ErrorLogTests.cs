using GenDoc.Services;

namespace GenDoc.Tests;

public class ErrorLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"gendoc-log-{Guid.NewGuid():N}");
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public void FilePathFor_OneFilePerDay()
        => Assert.Equal(Path.Combine(_dir, "error-2026-08-19.log"), ErrorLog.FilePathFor(_dir, new DateTime(2026, 8, 19, 14, 5, 0)));

    [Fact]
    public void Format_ContainsTypeMessageStackAndInner()
    {
        Exception ex;
        try { throw new InvalidOperationException("зовнішній", new ArgumentException("внутрішній")); }
        catch (Exception e) { ex = e; }

        var text = ErrorLog.Format(new DateTime(2026, 8, 19, 14, 5, 0), "1234", ex);

        Assert.Contains("2026-08-19 14:05:00", text);
        Assert.Contains("користувач: 1234", text);
        Assert.Contains("InvalidOperationException", text);
        Assert.Contains("зовнішній", text);
        Assert.Contains("ArgumentException", text);
        Assert.Contains("внутрішній", text);
        Assert.Contains(nameof(Format_ContainsTypeMessageStackAndInner), text);
    }

    [Fact]
    public void Write_CreatesDirectoryAndAppendsToDailyFile()
    {
        var first = ErrorLog.Write(new Exception("раз"), "1234", _dir);
        var second = ErrorLog.Write(new Exception("два"), null, _dir);

        Assert.Equal(first, second);
        var text = File.ReadAllText(first);
        Assert.Contains("раз", text);
        Assert.Contains("два", text);
        Assert.Contains("користувач: -", text);
    }
}
