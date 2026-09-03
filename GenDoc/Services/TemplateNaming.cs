using System.Globalization;
using System.Text.RegularExpressions;

namespace GenDoc.Services
{
    public static class TemplateNaming
    {
        private static readonly Regex TemplatePrefixRegex = new(
            @"^\s*шаблон[_\s\-–—:]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

        public static string Clean(string? name)
        {
            var value = (name ?? string.Empty).Replace('_', ' ');
            value = TemplatePrefixRegex.Replace(value, string.Empty);
            value = WhitespaceRegex.Replace(value, " ").Trim();

            return value.Length == 0 ? (name ?? string.Empty).Trim() : value;
        }

        public static string FormatDate(DateTime date)
            => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    }
}
