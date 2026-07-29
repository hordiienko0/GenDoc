using GenDoc.Models.Enums;

namespace GenDoc.Services.Documents
{
    public record ArchiveFilter(
        int? IntakeId,
        int? TemplateId,
        int? PackageId,
        int? UserId,
        int? Year,
        int Skip,
        int Take);

    public record ArchiveRowDto(
        int Id,
        int RecipientId,
        int TemplateId,
        string LastName,
        string? FirstName,
        string? MiddleName,
        string TemplateName,
        bool TemplateAlive,
        int Version,
        int? IntakeId,
        int? IntakeNumber,
        string? OrgPathSnapshot,
        DateTime CreatedAt,
        string Author,
        int AttachmentCount,
        bool HasContent,
        DocumentSourceType SourceType,
        string FileName,
        long SizeBytes);

    public record ArchiveStats(int Count, long TotalBytes);

    public record ArchiveFilterOptions(
        List<(int Id, string Label)> Intakes,
        List<(int Id, string Name)> Templates,
        List<(int Id, string Name)> Packages,
        List<(int Id, string Name)> Authors,
        List<int> Years);

    public record DocumentVersionDto(
        int Id,
        int Version,
        DateTime CreatedAt,
        string Author,
        long SizeBytes,
        DocumentSourceType SourceType,
        bool IsCurrent,
        bool HasContent,
        string FileName);

    public record AttachmentDto(int Id, string FileName, DateTime UploadedAt, string? Note, long SizeBytes);

    public record RunDto(
        int Id,
        DateTime RunAt,
        string PackageName,
        int? IntakeNumber,
        string? BranchName,
        int GeneratedCount,
        int SkippedCount,
        int ErrorCount);

    public record RunItemDto(string Person, string TemplateName, string Status, bool IsError, long SizeBytes, int? DocumentId, bool HasContent, string FileName);

    public record DeletedDocumentInfo(int Id, string Person, string TemplateName, int Version, DateTime DeletedAt, string? DeletedBy);

    public record ArchiveOpResult(bool Success, string? ErrorMessage);
}
