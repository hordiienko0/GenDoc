namespace GenDoc.Services.Completeness
{
    /// <summary>
    /// Короткі підписи колонок матриці комплектності.
    ///
    /// Раніше назва просто різалася з ПОЧАТКУ, тож два документи зі спільним
    /// початком («Рапорт котлове ІНДИВІДУАЛЬНИЙ» і «Рапорт котлове ГРУПОВИЙ»)
    /// давали однаковий підпис — дві різні колонки ставали нерозрізненними.
    ///
    /// Правило: показуємо ту частину назви, якою вона РІЗНИТЬСЯ від сусідів.
    /// Повна назва лишається в підказці колонки, тож нічого не втрачається.
    ///
    /// Функція чиста — саме тому вона й покрита тестами без підняття вікна.
    /// </summary>
    public static class MatrixColumnLabels
    {
        /// <summary>Скільки символів заголовка оператор реально бачить у колонці
        /// шириною 96 px. Число потрібне ЛИШЕ щоб зрозуміти, які назви зіллються
        /// на екрані; самі підписи тут не вкорочуються — багатокрапку домальовує
        /// TextTrimming, і різати ще й у коді означало б дві багатокрапки.</summary>
        private const int VisibleLength = 13;

        public static IReadOnlyList<string> Build(IReadOnlyList<string> names)
        {
            if (names.Count == 0) return Array.Empty<string>();

            // Двійники визначаємо за ВИДИМИМ початком: саме там і виникало злиття.
            var groups = names
                .Select((name, index) => (Name: (name ?? string.Empty).Trim(), Index: index))
                .GroupBy(x => VisiblePart(x.Name), StringComparer.Ordinal);

            var labels = new string[names.Count];

            foreach (var group in groups)
            {
                var members = group.ToList();

                if (members.Count == 1)
                {
                    labels[members[0].Index] = members[0].Name;
                    continue;
                }

                var prefix = CommonWordPrefixLength(members.Select(m => m.Name).ToList());

                foreach (var (name, index) in members)
                {
                    var rest = name.Length > prefix ? name[prefix..].Trim() : name;
                    labels[index] = rest.Length == 0 ? name : rest;
                }
            }

            return labels;
        }

        private static string VisiblePart(string name) =>
            name.Length > VisibleLength ? name[..VisibleLength] : name;

        /// <summary>
        /// Довжина спільного початку, обрізана ПО МЕЖІ СЛОВА. Різати посеред
        /// слова не можна: «ове ГРУПОВИЙ» гірше за «ГРУПОВИЙ», бо підпис
        /// починався б з уламка.
        /// </summary>
        private static int CommonWordPrefixLength(IReadOnlyList<string> names)
        {
            var shortest = names.Min(n => n.Length);
            var common = 0;
            while (common < shortest && names.All(n => n[common] == names[0][common]))
                common++;

            if (common == 0) return 0;

            // Відступаємо до останнього пробілу всередині спільної частини.
            var boundary = names[0].LastIndexOf(' ', Math.Min(common, names[0].Length - 1));
            return boundary < 0 ? 0 : boundary + 1;
        }
    }
}
