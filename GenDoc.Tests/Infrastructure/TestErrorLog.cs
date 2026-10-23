using System.Runtime.CompilerServices;
using GenDoc.Services;

namespace GenDoc.Tests.Infrastructure;

/// <summary>
/// Частина тестів навмисно ламає сервіси, і в'юмоделі пишуть ці помилки в ErrorLog.
/// Без перенаправлення вони потрапляли в справжній журнал користувача (%LOCALAPPDATA%\GenDoc\logs).
/// </summary>
internal static class TestErrorLog
{
    public static string Directory { get; } = Path.Combine(Path.GetTempPath(), "GenDoc.Tests", "logs");

    [ModuleInitializer]
    internal static void RedirectErrorLog() => ErrorLog.DirectoryOverride = Directory;
}
