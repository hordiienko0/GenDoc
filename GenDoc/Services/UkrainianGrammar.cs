using GenDoc.Models;
using GenDoc.Models.Enums;

namespace GenDoc.Services
{
    public static class UkrainianGrammar
    {
        public static Gender Detect(Recipient r)
        {
            var patronymic = r.MiddleName?.Trim();
            if (!string.IsNullOrEmpty(patronymic))
            {
                if (patronymic.EndsWith("ович", StringComparison.OrdinalIgnoreCase)
                    || patronymic.EndsWith("йович", StringComparison.OrdinalIgnoreCase))
                    return Gender.Male;

                if (patronymic.EndsWith("івна", StringComparison.OrdinalIgnoreCase)
                    || patronymic.EndsWith("ївна", StringComparison.OrdinalIgnoreCase))
                    return Gender.Female;
            }

            return r.Gender ?? Gender.Male;
        }

        public static string Accusative(string nominative, GrammaticalKind kind, Gender gender)
        {
            if (string.IsNullOrWhiteSpace(nominative)) return nominative;

            return kind == GrammaticalKind.Rank
                ? DeclineRankPhrase(nominative, gender)
                : DeclineWord(nominative, kind, gender);
        }

        public static string ArrivedVerb(Gender gender) => gender == Gender.Male ? "прибув" : "прибула";
        public static string SuchPronoun(Gender gender) => gender == Gender.Male ? "таким" : "такою";

        private const string NavalRankMarker = "рангу";

        private static string DeclineRankPhrase(string phrase, Gender gender)
        {
            var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return phrase;

            if (words.Contains(NavalRankMarker, StringComparer.OrdinalIgnoreCase))
            {
                words[0] = DeclineWord(words[0], GrammaticalKind.Rank, gender);
                return string.Join(' ', words);
            }

            for (var i = 0; i < words.Length; i++)
            {
                words[i] = i == words.Length - 1
                    ? DeclineWord(words[i], GrammaticalKind.Rank, gender)
                    : DeclineAdjective(words[i]);
            }
            return string.Join(' ', words);
        }

        private static string DeclineAdjective(string word)
        {
            if (word.EndsWith("ій", StringComparison.OrdinalIgnoreCase)) return word[..^2] + "ього";
            if (word.EndsWith("ий", StringComparison.OrdinalIgnoreCase)) return word[..^2] + "ого";
            return word;
        }

        private static readonly HashSet<string> SoftStemGivenNames =
            new(StringComparer.OrdinalIgnoreCase) { "Ігор", "Лазар" };

        private static string DeclineWord(string word, GrammaticalKind kind, Gender gender)
        {
            if (kind == GrammaticalKind.Surname && word.EndsWith("ко", StringComparison.OrdinalIgnoreCase))
                return gender == Gender.Male ? word[..^1] + "а" : word;

            if (kind == GrammaticalKind.GivenName && SoftStemGivenNames.Contains(word))
                return word + "я";

            if (kind is GrammaticalKind.Surname or GrammaticalKind.Rank)
            {
                if (word.EndsWith("ій", StringComparison.OrdinalIgnoreCase))
                    return gender == Gender.Male ? word[..^2] + "ього" : word;

                if (word.EndsWith("ий", StringComparison.OrdinalIgnoreCase))
                    return gender == Gender.Male ? word[..^2] + "ого" : word;
            }

            if (word.EndsWith('а'))
                return word[..^1] + "у";

            if (word.EndsWith('я'))
                return word[..^1] + "ю";

            if (word.EndsWith('о'))
                return kind == GrammaticalKind.GivenName ? word[..^1] + "а" : word;

            if (gender == Gender.Female && kind != GrammaticalKind.Rank)
                return word;

            if (word.EndsWith('й'))
                return word[..^1] + "я";

            if (word.EndsWith('ь'))
                return word[..^1] + "я";

            return word + "а";
        }
    }
}
