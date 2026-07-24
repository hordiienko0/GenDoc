namespace GenDoc.Services.Audit
{
    public record AuditLogListItem(string TimeDisplay, string Profile, string Action, string ObjectDisplay, string ChangeDisplay);

    public record AuditLogFilter(DateOnly? From, DateOnly? To, string? Profile, string? Action, int Skip, int Take);

    public interface IAuditLogQueryService
    {
        List<AuditLogListItem> Query(AuditLogFilter filter);
        List<string> GetProfiles();
        List<string> GetActions();
    }
}
