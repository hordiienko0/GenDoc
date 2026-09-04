using System.Globalization;

namespace GenDoc.Services
{
    public static class SearchNormalization
    {
        private static readonly CompareInfo UkCompare = CultureInfo.GetCultureInfo("uk-UA").CompareInfo;

        public static string NormalizeApostrophes(string? text)
            => string.IsNullOrEmpty(text)
                ? string.Empty
                : text.Replace('’', '\'').Replace('ʼ', '\'').Replace('‘', '\'').Replace('`', '\'');

        public static string? PrepareQuery(string? query)
        {
            var trimmed = NormalizeApostrophes(query).Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }

        public static bool Contains(string? haystack, string query)
            => !string.IsNullOrEmpty(haystack)
               && UkCompare.IndexOf(NormalizeApostrophes(haystack), NormalizeApostrophes(query), CompareOptions.IgnoreCase) >= 0;
    }
}
