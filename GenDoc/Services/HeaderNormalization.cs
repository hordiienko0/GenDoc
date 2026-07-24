using System.Text.RegularExpressions;

namespace GenDoc.Services;

// Спільна нормалізація заголовка колонки — використовується і автомапінгом
// імпорту (ImportService), і автомапінгом при завантаженні шаблону експорту
// (ExportTemplateService), щоб правила зіставлення не розходилися.
public static class HeaderNormalization
{
    public static string Normalize(string header)
    {
        var normalized = header.ToLowerInvariant()
            .Replace("'", string.Empty)
            .Replace("’", string.Empty)
            .Replace("-", string.Empty)
            .Replace('\n', ' ')
            .Replace('\r', ' ');

        return Regex.Replace(normalized, @"\s+", " ").Trim();
    }
}
