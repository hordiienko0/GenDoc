using System.Globalization;
using GenDoc.Data;
using GenDoc.Models;
using GenDoc.Models.Enums;

namespace GenDoc.Services.Generation
{
    /// <summary>
    /// Стройова записка курсів рахується сама на «Дату документа»:
    /// КППК - постійний склад (курсові офіцери й інші, причетні до навчання, - люди без набору),
    /// БЗВП - прикомандировані люди набору з документа.
    /// Відрядження, шпиталь (хвороба) і відпустка - події постійного складу, відкриті на цю дату;
    /// у строю = за списком мінус відсутні. «Звільнення» застосунок не веде - воно лишається ручним.
    /// </summary>
    public static class StrengthReport
    {
        private const string Kppk = "кппк";
        private const string Bzvp = "бзвп";
        private const string Total = "загалом";

        private static readonly string[] Rows = { "за_списком", "у_строю", "відсутні", "шпиталь", "відрядження", "відпустка" };
        private static readonly string[] Columns = { Kppk, Bzvp, Total };

        /// <summary>Теги, які застосунок заповнює сам: у скані шаблону вони не стають ручними полями.</summary>
        public static readonly IReadOnlyList<string> Tags = Rows
            .SelectMany(row => Columns.Select(column => $"{{{{{row}_{column}}}}}"))
            .Concat(new[] { "{{відрядження}}", "{{шпиталь}}", "{{відпустка}}" })
            .ToList();

        private const string NobodyText = "немає";

        public static Dictionary<string, string> Build(AppDbContext db, IEnumerable<Recipient> documentRoster, DateOnly date)
        {
            var staff = db.Recipients.Where(r => r.IntakeId == null).ToList();
            var intake = documentRoster.Where(r => r.IntakeId != null).ToList();

            var ids = staff.Concat(intake).Select(r => r.Id).ToList();
            var openEvents = db.StaffEvents
                .Where(e => ids.Contains(e.RecipientId) && e.DateStart <= date && e.DateEnd >= date)
                .ToList();

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var absentByKind = new Dictionary<StaffEventKind, List<Recipient>>();

            foreach (var (column, people) in new[] { (Kppk, staff), (Bzvp, intake) })
            {
                var peopleIds = people.Select(p => p.Id).ToHashSet();
                // Одна людина - одна відсутність, навіть якщо події на цю дату накладаються.
                var kindById = openEvents
                    .Where(e => peopleIds.Contains(e.RecipientId))
                    .GroupBy(e => e.RecipientId)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.DateStart).First().Kind);

                var trip = kindById.Count(k => k.Value == StaffEventKind.BusinessTrip);
                var sick = kindById.Count(k => k.Value == StaffEventKind.Sick);
                var leave = kindById.Count(k => k.Value == StaffEventKind.Leave);

                values[Tag("за_списком", column)] = Number(people.Count);
                values[Tag("у_строю", column)] = Number(people.Count - kindById.Count);
                values[Tag("відсутні", column)] = Number(kindById.Count);
                values[Tag("відрядження", column)] = Number(trip);
                values[Tag("шпиталь", column)] = Number(sick);
                values[Tag("відпустка", column)] = Number(leave);

                foreach (var (id, kind) in kindById)
                {
                    if (!absentByKind.TryGetValue(kind, out var list)) absentByKind[kind] = list = new List<Recipient>();
                    list.Add(people.First(p => p.Id == id));
                }
            }

            foreach (var row in Rows)
            {
                values[Tag(row, Total)] = Number(
                    int.Parse(values[Tag(row, Kppk)], CultureInfo.InvariantCulture)
                    + int.Parse(values[Tag(row, Bzvp)], CultureInfo.InvariantCulture));
            }

            values["{{відрядження}}"] = NamesOf(absentByKind, StaffEventKind.BusinessTrip);
            values["{{шпиталь}}"] = NamesOf(absentByKind, StaffEventKind.Sick);
            values["{{відпустка}}"] = NamesOf(absentByKind, StaffEventKind.Leave);
            return values;
        }

        private static string Tag(string row, string column) => $"{{{{{row}_{column}}}}}";

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>«капітан КОВАЛЬЧУК В. Б., майор ІВАНЕНКО І. І.» або «немає».</summary>
        private static string NamesOf(Dictionary<StaffEventKind, List<Recipient>> absentByKind, StaffEventKind kind) =>
            absentByKind.TryGetValue(kind, out var people) && people.Count > 0
                ? string.Join(", ", people
                    .OrderBy(p => p.LastName, StringComparer.Create(new CultureInfo("uk-UA"), true))
                    .Select(p => $"{p.Rank} {NameFormatter.ShortName(p.LastName, p.FirstName, p.MiddleName)}".Trim()))
                : NobodyText;
    }
}
