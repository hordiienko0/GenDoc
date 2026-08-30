using System.Globalization;
using System.Text.RegularExpressions;

namespace GenDoc.Services
{
    // Назви шаблонів приходять з імені завантаженого файлу («Шаблон_Залік_Додаток_8»),
    // і в такому вигляді потрапляли і в список шаблонів, і в імена згенерованих
    // документів. Людині потрібне «Залік Додаток 8», а не технічне ім'я файлу.
    public static class TemplateNaming
    {
        // Роздільник обов'язковий (+, не *): з * збіг наставав і тоді, коли після
        // «шаблон» іде літера, і префікс відкушував початок слова -
        // «Шаблони обліку» перетворювалось на «и обліку» (аудит 2026-08-28).
        // Назва рівно «Шаблон» під це вже не підпадає, але страховка на порожній
        // результат нижче лишається.
        private static readonly Regex TemplatePrefixRegex = new(
            @"^\s*шаблон[_\s\-–—:]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

        // «Шаблон_Залік_Додаток_8» → «Залік Додаток 8».
        // Ідемпотентна: повторний виклик на вже очищеній назві нічого не змінює.
        public static string Clean(string? name)
        {
            var value = (name ?? string.Empty).Replace('_', ' ');
            value = TemplatePrefixRegex.Replace(value, string.Empty);
            value = WhitespaceRegex.Replace(value, " ").Trim();

            // Назва з самого лише слова «Шаблон» після зачистки стала б порожньою -
            // краще лишити вихідний текст, ніж безіменний документ.
            return value.Length == 0 ? (name ?? string.Empty).Trim() : value;
        }

        // Дата для імені файлу - крапками, без підкреслень: «06.08.2026».
        public static string FormatDate(DateTime date)
            => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    }
}
