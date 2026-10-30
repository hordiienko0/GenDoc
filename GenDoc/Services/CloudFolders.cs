using System.IO;

namespace GenDoc.Services;

/// <summary>
/// OneDrive у Windows 11 сам забирає «Робочий стіл» і «Документи»: тека, що виглядає локальною,
/// вивантажується в хмару Microsoft. База й документи - персональні дані особового складу, тож
/// програма нічого не пише в OneDrive: лише в локальну базу й теки на ПК.
/// </summary>
public static class CloudFolders
{
    private static readonly string[] OneDriveVariables = { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" };

    public static IReadOnlyList<string> OneDriveRoots()
        => OneDriveVariables
            .Select(Environment.GetEnvironmentVariable)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static bool IsInOneDrive(string? path) => IsInOneDrive(path, OneDriveRoots());

    public static bool IsInOneDrive(string? path, IEnumerable<string> oneDriveRoots)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        string full;
        try { full = Normalize(path); }
        catch (Exception) { return false; }

        return oneDriveRoots.Any(root =>
        {
            var rootFull = Normalize(root);
            return full.Equals(rootFull, StringComparison.OrdinalIgnoreCase)
                   || full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        });
    }

    public static string Warning(string path)
        => $"«{path}» лежить в OneDrive - звідти файли вивантажуються в хмару Microsoft.\n\n"
           + "Оберіть теку на локальному диску, наприклад D:\\ДокОблік.";

    private static string Normalize(string path)
        => Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
