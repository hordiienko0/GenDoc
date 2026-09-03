using System.Globalization;

namespace GenDoc.Services.Generation
{
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
