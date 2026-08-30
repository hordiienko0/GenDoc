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
            ["ExportTemplate"] = "Шаблон експорту"
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

            // Вторинний ключ обов'язковий: AuditLogService пише DateTime.Now, а
            // роздільність системного годинника ~15 мс, тож усі записи одного
            // SaveChanges мають ОДНАКОВУ мітку. Без tie-break порядок між
            // запитами не гарантований, і між сторінками рядки то дублювались,
            // то зникали (аудит 2026-08-28). За однакового часу новішим
            // вважаємо більший Id - він доданий пізніше.
            var entities = query
                .OrderByDescending(e => e.OccurredAt)
                .ThenByDescending(e => e.Id)
                .Skip(filter.Skip)
                .Take(filter.Take)
                .ToList();

            return entities.Select(ToListItem).ToList();
        }

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

        private static AuditLogListItem ToListItem(AuditLogEntry e)
        {
            var objectDisplay = EntityNameDisplay.GetValueOrDefault(e.EntityName, e.EntityName);
            if (e.EntityId is { } id && id > 0) objectDisplay += $", №{id}";

            string changeDisplay;
            if (!string.IsNullOrWhiteSpace(e.Details))
            {
                changeDisplay = e.Details;
            }
            else if (!string.IsNullOrWhiteSpace(e.OldValue) && !string.IsNullOrWhiteSpace(e.NewValue))
            {
                changeDisplay = $"{Truncate(e.OldValue)} → {Truncate(e.NewValue)}";
            }
            else
            {
                changeDisplay = e.NewValue ?? e.OldValue ?? "-";
            }

            return new AuditLogListItem(
                e.OccurredAt.ToString("dd.MM.yyyy HH:mm"),
                e.UserProfileName,
                e.Action,
                objectDisplay,
                changeDisplay);
        }

        private static string Truncate(string value)
            => value.Length <= 60 ? value : value[..60] + "…";
    }
}
