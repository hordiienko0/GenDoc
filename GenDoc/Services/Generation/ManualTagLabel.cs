using System.Globalization;

namespace GenDoc.Services.Generation
{
    /// <summary>
    /// Людський підпис поля ручної мітки.
    ///
    /// Оператор бачив рівно те, що пише розробник: «{{дата_зарахування}}»,
    /// «{{кількість_патронів}}» - фігурні дужки й підкреслення. Назва
    /// ВИВОДИТЬСЯ з самого тега, а не береться зі словника: тоді вона є і в
    /// мітки, якої ніхто наперед не передбачив, - а такі з'являються щоразу,
    /// коли завантажують новий шаблон.
    ///
    /// Словник потрібен лише там, де механічне правило дало б неправду:
    /// «Піб начальника» замість «ПІБ начальника».
    ///
    /// Це ПІДПИС, а не заміна тега: сам тег лишається поруч (у підказці), бо
    /// саме за ним оператор звіряється з шаблоном.
    /// </summary>
    public static class ManualTagLabel
    {
        private static readonly HashSet<string> Abbreviations =
            new(StringComparer.CurrentCultureIgnoreCase) { "піб", "вч", "вос", "влк" };

        private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("uk-UA");

        public static string Human(string? tag)
        {
            var inner = (tag ?? string.Empty).Trim().Trim('{', '}').Trim();
            if (inner.Length == 0) return string.Empty;

            var words = inner.Split('_', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return string.Empty;

            var parts = new List<string>(words.Length);
            for (var i = 0; i < words.Length; i++)
            {
                var word = words[i];

                if (Abbreviations.Contains(word))
                {
                    parts.Add(word.ToUpper(Uk));
                    continue;
                }

                // Велика літера лише в ПЕРШОМУ слові - решта лишається як у
                // тезі, інакше «Дата Зарахування» читалося б як заголовок.
                parts.Add(i == 0 ? Capitalize(word) : word);
            }

            return string.Join(' ', parts);
        }

        private static string Capitalize(string word) =>
            word.Length == 1
                ? word.ToUpper(Uk)
                : char.ToUpper(word[0], Uk) + word[1..];
    }
}
