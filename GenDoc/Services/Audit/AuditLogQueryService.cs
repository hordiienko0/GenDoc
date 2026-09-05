using GenDoc.Data;
using GenDoc.Models;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services.Audit
{
    public class AuditLogQueryService : IAuditLogQueryService
    {
        private static readonly Dictionary<string, string> EntityNameDisplay = new()
        {
            ["Recipient"] = "Запис особового складу",
            ["Unit"] = "Підрозділ",
            ["Room"] = "Кімната",
            ["UserProfile"] = "Профіль",
            ["ExportTemplate"] = "Шаблон відомості",
            ["Template"] = "Шаблон",
            ["GeneratedDocument"] = "Документ",
            ["GeneratedGroupDocument"] = "Груповий документ",
            ["Intake"] = "Набір",
            ["GenerationPackage"] = "Пакет генерації",
            ["GenerationPackageRun"] = "Запуск генерації",
            ["OrgNode"] = "Папка",
            ["StaffEvent"] = "Постійний склад",
            ["AppSettings"] = "Налаштування",
            ["OrganizationSettings"] = "Налаштування частини"
        };

        private static readonly (string Prefix, string Kind)[] ActionKinds =
        {
            ("Видалено", "delete"),
            ("Вилучено", "delete"),
            ("Створено", "create"),
            ("Відновлено", "create"),
            ("Оновлено", "update"),
            ("Змінено", "update"),
            ("Перейменовано", "update"),
            ("Переміщено", "update"),
            ("Закрито", "update"),
            ("Відкрито повторно", "update"),
            ("Оформлено", "update"),
            ("Імпортовано", "import"),
            ("Експортовано", "export"),
            ("Надруковано", "export"),
            ("Збережено", "export"),
            ("Згенеровано", "generate"),
            ("Перегенеровано", "generate"),
            ("Завантажено", "generate"),
            ("Додано", "generate")
        };

        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public AuditLogQueryService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public List<AuditLogListItem> Query(AuditLogFilter filter)
        {
            using var db = _dbFactory.CreateDbContext();
            var query = db.AuditLog.AsQueryable();

            if (filter.From is { } from)
            {
                var fromDateTime = from.ToDateTime(TimeOnly.MinValue);
                query = query.Where(e => e.OccurredAt >= fromDateTime);
            }

            if (filter.To is { } to)
            {
                var toExclusive = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
                query = query.Where(e => e.OccurredAt < toExclusive);
            }

            if (!string.IsNullOrWhiteSpace(filter.Profile))
                query = query.Where(e => e.UserProfileName == filter.Profile);

            if (!string.IsNullOrWhiteSpace(filter.Action))
                query = query.Where(e => e.Action == filter.Action);

            query = query
                .OrderByDescending(e => e.OccurredAt)
                .ThenByDescending(e => e.Id);

            if (SearchNormalization.PrepareQuery(filter.Search) is { } search)
            {
                return query
                    .AsEnumerable()
                    .Where(e => Matches(e, search))
                    .Skip(filter.Skip)
                    .Take(filter.Take)
                    .Select(ToListItem)
                    .ToList();
            }

            return query
                .Skip(filter.Skip)
                .Take(filter.Take)
                .ToList()
                .Select(ToListItem)
                .ToList();
        }

        private static bool Matches(AuditLogEntry e, string search)
            => SearchNormalization.Contains(
                string.Join(' ', new[] { e.Details, e.OldValue, e.NewValue, e.Action, EntityDisplay(e) }
                    .Where(p => !string.IsNullOrWhiteSpace(p))),
                search);

        public List<string> GetProfiles()
        {
            using var db = _dbFactory.CreateDbContext();
            return db.AuditLog.Select(e => e.UserProfileName).Distinct().OrderBy(p => p).ToList();
        }

        public List<string> GetActions()
        {
            using var db = _dbFactory.CreateDbContext();
            return db.AuditLog.Select(e => e.Action).Distinct().OrderBy(a => a).ToList();
        }

        private static string EntityDisplay(AuditLogEntry e)
        {
            var objectDisplay = EntityNameDisplay.GetValueOrDefault(e.EntityName, e.EntityName);
            if (e.EntityId is { } id && id > 0) objectDisplay += $", №{id}";
            return objectDisplay;
        }

        internal static string ActionKindOf(string action)
        {
            foreach (var (prefix, kind) in ActionKinds)
            {
                if (action.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return kind;
            }
            return "other";
        }

        private static AuditLogListItem ToListItem(AuditLogEntry e)
        {
            string changeDisplay;
            var hasDetails = !string.IsNullOrWhiteSpace(e.Details);
            var hasOld = !string.IsNullOrWhiteSpace(e.OldValue);
            var hasNew = !string.IsNullOrWhiteSpace(e.NewValue);

            if (hasDetails && hasNew && !hasOld)
            {
                changeDisplay = e.Details!.Contains(e.NewValue!, StringComparison.Ordinal)
                    ? e.Details!
                    : $"{e.NewValue} · {e.Details}";
            }
            else if (hasDetails)
            {
                changeDisplay = e.Details!;
            }
            else if (hasOld && hasNew)
            {
                changeDisplay = $"{Truncate(e.OldValue!)} → {Truncate(e.NewValue!)}";
            }
            else
            {
                changeDisplay = e.NewValue ?? e.OldValue ?? "-";
            }

            return new AuditLogListItem(
                e.OccurredAt.ToString("dd.MM.yyyy HH:mm"),
                e.UserProfileName,
                e.Action,
                EntityDisplay(e),
                changeDisplay,
                ActionKindOf(e.Action));
        }

        private static string Truncate(string value)
            => value.Length <= 60 ? value : value[..60] + "…";
    }
}
