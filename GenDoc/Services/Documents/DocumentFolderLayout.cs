using System.Globalization;
using System.IO;

namespace GenDoc.Services.Documents
{
    /// <summary>Куди лягає один документ: перелік вкладених папок і назва файлу
    /// без розширення. Розширення додає той, хто пише файл - він єдиний знає,
    /// docx це чи xlsx.</summary>
    public record DocumentPlacement(IReadOnlyList<string> Folders, string FileName)
    {
        /// <summary>Відносний шлях «Набір №15\Акти\КОВАЛЬЧУК В.Б.docx».</summary>
        public string RelativePath(string extension)
            => Path.Combine(Folders.Append(FileName + extension).ToArray());
    }

    /// <summary>
    /// Розкладка згенерованих документів по папках.
    ///
    /// ОДНЕ місце на двох споживачів: за нею генерація створює теки на диску, і
    /// за нею ж «Архів документів» будує дерево. Тримати це в одному місці
    /// обов'язково - дві копії правил у цьому проєкті вже двічі розходилися
    /// (мапи тегів; розкладка аркуша, звідки й узявся TemplateSheetLayout), і
    /// тут ціна розходження та сама: дерево на екрані показувало б не те, що
    /// лежить на диску.
    ///
    /// Функція чиста: жодної БД, лише те, що їй передали. Тому вона й покрита
    /// тестами без піднімання застосунку.
    /// </summary>
    public static class DocumentFolderLayout
    {
        /// <summary>Верхній рівень для людей поза наборами. Постійний склад не
        /// належить жодному набору, і без власної папки його документи лягали б
        /// у корінь обраної теки - упереміш із папками наборів.</summary>
        public const string PermanentStaffFolder = "Постійний склад";

        /// <summary>Верхній рівень для групових документів, склад яких не
        /// зводиться до одного набору.</summary>
        public const string SharedFolder = "Спільні";

        private const string FallbackFolder = "Без назви";

        /// <summary>Документ на одну особу: набір / тип документа / особа.</summary>
        /// <summary>
        /// Позначка прогону - рівень папки, що відділяє один запуск генерації від
        /// іншого. Рахується ОДИН раз на запуск і передається всім документам:
        /// інакше перші файли впали б у «2026-08-14», а ті, що згенерувалися вже
        /// після півночі чи після перевірки - в іншу папку, і один прогін
        /// розповзся б по двох теках.
        ///
        /// Час додається лише за потреби: звичайний випадок - один прогін на день
        /// - лишається чистим «2026-08-14», а другий за той самий день дістає
        /// «2026-08-14 14-30» замість того, щоб затерти перший.
        /// </summary>
        public static string RunStamp(DateTime now, bool dateFolderAlreadyUsed)
            => dateFolderAlreadyUsed
                // Двокрапка в шляху неприпустима, тож година від хвилин через дефіс.
                ? now.ToString("yyyy-MM-dd HH-mm", CultureInfo.InvariantCulture)
                : now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        public static DocumentPlacement ForPerson(
            string? intakeName, string? templateName, string? runStamp,
            string? personName, string? serviceNumber = null)
        {
            var person = SanitizeFileStem(personName);

            // Двоє однофамільців із однаковими ініціалами в одному наборі дали б
            // той самий шлях, і другий файл тихо затер би перший. Особовий номер
            // розводить їх; його може не бути - тоді лишається ризик, але хоч
            // видимий в імені, а не прихований.
            if (!string.IsNullOrWhiteSpace(serviceNumber))
                person = $"{person} ({Sanitize(serviceNumber)})";

            // Рівень дати відділяє прогони: без нього повторна генерація затирала б
            // попередню, і на диску не лишалося б історії.
            return new DocumentPlacement(
                new[] { IntakeFolder(intakeName), Sanitize(templateName), Sanitize(runStamp) },
                person);
        }

        /// <summary>
        /// Груповий документ, набір якого виводиться зі складу відомості.
        ///
        /// Окремий шлях, бо в моделі груповий документ НЕ належить наборові
        /// (у запитах `IntakeId == null`), тож єдине чесне джерело верхнього
        /// рівня - люди, які в нього потрапили. Усі з одного набору - беремо
        /// його; мішанина або взагалі без набору - «Спільні», бо покласти таку
        /// відомість в один із наборів означало б збрехати про її склад.
        /// </summary>
        public static DocumentPlacement ForGroup(
            IEnumerable<string?> memberIntakeNames, string? templateName, string? runStamp)
        {
            var distinct = memberIntakeNames
                .Select(n => string.IsNullOrWhiteSpace(n) ? null : n.Trim())
                .Distinct(StringComparer.Ordinal)
                .Take(2)
                .ToList();

            var uniform = distinct.Count == 1 ? distinct[0] : null;

            // У груповому документі позначка прогону і є іменем файлу: документ
            // один на весь список, окрема папка під нього була б порожньою.
            return new DocumentPlacement(
                new[] { uniform is null ? SharedFolder : Sanitize(uniform), Sanitize(templateName) },
                Sanitize(runStamp));
        }

        private static string IntakeFolder(string? intakeName)
            => string.IsNullOrWhiteSpace(intakeName) ? PermanentStaffFolder : Sanitize(intakeName);

        /// <summary>Назва шаблону і ПІБ ідуть у назву папки, а там неприпустимі
        /// \/:*?"&lt;&gt;| і керівні символи. Крапки й пробіли в кінці Windows теж
        /// мовчки відкидає, тому знімаємо їх самі - інакше «Акт.» і «Акт» стали б
        /// однією текою.</summary>
        internal static string Sanitize(string? name)
        {
            var cleaned = StripInvalid(name).TrimEnd('.', ' ');
            return cleaned.Length == 0 ? FallbackFolder : cleaned;
        }

        /// <summary>Те саме для ІМЕНІ ФАЙЛУ, але кінцева крапка лишається: у
        /// «КОВАЛЬЧУК В.Б.» вона - ініціал, а не сміття. Для папки її знімати
        /// треба, для основи імені файлу - ні: після неї ще йде розширення, тож
        /// в кінці повного імені вона не опиняється.</summary>
        internal static string SanitizeFileStem(string? name)
        {
            var cleaned = StripInvalid(name);
            return cleaned.Length == 0 ? FallbackFolder : cleaned;
        }

        private static string StripInvalid(string? name)
        {
            var value = (name ?? string.Empty).Trim();
            if (value.Length == 0) return string.Empty;

            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(value.Select(c => invalid.Contains(c) ? ' ' : c).ToArray());

            // Кілька пробілів поспіль після заміни виглядають як помилка.
            return string.Join(" ", cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
