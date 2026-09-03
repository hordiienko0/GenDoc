using System.Text.RegularExpressions;

namespace GenDoc.Tests;

public class StaticResourceScopeTests
{
    private static readonly Regex ResourceUse = new(@"\{StaticResource\s+([^}\s]+)\s*\}", RegexOptions.Compiled);
    private static readonly Regex ResourceKey = new(@"x:Key=""([^""]+)""", RegexOptions.Compiled);

    private static bool IsIntrinsic(string key) =>
        key.StartsWith("{x:Type", StringComparison.Ordinal) || key.Contains('.');

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "GenDoc", "Views")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Не знайдено корінь репозиторію");
    }

    [Fact]
    public void EveryStaticResourceReferenceResolvesInItsOwnFileOrGlobally()
    {
        var root = RepoRoot();
        var appDir = Path.Combine(root, "GenDoc");

        var globalFiles = new List<string> { Path.Combine(appDir, "App.xaml") };
        globalFiles.AddRange(Directory.GetFiles(Path.Combine(appDir, "Themes"), "*.xaml"));

        var global = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in globalFiles)
            foreach (Match m in ResourceKey.Matches(File.ReadAllText(file)))
                global.Add(m.Groups[1].Value);

        var offenders = new List<string>();

        foreach (var file in Directory.GetFiles(appDir, "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;

            var text = File.ReadAllText(file);

            var local = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in ResourceKey.Matches(text)) local.Add(m.Groups[1].Value);

            foreach (Match m in ResourceUse.Matches(text))
            {
                var key = m.Groups[1].Value;
                if (IsIntrinsic(key)) continue;
                if (local.Contains(key) || global.Contains(key)) continue;

                offenders.Add($"{Path.GetFileName(file)} → {{StaticResource {key}}}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Посилання на ресурс поза областю видимості - застосунок упаде при відкритті розділу:\n"
            + string.Join("\n", offenders.Distinct()));
    }
}
