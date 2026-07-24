using GenDoc.Data;
using GenDoc.Models;

namespace GenDoc.Services
{
    public class AuditLogService : IAuditLogService
    {
        private readonly ICurrentUserContext _currentUserContext;

        public AuditLogService(ICurrentUserContext currentUserContext)
        {
            _currentUserContext = currentUserContext;
        }

        public void LogCreate(AppDbContext db, string entityName, int entityId, string? newValue = null, string? details = null)
            => Add(db, "Створено", entityName, entityId, null, newValue, details);

        public void LogUpdate(AppDbContext db, string entityName, int entityId, string? oldValue, string? newValue, string? details = null)
            => Add(db, "Оновлено", entityName, entityId, oldValue, newValue, details);

        public void LogDelete(AppDbContext db, string entityName, int entityId, string? oldValue = null, string? details = null)
            => Add(db, "Видалено", entityName, entityId, oldValue, null, details);

        public void LogExport(AppDbContext db, string entityName, int count, string? details = null)
            => Add(db, "Експортовано", entityName, 0, null, null, details ?? $"{count} записів");

        public void LogImport(AppDbContext db, string entityName, int count, string? details = null)
            => Add(db, "Імпортовано", entityName, 0, null, null, details ?? $"{count} записів");

        public void LogGenerate(AppDbContext db, string entityName, int entityId, string? details = null)
            => Add(db, "Згенеровано", entityName, entityId, null, null, details);

        private void Add(AppDbContext db, string action, string entityName, int entityId, string? oldValue, string? newValue, string? details)
        {
            db.AuditLog.Add(new AuditLogEntry
            {
                OccurredAt = DateTime.Now,
                UserProfileId = _currentUserContext.CurrentUserId,
                UserProfileName = _currentUserContext.CurrentUserFullName ?? "Невідомий профіль",
                Action = action,
                EntityName = entityName,
                EntityId = entityId,
                OldValue = oldValue,
                NewValue = newValue,
                Details = details
            });
        }
    }
}
