namespace GenDoc.Services.Documents
{
    public interface IDocumentArchiveService
    {
        Task<List<ArchiveRowDto>> QueryAsync(ArchiveFilter filter);

        Task<ArchiveStats> GetStatsAsync(ArchiveFilter filter);
        Task<ArchiveFilterOptions> GetFilterOptionsAsync();

        Task<ArchiveOpResult> OpenAsync(int documentId);
        Task<ArchiveOpResult> PrintAsync(int documentId);
        Task<ArchiveOpResult> SaveAsAsync(int documentId, string targetPath);
        Task<(int Saved, List<string> Errors)> SaveManyAsync(IReadOnlyList<int> documentIds, string targetFolder);

        Task<List<string>> GetManualTagsAsync(IReadOnlyList<int> templateIds);
        Task<ArchiveOpResult> RegenerateAsync(
            int documentId, Dictionary<string, string> manualValues, int? courseOfficerId = null);

        Task<ArchiveOpResult> UploadManualAsync(int documentId, string filePath, string? note);
        Task<ArchiveOpResult> AttachAsync(int documentId, string filePath, string? note);

        Task<ArchiveRowDto?> GetCurrentRowAsync(int recipientId, int templateId);
        Task<ArchiveOpResult> OpenAttachmentAsync(int attachmentId);
        Task<ArchiveOpResult> SaveAttachmentAsAsync(int attachmentId, string targetPath);
        Task<List<DocumentVersionDto>> GetVersionsAsync(int recipientId, int templateId);
        Task<List<AttachmentDto>> GetAttachmentsAsync(int recipientId, int templateId);
        Task DeleteAttachmentAsync(int attachmentId);
        Task<List<DeletedAttachmentInfo>> GetDeletedAttachmentsAsync();
        Task RestoreAttachmentAsync(int attachmentId);
        Task<int> MakeCurrentAsync(int versionDocumentId);

        Task<int> CountVersionsAsync(IReadOnlyList<int> documentIds);
        Task DeleteAsync(IReadOnlyList<int> documentIds, bool allVersions = false);
        Task<List<DeletedDocumentInfo>> GetDeletedDocumentsAsync();
        Task RestoreAsync(int documentId);

        Task<List<RunDto>> GetRunsAsync(int? intakeId, int? year, int? userId = null);
        Task<List<RunItemDto>> GetRunItemsAsync(int runId);

        Task<List<GroupDocumentRowDto>> QueryGroupAsync(GroupArchiveFilter filter);
        Task<List<GroupTemplateOption>> GetGroupTemplateOptionsAsync();
        Task<ArchiveOpResult> OpenGroupAsync(int groupDocumentId);
        Task<ArchiveOpResult> PrintGroupAsync(int groupDocumentId);

        Task<List<GroupParticipantDto>> GetGroupParticipantsAsync(int groupDocumentId);
        Task<ArchiveOpResult> SaveGroupAsAsync(int groupDocumentId, string targetPath);
        Task DeleteGroupAsync(IReadOnlyList<int> groupDocumentIds);
        Task<List<GroupVersionDto>> GetGroupVersionsAsync(int? exportTemplateId, int? docxTemplateId, int? intakeId);
        Task<int> MakeGroupCurrentAsync(int versionDocumentId);
        Task<List<DeletedGroupDocumentInfo>> GetDeletedGroupDocumentsAsync();
        Task RestoreGroupAsync(int groupDocumentId);
    }
}
