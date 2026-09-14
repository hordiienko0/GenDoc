using System.Xml.Linq;

namespace GenDoc.Tests;

/// <summary>
/// Неявний стиль MenuItem у Theme.xaml пофарбований під темну навігаційну панель
/// (MenuTextBrush = #D0E0F2). Контекстне меню без AppContextMenuStyle малюється
/// системним світлим поповером, і майже білий текст на білому тлі має вигляд
/// вимкненого пункту. Тому кожне контекстне меню мусить брати стилі теми.
/// </summary>
public class ContextMenuStyleScopeTests
{
    private const string Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "GenDoc", "Views")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Не знайдено корінь репозиторію");
    }

    private static IEnumerable<string> ViewFiles()
    {
        var appDir = Path.Combine(RepoRoot(), "GenDoc");
        foreach (var file in Directory.GetFiles(appDir, "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}Themes{Path.DirectorySeparatorChar}")) continue;
            yield return file;
        }
    }

    private static bool Uses(XElement element, string attribute, string key) =>
        (string?)element.Attribute(attribute) is { } value && value.Contains(key, StringComparison.Ordinal);

    [Fact]
    public void EveryContextMenuUsesTheAppContextMenuStyles()
    {
        var offenders = new List<string>();

        foreach (var file in ViewFiles())
        {
            var name = Path.GetFileName(file);

            foreach (var menu in XDocument.Load(file).Descendants(XName.Get("ContextMenu", Xaml)))
            {
                if (!Uses(menu, "Style", "AppContextMenuStyle"))
                    offenders.Add($"{name} → ContextMenu без Style=\"{{StaticResource AppContextMenuStyle}}\"");

                // Меню з ItemsSource стилізує пункти через ItemContainerStyle, а не поелементно.
                if (menu.Element(XName.Get("ContextMenu.ItemContainerStyle", Xaml)) is not null) continue;

                foreach (var item in menu.Descendants(XName.Get("MenuItem", Xaml)))
                {
                    if (Uses(item, "Style", "AppContextMenuItemStyle")) continue;
                    offenders.Add($"{name} → MenuItem \"{(string?)item.Attribute("Header")}\" без Style=\"{{StaticResource AppContextMenuItemStyle}}\"");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Контекстне меню без стилів теми - пункти виглядають сірими (вимкненими) на світлому поповері:\n"
            + string.Join("\n", offenders));
    }
}
