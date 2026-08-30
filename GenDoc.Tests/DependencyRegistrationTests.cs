using System.Text.RegularExpressions;

namespace GenDoc.Tests;

/// <summary>
/// Кожен тип, який код дістає з контейнера через GetRequiredService&lt;T&gt;,
/// мусить бути зареєстрований в App.ConfigureServices.
///
/// Навіщо тест: пропущена реєстрація компілюється без жодного попередження, а
/// падає аж у рантаймі - InvalidOperationException «No service for type» на
/// кліку по кнопці. Саме так сталося з IntakeWizardViewModel: кнопка «Створити
/// набір» у «Наборах» була мертвою, попри 564 зелені тести, бо поруч в
/// OrgTreeViewModel той самий майстер створюється через new і там працює.
///
/// Тест текстовий навмисно, як і StaticResourceScopeTests: підняти справжній
/// контейнер у юніт-тесті означало б підняти WPF-вікна, а розбір коду ловить
/// рівно цей клас помилок.
/// </summary>
public class DependencyRegistrationTests
{
    private static readonly Regex Resolve = new(
        @"Get(?:Required)?Service<\s*([A-Za-z0-9_.]+)\s*>\s*\(", RegexOptions.Compiled);

    private static readonly Regex Register = new(
        @"Add(?:Singleton|Transient|Scoped|DbContextFactory)<\s*([A-Za-z0-9_.]+?)(?:\s*,\s*([A-Za-z0-9_.]+?))?\s*>\s*\(",
        RegexOptions.Compiled);

    // Типи, які надає сам хост, а не наша реєстрація.
    private static readonly HashSet<string> ProvidedByHost = new(StringComparer.Ordinal)
    {
        "IServiceProvider", "IServiceScopeFactory",
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "GenDoc", "Views")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Не знайдено корінь репозиторію");
    }

    // Порівнюємо коротким іменем: у коді той самий тип пишеться то з простором
    // імен (ViewModels.Home.HomeViewModel), то без нього.
    private static string ShortName(string typeName)
    {
        var i = typeName.LastIndexOf('.');
        return i < 0 ? typeName : typeName[(i + 1)..];
    }

    [Fact]
    public void EveryResolvedTypeIsRegisteredInTheContainer()
    {
        var appDir = Path.Combine(RepoRoot(), "GenDoc");
        var appXamlCs = File.ReadAllText(Path.Combine(appDir, "App.xaml.cs"));

        var registered = Register.Matches(appXamlCs)
            .SelectMany(m => new[] { m.Groups[1].Value, m.Groups[2].Value })
            .Where(v => !string.IsNullOrEmpty(v))
            .Select(ShortName)
            .ToHashSet(StringComparer.Ordinal);

        var missing = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.GetFiles(appDir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            foreach (Match m in Resolve.Matches(File.ReadAllText(file)))
            {
                var name = ShortName(m.Groups[1].Value);
                if (ProvidedByHost.Contains(name) || registered.Contains(name)) continue;
                missing.Add($"{name} (у {Path.GetFileName(file)})");
            }
        }

        Assert.True(missing.Count == 0,
            "Ці типи дістаються з контейнера, але не зареєстровані в App.ConfigureServices:\n  "
            + string.Join("\n  ", missing));
    }
}
