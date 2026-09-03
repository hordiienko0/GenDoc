namespace GenDoc.Services
{
    public static class RankAbbreviation
    {
        private static readonly IReadOnlyDictionary<string, string> DegreeWords =
            new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase)
            {
                ["молодший"] = "мол.",
                ["старший"] = "ст."
            };

        public static string Short(string? rank)
        {
            var value = (rank ?? string.Empty).Trim();
            if (value.Length == 0) return string.Empty;

            var space = value.IndexOf(' ');
            if (space <= 0) return value;

            var first = value[..space];
            if (!DegreeWords.TryGetValue(first, out var shortForm)) return value;

            return shortForm + value[space..];
        }
    }
}
