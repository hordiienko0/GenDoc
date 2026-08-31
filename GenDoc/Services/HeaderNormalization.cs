using System.Text.RegularExpressions;

namespace GenDoc.Services;

// Спільна нормалізація заголовка колонки - використовується і автомапінгом
// імпорту (ImportService), і автомапінгом при завантаженні шаблону експорту
// (ExportTemplateService), щоб правила зіставлення не розходилися.
public static class HeaderNormalization
{
    public static string Normalize(string header)
    {
        // Дефіс стає ПРОБІЛОМ, а не зникає. Правила зіставлення писані через
        // пробіл («по батькові», «особовий номер»), тож видалення дефіса робило
        // «По-батькові» → «побатькові» - колонка переставала зіставлятися
        // взагалі й мовчки пропадала (аудит 2026-08-28). Односкладові правила
        // («прод») від зайвого пробілу не страждають, бо шукаються входженням.
        var normalized = header.ToLowerInvariant()
            .Replace("'", string.Empty)
            .Replace("’", string.Empty)
            .Replace('-', ' ')
            .Replace('\n', ' ')
            .Replace('\r', ' ');

        return Regex.Replace(normalized, @"\s+", " ").Trim();
    }
}
