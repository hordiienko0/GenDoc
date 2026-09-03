using System.Text.RegularExpressions;

namespace GenDoc.Tests;

public class StaticBindingScopeTests
{
    private static readonly Regex StaticProperty = new(
        @"public\s+static\s+(?!class\b)[\w<>,\[\]\?\s\.]+?\s+(?<name>\w+)\s*(?:\{\s*get|=>)",
        RegexOptions.Compiled);

    private static readonly Regex InstanceProperty = new(
        @"public\s+(?!static\b)(?!class\b)[\w<>,\[\]\?\s\.]+?\s+(?<name>\w+)\s*(?:\{\s*get|=>)",
        RegexOptions.Compiled);

    private static readonly Regex SimpleBinding = new(
        @"\{Binding\s+(?:Path=)?(?<name>[A-Z]\w*)\s*(?:,[^}]*)?\}",
        RegexOptions.Compiled);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "GenDoc", "Views")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Не знайдено корінь репозиторію");
    }

    private static IEnumerable<string> SourceFiles(string dir) =>
        Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    [Fact]
    public void NoBindingPointsAtAStaticOnlyProperty()
    {
        var appDir = Path.Combine(RepoRoot(), "GenDoc");

        var staticOnly = new HashSet<string>(StringComparer.Ordinal);
        var instance = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in SourceFiles(Path.Combine(appDir, "ViewModels")))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in StaticProperty.Matches(text)) staticOnly.Add(m.Groups["name"].Value);
            foreach (Match m in InstanceProperty.Matches(text)) instance.Add(m.Groups["name"].Value);
        }

        staticOnly.ExceptWith(instance);

        var offenders = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var xaml in Directory.GetFiles(appDir, "*.xaml", SearchOption.AllDirectories))
        {
            if (xaml.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;

            foreach (Match m in SimpleBinding.Matches(File.ReadAllText(xaml)))
            {
                var name = m.Groups["name"].Value;
                if (staticOnly.Contains(name))
                    offenders.Add($"{name} (у {Path.GetFileName(xaml)})");
            }
        }

        Assert.True(offenders.Count == 0,
            "Ці прив'язки вказують на static-властивість в'ю-моделі - WPF їх не резолвить, "
            + "потрібен {x:Static Тип.Ім'я}:\n  " + string.Join("\n  ", offenders));
    }
}
