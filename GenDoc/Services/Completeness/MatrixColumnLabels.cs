namespace GenDoc.Services.Completeness
{
    public static class MatrixColumnLabels
    {
        private const int VisibleLength = 13;

        public static IReadOnlyList<string> Build(IReadOnlyList<string> names)
        {
            if (names.Count == 0) return Array.Empty<string>();

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
                    labels[index] = rest.Length == 0 ? name : WithLeadingWord(name, prefix, rest);
                }
            }

            return labels;
        }

        // Сам хвіст («стаціонарного лікування», «майна») не каже, що це за документ, тож перше слово
        // спільної частини лишається, а пропущене позначає «…»: «Рапорт … стаціонарного лікування».
        private static string WithLeadingWord(string name, int prefix, string rest)
        {
            var firstSpace = name.IndexOf(' ');
            if (firstSpace <= 0 || firstSpace >= prefix) return rest;

            var firstWord = name[..firstSpace];
            return firstSpace + 1 == prefix ? $"{firstWord} {rest}" : $"{firstWord} … {rest}";
        }

        private static string VisiblePart(string name) =>
            name.Length > VisibleLength ? name[..VisibleLength] : name;

        private static int CommonWordPrefixLength(IReadOnlyList<string> names)
        {
            var shortest = names.Min(n => n.Length);
            var common = 0;
            while (common < shortest && names.All(n => n[common] == names[0][common]))
                common++;

            if (common == 0) return 0;

            var boundary = names[0].LastIndexOf(' ', Math.Min(common, names[0].Length - 1));
            return boundary < 0 ? 0 : boundary + 1;
        }
    }
}
