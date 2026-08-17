using GenDoc.Models.Enums;

namespace GenDoc.Services.Documents
{
    /// <summary>FolderPath — обрана гілка дерева. Це префікс збереженого
    /// відносного шляху, а не окреме поле: дерево й список читають ОДИН рядок,
    /// тому розійтися не можуть.</summary>
    public record ArchiveFilter(
        int? IntakeId,
        int? TemplateId,
        int? PackageId,
        int? UserId,
        int? Year,
        int Skip,
        int Take,
        string? FolderPath = null);

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

    public record GroupArchiveFilter(int? ExportTemplateId, int? DocxTemplateId, int? Year, int Skip, int Take);

    // Ідентифікатори шаблонів XLSX і DOCX живуть у різних таблицях з незалежною
    // нумерацією, тож саме число нічого не каже про вид документа — вид має
    // їхати разом з ним, інакше фільтр знайде чужу відомість.
    public record GroupTemplateOption(int? ExportTemplateId, int? DocxTemplateId, string Name);

    public record GroupDocumentRowDto(
        int Id,
        int ExportTemplateId,
        int? DocxTemplateId,
        string TemplateName,
        bool TemplateAlive,
        int Version,
        int RecipientCount,
        DateTime GeneratedAt,
        string Author,
        bool HasContent,
        string FileName,
        long SizeBytes);

    public record GroupVersionDto(
        int Id, int Version, DateTime CreatedAt, string Author, long SizeBytes,
        bool IsCurrent, bool HasContent, string FileName, int RecipientCount);

    public record DeletedGroupDocumentInfo(int Id, string TemplateName, int Version, int RecipientCount, DateTime DeletedAt, string? DeletedBy);
}
