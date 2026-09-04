using System.Globalization;
using System.Text.RegularExpressions;

namespace GenDoc.Services
{
    public enum RankCategory { Officers, Sergeants, Soldiers, Unknown }

    public static class RankOrder
    {
        private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("uk-UA");

        private static readonly Dictionary<string, int> Seniorities = new(StringComparer.Ordinal)
        {
            ["генерал"] = 0,
            ["генерал-лейтенант"] = 1,
            ["генерал-майор"] = 2,
            ["бригадний генерал"] = 3,
            ["полковник"] = 4,
            ["підполковник"] = 5,
            ["майор"] = 6,
            ["капітан"] = 7,
            ["старший лейтенант"] = 8,
            ["лейтенант"] = 9,
            ["молодший лейтенант"] = 10,
            ["головний майстер-сержант"] = 11,
            ["старший майстер-сержант"] = 12,
            ["майстер-сержант"] = 13,
            ["штаб-сержант"] = 14,
            ["головний сержант"] = 15,
            ["старший сержант"] = 16,
            ["сержант"] = 17,
            ["молодший сержант"] = 18,
            ["старший солдат"] = 19,
            ["солдат"] = 20,

            ["капітан 1 рангу"] = 4,
            ["капітан 2 рангу"] = 5,
            ["капітан 3 рангу"] = 6,
            ["капітан-лейтенант"] = 7,
            ["старший мічман"] = 11,
            ["мічман"] = 13,
            ["старший матрос"] = 19,
            ["матрос"] = 20,
        };

        private static readonly (string From, string To)[] Abbreviations = new (string, string)[]
        {
            ("ст. лейтенант", "старший лейтенант"),
            ("ст.лейт.", "старший лейтенант"),
            ("мол. лейтенант", "молодший лейтенант"),
            ("мол. сержант", "молодший сержант"),
            ("ст. сержант", "старший сержант"),
            ("п/п-к", "підполковник"),
            ("п-к", "полковник"),
            ("м-р", "майор"),
            ("к-н", "капітан"),
        }.OrderByDescending(a => a.Item1.Length).ToArray();

        private static readonly string[] Qualifiers =
        {
            "медичної служби", "юстиції", "запасу", "у відставці", "військової служби правопорядку"
        };

        public static string Normalize(string? rank)
        {
            if (string.IsNullOrWhiteSpace(rank)) return string.Empty;

            var text = SearchNormalization.NormalizeApostrophes(rank.Trim().ToLower(Uk));
            text = text.Replace('ґ', 'г');
            text = Regex.Replace(text, @"\s+", " ");

            foreach (var (from, to) in Abbreviations)
                text = text.Replace(from, to);

            foreach (var qualifier in Qualifiers)
                text = text.Replace(qualifier, string.Empty);

            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        public static int Seniority(string? rank)
        {
            var normalized = Normalize(rank);
            return normalized.Length > 0 && Seniorities.TryGetValue(normalized, out var value) ? value : int.MaxValue;
        }

        public static RankCategory Category(string? rank) => Seniority(rank) switch
        {
            >= 0 and <= 10 => RankCategory.Officers,
            >= 11 and <= 18 => RankCategory.Sergeants,
            19 or 20 => RankCategory.Soldiers,
            _ => RankCategory.Unknown
        };

        public static string CategoryDisplayName(RankCategory category) => category switch
        {
            RankCategory.Officers => "Офіцерський склад",
            RankCategory.Sergeants => "Сержантський і старшинський склад",
            RankCategory.Soldiers => "Рядовий склад",
            _ => "Звання не визначено"
        };
    }
}
