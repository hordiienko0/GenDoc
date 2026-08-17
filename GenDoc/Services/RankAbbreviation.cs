namespace GenDoc.Services
{
    /// <summary>
    /// Скорочення звання ДЛЯ СПИСКІВ. У переліках звання стоїть поруч із ПІБ і
    /// посадою, і саме воно з'їдало ширину: «молодший лейтенант» — 18 символів,
    /// через які обрізалося прізвище. Прізвище обрізати найгірше з усього.
    ///
    /// Скорочується лише перше слово-ступінь. Це підпис для списку, а не заміна
    /// даних: картка особи й документи беруть повне звання з `Recipient.Rank`.
    /// </summary>
    public static class RankAbbreviation
    {
        // Ключ — окреме ПЕРШЕ слово. «Старшина» починається на ті самі літери,
        // але це самостійне звання, і калічити його не можна — тому порівняння
        // йде по цілому слову, а не по початку рядка.
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
