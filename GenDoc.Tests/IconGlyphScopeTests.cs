using System.Text.RegularExpressions;

namespace GenDoc.Tests;

public class IconGlyphScopeTests
{
    private static readonly Regex ButtonTag = new(@"<Button\b[^>]*?/?>", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex ContentAttr = new(@"\bContent=""([^""]*)""", RegexOptions.Compiled);
    private static readonly Regex PuaRef = new(@"^&#x[EeFf][0-9A-Fa-f]{3};$", RegexOptions.Compiled);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "GenDoc", "Views")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Не знайдено корінь репозиторію");
    }

    [Fact]
    public void ButtonsWithMdl2IconStyleUsePrivateUseAreaGlyphs()
    {
        var viewsDir = Path.Combine(RepoRoot(), "GenDoc", "Views");
        var offenders = new List<string>();

        foreach (var file in Directory.GetFiles(viewsDir, "*.xaml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (Match tag in ButtonTag.Matches(text))
            {
                if (!tag.Value.Contains("RowIconButtonStyle", StringComparison.Ordinal)) continue;
                var content = ContentAttr.Match(tag.Value);
                if (!content.Success) continue;
                var value = content.Groups[1].Value;
                var isPua = PuaRef.IsMatch(value) || (value.Length == 1 && value[0] >= '\uE000' && value[0] <= '\uF8FF');
                if (!isPua) offenders.Add($"{Path.GetRelativePath(viewsDir, file)}: Content=\"{value}\"");
            }
        }

        Assert.True(offenders.Count == 0,
            "Кнопки зі стилем RowIconButtonStyle (шрифт Segoe MDL2 Assets) мають текстовий вміст, який цей шрифт не малює:\n"
            + string.Join("\n", offenders));
    }
}
