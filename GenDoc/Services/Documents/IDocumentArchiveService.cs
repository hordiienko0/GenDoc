namespace GenDoc.Services.Documents
{
    public interface IDocumentArchiveService
    {
        Task<List<ArchiveRowDto>> QueryAsync(ArchiveFilter filter);
        Task<ArchiveStats> GetStatsAsync(ArchiveFilter filter);
        Task<ArchiveFilterOptions> GetFilterOptionsAsync();

        Task OpenAsync(int documentId);
        Task<ArchiveOpResult> SaveAsAsync(int documentId, string targetPath);
        Task<(int Saved, List<string> Errors)> SaveManyAsync(IReadOnlyList<int> documentIds, string targetFolder);

        Task<List<string>> GetManualTagsAsync(IReadOnlyList<int> templateIds);
        Task<ArchiveOpResult> RegenerateAsync(int documentId, Dictionary<string, string> manualValues);

        Task<ArchiveOpResult> UploadManualAsync(int documentId, string filePath, string? note);
        Task<ArchiveOpResult> AttachAsync(int documentId, string filePath, string? note);

        Task<ArchiveRowDto?> GetCurrentRowAsync(int recipientId, int templateId);
        Task OpenAttachmentAsync(int attachmentId);
        Task<ArchiveOpResult> SaveAttachmentAsAsync(int attachmentId, string targetPath);
        Task<List<DocumentVersionDto>> GetVersionsAsync(int recipientId, int templateId);
        Task<List<AttachmentDto>> GetAttachmentsAsync(int documentId);
        Task DeleteAttachmentAsync(int attachmentId);
        Task<int> MakeCurrentAsync(int versionDocumentId);

        Task DeleteAsync(IReadOnlyList<int> documentIds);
        Task<List<DeletedDocumentInfo>> GetDeletedDocumentsAsync();
        Task RestoreAsync(int documentId);

        Task<List<RunDto>> GetRunsAsync(int? intakeId, int? year);
        Task<List<RunItemDto>> GetRunItemsAsync(int runId);
    }
}
