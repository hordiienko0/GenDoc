using GenDoc.Models;
using GenDoc.Models.Enums;

namespace GenDoc.Services
{
    // Правила відмінювання (родовий рід і знахідний відмінок) для прізвищ, імен,
    // по батькові й звань - покриває типові українські закінчення. Не претендує на
    // повноту: для нетипових форм оператор вводить {{...}}_зв уручну (Recipient.*Accusative).
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

        // Флотські звання: «капітан 1 рангу», «капітан 2 рангу». Головне слово тут
        // ПЕРШЕ, а «рангу» - неузгоджене означення в родовому, яке не змінюється.
        // Відмінювання останнього слова, як у решті звань, давало «капітан 1 рангуа».
        private const string NavalRankMarker = "рангу";

        // Звання можуть бути складеними («старший лейтенант») - відмінюється кожне слово:
        // прикметникова частина за прикметниковим правилом, іменникова - за іменниковим.
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

        // Особові імена м'якої групи: «Ігоря», а не «Ігора». Список навмисно
        // вузький - загальне правило для основ на -р зробило б гірше, бо
        // українська тут непослідовна («майора» і «командира» тверді, «Кобзаря»
        // м'яка). Нетипові форми лишаються за ручним Recipient.FullNameAccusative.
        private static readonly HashSet<string> SoftStemGivenNames =
            new(StringComparer.OrdinalIgnoreCase) { "Ігор", "Лазар" };

        private static string DeclineWord(string word, GrammaticalKind kind, Gender gender)
        {
            // Прізвища на -ко: за правописом не відмінюються лише ЖІНОЧІ
            // («Ірину Шевченко»), а чоловічі відмінюються як іменники другої
            // відміни («солдата Шевченка»). До 2026-08-28 код лишав незмінними
            // обидва, і найпоширеніший тип прізвища йшов у накази в називному.
            if (kind == GrammaticalKind.Surname && word.EndsWith("ко", StringComparison.OrdinalIgnoreCase))
                return gender == Gender.Male ? word[..^1] + "а" : word;

            if (kind == GrammaticalKind.GivenName && SoftStemGivenNames.Contains(word))
                return word + "я";

            // Прикметникові закінчення трапляються лише в прізвищах і званнях
            // (Ковальський, «старший»); особові імена на -ій (Юрій) - інший клас, нижче.
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

            // Імена на -о відмінюються (Павло → Павла); прізвища на -о (крім -ко) трапляються
            // рідко в цьому контексті й лишаються незмінними.
            if (word.EndsWith('о'))
                return kind == GrammaticalKind.GivenName ? word[..^1] + "а" : word;

            // Жіночі прізвища/імена на приголосний незмінні (Кравчук, Мельник).
            // ЗВАННЯ сюди не потрапляють: вони граматично чоловічого роду й
            // відмінюються незалежно від статі особи, інакше складене звання
            // виходило напівузгодженим - «старшого лейтенант».
            if (gender == Gender.Female && kind != GrammaticalKind.Rank)
                return word;

            // Імена/прізвища на -й (Юрій, Андрій) втрачають "й" і отримують "я".
            if (word.EndsWith('й'))
                return word[..^1] + "я";

            if (word.EndsWith('ь'))
                return word[..^1] + "я";

            return word + "а";
        }
    }
}
