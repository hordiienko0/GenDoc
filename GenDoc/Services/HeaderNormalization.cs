using System.Text.RegularExpressions;

namespace GenDoc.Services;

public static class HeaderNormalization
{
    public static string Normalize(string header)
    {
        var normalized = header.ToLowerInvariant()
            .Replace("'", string.Empty)
            .Replace("’", string.Empty)
            .Replace('-', ' ')
            .Replace('\n', ' ')
            .Replace('\r', ' ');

        return Regex.Replace(normalized, @"\s+", " ").Trim();
    }
}
