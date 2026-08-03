using GenDoc.Data;
using GenDoc.Models;
using GenDoc.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services.Staff
{
    public class StaffService : IStaffService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly Completeness.ICompletenessService _completenessService;
        private readonly IAuditLogService _auditLogService;
        private readonly ICurrentUserContext _currentUserContext;

        public StaffService(
            IDbContextFactory<AppDbContext> dbFactory,
            Completeness.ICompletenessService completenessService,
            IAuditLogService auditLogService,
            ICurrentUserContext currentUserContext)
        {
            _dbFactory = dbFactory;
            _completenessService = completenessService;
            _auditLogService = auditLogService;
            _currentUserContext = currentUserContext;
        }

        public async Task<IReadOnlyList<StaffRowOverview>> GetOverviewAsync()
        {
            using var db = _dbFactory.CreateDbContext();
            var today = DateOnly.FromDateTime(DateTime.Today);

            var staff = await db.Recipients
                .Where(r => r.IntakeId == null)
                .Include(r => r.Unit)
                .AsNoTracking()
                .OrderBy(r => r.LastName).ThenBy(r => r.FirstName)
                .ToListAsync();

            var staffIds = staff.Select(s => s.Id).ToList();

            // Один груповий запит на кількість документів для всіх — не в циклі.
            var docCounts = await db.GeneratedDocuments
                .Where(g => g.IsCurrent && staffIds.Contains(g.RecipientId))
                .GroupBy(g => g.RecipientId)
                .Select(g => new { RecipientId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.RecipientId, x => x.Count);

            // Активний на сьогодні період (якщо є) для кожної людини — теж один запит.
            var activeEvents = await db.StaffEvents
                .Where(e => staffIds.Contains(e.RecipientId) && e.DateStart <= today && e.DateEnd >= today)
                .ToListAsync();
            var stateByRecipient = activeEvents
                .GroupBy(e => e.RecipientId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.DateStart).First().Kind);

            var rows = new List<StaffRowOverview>(staff.Count);
            foreach (var r in staff)
            {
                var state = stateByRecipient.TryGetValue(r.Id, out var kind) ? kind : (StaffEventKind?)null;
                rows.Add(new StaffRowOverview(
                    r.Id, r.FullName, r.Rank, r.Position, r.Unit?.Name, state, docCounts.GetValueOrDefault(r.Id)));
            }
            return rows;
        }

        public async Task<IReadOnlyList<string>> GetUnitsAsync()
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.Recipients
                .Where(r => r.IntakeId == null && r.Unit != null)
                .Select(r => r.Unit!.Name)
                .Distinct()
                .OrderBy(n => n)
                .ToListAsync();
        }

        public async Task IssueDocumentsAsync(
            StaffEventKind kind, IReadOnlyList<int> recipientIds, IReadOnlyList<int> templateIds,
            DateOnly dateStart, DateOnly dateEnd, string? note)
        {
            using var db = _dbFactory.CreateDbContext();

            foreach (var recipientId in recipientIds)
            {
                db.StaffEvents.Add(new StaffEvent
                {
                    RecipientId = recipientId,
                    Kind = kind,
                    DateStart = dateStart,
                    DateEnd = dateEnd,
                    Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                    CreatedAt = DateTime.Now,
                    CreatedBy = _currentUserContext.CurrentUserFullName
                });
            }

            var kindLabel = kind == StaffEventKind.BusinessTrip ? "відрядження" : "відпустку";
            _auditLogService.Log(db, $"Оформлено {kindLabel}", "StaffEvent", 0, null, null,
                $"{recipientIds.Count} осіб, {templateIds.Count} шаблонів, {dateStart:dd.MM.yyyy}–{dateEnd:dd.MM.yyyy}");
            await db.SaveChangesAsync();

            foreach (var recipientId in recipientIds)
            {
                foreach (var templateId in templateIds)
                {
                    await _completenessService.GenerateForPairAsync(recipientId, templateId, new Dictionary<string, string>());
                }
            }
        }

        public async Task DeleteManyAsync(IReadOnlyList<int> recipientIds)
        {
            using var db = _dbFactory.CreateDbContext();
            var recipients = await db.Recipients.Where(r => recipientIds.Contains(r.Id)).ToListAsync();

            foreach (var r in recipients)
            {
                var snapshot = $"{r.LastName} {r.FirstName} {r.MiddleName} · {r.Rank} · {r.Position}".Trim();
                r.DeletedAt = DateTime.Now;
                r.DeletedBy = _currentUserContext.CurrentUserFullName;
                _auditLogService.LogDelete(db, "Recipient", r.Id, snapshot);
            }

            await db.SaveChangesAsync();
        }
    }
}
