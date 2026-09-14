using GenDoc.Models;
using GenDoc.Models.Enums;

namespace GenDoc.Services
{
    public static class UkrainianGrammar
    {
        private enum Case
        {
            Accusative,
            Dative
        }

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
            => Decline(nominative, kind, gender, Case.Accusative);

        /// <summary>Давальний відмінок: «надати капітану БУГАЮ Сергію Михайловичу».</summary>
        public static string Dative(string nominative, GrammaticalKind kind, Gender gender)
            => Decline(nominative, kind, gender, Case.Dative);

        private static string Decline(string nominative, GrammaticalKind kind, Gender gender, Case grammaticalCase)
        {
            if (string.IsNullOrWhiteSpace(nominative)) return nominative;

            return kind == GrammaticalKind.Rank
                ? DeclineRankPhrase(nominative, gender, grammaticalCase)
                : DeclineWord(nominative, kind, gender, grammaticalCase);
        }

        public static string ArrivedVerb(Gender gender) => gender == Gender.Male ? "прибув" : "прибула";
        public static string SuchPronoun(Gender gender) => gender == Gender.Male ? "таким" : "такою";

        /// <summary>«приступив/приступила до виконання службових обов'язків».</summary>
        public static string ResumedDutyVerb(Gender gender) => gender == Gender.Male ? "приступив" : "приступила";

        /// <summary>«з правилами … ознайомлений/ознайомлена».</summary>
        public static string AcquaintedAdjective(Gender gender) => gender == Gender.Male ? "ознайомлений" : "ознайомлена";

        // -----------------------------------------------------------------
        // Посада: відмінюється тільки головне слово (з означеннями перед ним),
        // залежні слова в родовому лишаються як є:
        // «начальник служби захисту …» → «начальника служби захисту …».
        // -----------------------------------------------------------------

        public static string PositionAccusative(string position, Gender gender)
            => DeclinePosition(position, gender, Case.Accusative);

        public static string PositionDative(string position, Gender gender)
            => DeclinePosition(position, gender, Case.Dative);

        private static readonly string[] PositionSeparators = { " - ", " – ", " — " };

        private static string DeclinePosition(string position, Gender gender, Case grammaticalCase)
        {
            if (string.IsNullOrWhiteSpace(position)) return position;

            foreach (var separator in PositionSeparators)
            {
                if (!position.Contains(separator, StringComparison.Ordinal)) continue;

                var parts = position.Split(separator, StringSplitOptions.None)
                    .Select(part => DeclinePosition(part, gender, grammaticalCase));
                return string.Join(separator, parts);
            }

            var words = position.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return position;

            var head = 0;
            while (head < words.Length && IsAdjective(words[head]))
            {
                words[head] = DeclineAdjective(words[head], grammaticalCase);
                head++;
            }

            if (head < words.Length)
                words[head] = DeclineWord(words[head], GrammaticalKind.Rank, gender, grammaticalCase);

            return string.Join(' ', words);
        }

        private static bool IsAdjective(string word)
            => word.EndsWith("ий", StringComparison.OrdinalIgnoreCase)
               || word.EndsWith("ій", StringComparison.OrdinalIgnoreCase);

        private const string NavalRankMarker = "рангу";

        private static string DeclineRankPhrase(string phrase, Gender gender, Case grammaticalCase)
        {
            var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return phrase;

            if (words.Contains(NavalRankMarker, StringComparer.OrdinalIgnoreCase))
            {
                words[0] = DeclineWord(words[0], GrammaticalKind.Rank, gender, grammaticalCase);
                return string.Join(' ', words);
            }

            for (var i = 0; i < words.Length; i++)
            {
                words[i] = i == words.Length - 1
                    ? DeclineWord(words[i], GrammaticalKind.Rank, gender, grammaticalCase)
                    : DeclineAdjective(words[i], grammaticalCase);
            }
            return string.Join(' ', words);
        }

        private static string DeclineAdjective(string word, Case grammaticalCase)
        {
            if (word.EndsWith("ій", StringComparison.OrdinalIgnoreCase))
                return word[..^2] + (grammaticalCase == Case.Dative ? "ьому" : "ього");

            if (word.EndsWith("ий", StringComparison.OrdinalIgnoreCase))
                return word[..^2] + (grammaticalCase == Case.Dative ? "ому" : "ого");

            return word;
        }

        private static readonly HashSet<string> SoftStemGivenNames =
            new(StringComparer.OrdinalIgnoreCase) { "Ігор", "Лазар" };

        private static string DeclineWord(string word, GrammaticalKind kind, Gender gender, Case grammaticalCase)
            => grammaticalCase == Case.Dative
                ? DeclineWordDative(word, kind, gender)
                : DeclineWordAccusative(word, kind, gender);

        private static string DeclineWordAccusative(string word, GrammaticalKind kind, Gender gender)
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

        private const string Vowels = "аеєиіїоуюя";

        private static readonly (string Ending, string Replacement)[] AdjectivalFeminineEndings =
        {
            ("ська", "ській"), ("цька", "цькій"), ("зька", "зькій")
        };

        private static string DeclineWordDative(string word, GrammaticalKind kind, Gender gender)
        {
            if (kind == GrammaticalKind.Surname && word.EndsWith("ко", StringComparison.OrdinalIgnoreCase))
                return gender == Gender.Male ? word[..^1] + "у" : word;

            if (kind == GrammaticalKind.GivenName && SoftStemGivenNames.Contains(word))
                return word + "ю";

            if (kind is GrammaticalKind.Surname or GrammaticalKind.Rank)
            {
                if (word.EndsWith("ій", StringComparison.OrdinalIgnoreCase))
                    return gender == Gender.Male ? word[..^2] + "ьому" : word;

                if (word.EndsWith("ий", StringComparison.OrdinalIgnoreCase))
                    return gender == Gender.Male ? word[..^2] + "ому" : word;
            }

            foreach (var (ending, replacement) in AdjectivalFeminineEndings)
            {
                if (word.EndsWith(ending, StringComparison.OrdinalIgnoreCase))
                    return word[..^ending.Length] + replacement;
            }

            if (word.EndsWith('а'))
                return SoftenStem(word[..^1]) + "і";

            if (word.EndsWith('я'))
            {
                var stem = word[..^1];
                return stem.Length > 0 && Vowels.Contains(char.ToLowerInvariant(stem[^1]))
                    ? stem + "ї"
                    : stem + "і";
            }

            if (word.EndsWith('о'))
                return kind == GrammaticalKind.GivenName ? word[..^1] + "у" : word;

            if (gender == Gender.Female && kind != GrammaticalKind.Rank)
                return word;

            if (word.EndsWith('й'))
                return word[..^1] + "ю";

            if (word.EndsWith('ь'))
                return word[..^1] + "ю";

            return word + "у";
        }

        /// <summary>Чергування приголосних перед «-і»: Ольга → Ользі, Сорока → Сороці, Муха → Мусі.</summary>
        private static string SoftenStem(string stem)
        {
            if (stem.Length == 0) return stem;

            var last = stem[^1];
            var softened = char.ToLowerInvariant(last) switch
            {
                'к' => 'ц',
                'г' => 'з',
                'х' => 'с',
                _ => last
            };

            if (softened == last) return stem;

            if (char.IsUpper(last)) softened = char.ToUpperInvariant(softened);
            return stem[..^1] + softened;
        }
    }
}
