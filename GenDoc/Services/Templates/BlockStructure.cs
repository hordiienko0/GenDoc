using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace GenDoc.Services.Templates
{
    public static class BlockStructure
    {
        public static readonly Regex OpenRegex = new(@"^\{\{#([^{}]+)\}\}$", RegexOptions.Compiled);
        public static readonly Regex CloseRegex = new(@"^\{\{/([^{}]+)\}\}$", RegexOptions.Compiled);

        public static readonly Regex EmbeddedMarkerRegex = new(@"\{\{[#/][^{}]+\}\}", RegexOptions.Compiled);

        public static IEnumerable<OpenXmlElement> BlockChildren(OpenXmlCompositeElement container)
            => container.ChildElements.Where(e => e is Paragraph or Table);

        public static IEnumerable<TableRow> Rows(Table table) => table.Elements<TableRow>();

        public static string MarkerText(OpenXmlElement element)
            => string.Concat(element.Descendants<Text>().Select(t => t.Text)).Trim();

        public static string? OpenName(string markerText)
        {
            var match = OpenRegex.Match(markerText);
            return match.Success ? match.Groups[1].Value : null;
        }

        public static string? CloseName(string markerText)
        {
            var match = CloseRegex.Match(markerText);
            return match.Success ? match.Groups[1].Value : null;
        }

        public static bool IsMarkerTag(string tagWithBraces)
            => OpenRegex.IsMatch(tagWithBraces) || CloseRegex.IsMatch(tagWithBraces);
    }
}
