namespace GenDoc.Services.Audit
{
    public record AuditLogListItem(
        string TimeDisplay, string Profile, string Action, string ObjectDisplay, string ChangeDisplay, string ActionKind = "other");

    public record AuditLogFilter(DateOnly? From, DateOnly? To, string? Profile, string? Action, int Skip, int Take, string? Search = null);

    public interface IAuditLogQueryService
    {
        List<AuditLogListItem> Query(AuditLogFilter filter);
        List<string> GetProfiles();
        List<string> GetActions();
    }
}
