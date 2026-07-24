using GenDoc.Data;

namespace GenDoc.Services
{
    public interface IAuditLogService
    {
        void LogCreate(AppDbContext db, string entityName, int entityId, string? newValue = null, string? details = null);
        void LogUpdate(AppDbContext db, string entityName, int entityId, string? oldValue, string? newValue, string? details = null);
        void LogDelete(AppDbContext db, string entityName, int entityId, string? oldValue = null, string? details = null);
        void LogExport(AppDbContext db, string entityName, int count, string? details = null);
        void LogImport(AppDbContext db, string entityName, int count, string? details = null);
        void LogGenerate(AppDbContext db, string entityName, int entityId, string? details = null);
    }
}
