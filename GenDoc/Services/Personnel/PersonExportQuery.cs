using GenDoc.Models.Enums;

namespace GenDoc.Services.Personnel
{
    public enum PersonExportSort
    {
        Alphabet,
        Rank,
        Folder
    }

    public enum PersonExportOperator
    {
        Contains,
        NotContains,
        EqualTo,
        IsEmpty,
        IsNotEmpty
    }

    public sealed record PersonExportCondition(PersonExportField Field, PersonExportOperator Operator, string? Value)
    {
        public bool NeedsValue => Operator is not (PersonExportOperator.IsEmpty or PersonExportOperator.IsNotEmpty);

        /// <summary>Умову з порожнім значенням ще набирають - на список вона не впливає.</summary>
        public bool IsActive => !NeedsValue || !string.IsNullOrWhiteSpace(Value);
    }

    public sealed class PersonExportFilter
    {
        /// <summary>Папка, з якої беремо людей; з IncludeDescendants = false - лише ті, хто лежить просто в ній.</summary>
        public int NodeId { get; init; }
        public bool IncludeDescendants { get; init; } = true;

        /// <summary>Позначені галочкою в списку: якщо задано, папка вже не обмежує.</summary>
        public IReadOnlyCollection<int>? OnlyIds { get; init; }

        public string? SearchText { get; init; }

        /// <summary>null - усі категорії звань.</summary>
        public IReadOnlyCollection<RankCategory>? RankCategories { get; init; }

        public FitnessFilter Fitness { get; init; } = FitnessFilter.All;

        public IReadOnlyList<PersonExportCondition> Conditions { get; init; } = Array.Empty<PersonExportCondition>();
    }

    public static class PersonExportQuery
    {
        /// <summary>Хто потрапляє під папку чи галочки - ще до фільтрів (від цього рахуються лічильники звань).</summary>
        public static IEnumerable<PersonExportRow> InScope(IEnumerable<PersonExportRow> rows, PersonExportFilter filter)
        {
            if (filter.OnlyIds is { } ids)
            {
                var set = ids as ISet<int> ?? new HashSet<int>(ids);
                return rows.Where(r => set.Contains(r.Person.Id));
            }

            return filter.IncludeDescendants ? rows : rows.Where(r => r.Person.OrgNodeId == filter.NodeId);
        }

        public static List<PersonExportRow> Apply(
            IEnumerable<PersonExportRow> rows, PersonExportFilter filter, PersonExportSort sort)
        {
            var result = InScope(rows, filter);

            if (SearchNormalization.PrepareQuery(filter.SearchText) is { } query)
                result = result.Where(r => SearchNormalization.Contains(SearchHaystack(r), query));

            if (filter.RankCategories is { } categories)
            {
                var set = categories as ISet<RankCategory> ?? new HashSet<RankCategory>(categories);
                result = result.Where(r => set.Contains(RankOrder.Category(r.Person.Rank)));
            }

            if (filter.Fitness != FitnessFilter.All)
                result = result.Where(r => FitnessCategoryHelper.Matches(filter.Fitness, r.Person.FitnessCategory));

            foreach (var condition in filter.Conditions.Where(c => c.IsActive))
            {
                var current = condition;
                result = result.Where(r => Matches(current, r));
            }

            return Sort(result, sort).ToList();
        }

        public static bool Matches(PersonExportCondition condition, PersonExportRow row)
        {
            var text = condition.Field.Text(row).Trim();
            var value = (condition.Value ?? string.Empty).Trim();

            return condition.Operator switch
            {
                PersonExportOperator.IsEmpty => text.Length == 0,
                PersonExportOperator.IsNotEmpty => text.Length > 0,
                PersonExportOperator.Contains => SearchNormalization.Contains(text, value),
                PersonExportOperator.NotContains => !SearchNormalization.Contains(text, value),
                PersonExportOperator.EqualTo => UkrainianCollation.IgnoreCase.Equals(
                    SearchNormalization.NormalizeApostrophes(text), SearchNormalization.NormalizeApostrophes(value)),
                _ => true
            };
        }

        private static IEnumerable<PersonExportRow> Sort(IEnumerable<PersonExportRow> rows, PersonExportSort sort)
        {
            var ordered = sort switch
            {
                PersonExportSort.Rank => rows.OrderBy(r => RankOrder.Seniority(r.Person.Rank)),
                PersonExportSort.Folder => rows.OrderBy(r => r.FolderOrder),
                _ => rows.OrderBy(_ => 0)
            };

            return ordered
                .ThenBy(r => r.Person.LastName, UkrainianCollation.Surname)
                .ThenBy(r => r.Person.FirstName, UkrainianCollation.Surname)
                .ThenBy(r => r.Person.MiddleName ?? string.Empty, UkrainianCollation.Surname)
                .ThenBy(r => r.Person.Id);
        }

        // Те саме, що шукає рядок пошуку над списком: ПІБ і посада.
        private static string SearchHaystack(PersonExportRow row) => string.Join(' ',
            new[] { row.Person.LastName, row.Person.FirstName, row.Person.MiddleName, row.Person.Position }
                .Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    /// <summary>Готова таблиця: заголовки й значення клітинок (string, DateTime, int або null).</summary>
    public sealed record PersonExportTable(IReadOnlyList<string> Headers, IReadOnlyList<object?[]> Rows)
    {
        public static PersonExportTable Build(IReadOnlyList<PersonExportRow> rows, IReadOnlyList<PersonExportField> fields)
        {
            var data = new List<object?[]>(rows.Count);
            for (var i = 0; i < rows.Count; i++)
            {
                var cells = new object?[fields.Count];
                for (var c = 0; c < fields.Count; c++)
                    cells[c] = fields[c].IsRowNumber ? i + 1 : Normalize(fields[c].Value(rows[i]));
                data.Add(cells);
            }

            return new PersonExportTable(fields.Select(f => f.Label).ToList(), data);
        }

        /// <summary>Таблиця для буфера обміну: колонки через табуляцію - так її розкладає по клітинках і Excel, і Word.</summary>
        public string ToClipboardText()
        {
            var lines = new List<string>(Rows.Count + 1) { string.Join('\t', Headers.Select(Flatten)) };
            lines.AddRange(Rows.Select(row => string.Join('\t', row.Select(v => Flatten(PersonExportFields.AsText(v))))));
            return string.Join(Environment.NewLine, lines);
        }

        private static object? Normalize(object? value)
            => value is string text ? (string.IsNullOrWhiteSpace(text) ? null : text.Trim()) : value;

        private static string Flatten(string text)
            => text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
    }
}
