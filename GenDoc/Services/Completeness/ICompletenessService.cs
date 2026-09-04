using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Documents;

namespace GenDoc.Services.Completeness
{
    public record MatrixTemplateInfo(
        int LinkId, int TemplateId, string Name, string? ShortName, int SortOrder,
        TemplateRequirement RequirementRegular, TemplateRequirement RequirementLimited,
        bool IsGroup = false, bool IsExport = false);

    public record PackageGroupDocumentStatus(
        int TemplateId, string TemplateName, int? GroupDocumentId, int Version,
        bool IsParticipant = false, bool RosterUnknown = false);

    public record MatrixDocDto(
        int Id, int RecipientId, int TemplateId, int Version, bool HasContent, bool IsStale,
        DocumentSourceType SourceType, bool IsGroup = false, bool RosterUnknown = false,
        bool IsExport = false);

    public record MatrixData(
        List<Recipient> People,
        List<MatrixTemplateInfo> Templates,
        Dictionary<(int RecipientId, int TemplateId, bool IsExport), MatrixDocDto> Docs,
        bool DetectStale);

    public record RequirementRow(
        int? LinkId, int TemplateId, TemplateRequirement RequirementRegular,
        TemplateRequirement RequirementLimited, int SortOrder);

    public record RecipientDocStatus(
        int TemplateId, string TemplateName, int? DocumentId, int Version, bool HasContent, bool IsStale,
        TemplateRequirement Requirement = TemplateRequirement.Required);

    public interface ICompletenessService
    {
        Task<MatrixData> BuildAsync(int intakeId, int packageId);
        Task<(int Percent, int IncompletePeople, int RequiredCells, int SatisfiedCells)> GetIntakeSummaryAsync(int intakeId, int packageId);
        Task<MatrixDocDto?> GetCellAsync(int recipientId, int templateId);
        Task<ArchiveOpResult> GenerateForPairAsync(
            int recipientId, int templateId, Dictionary<string, string> manualValues, int? courseOfficerId = null);
        Task<(int People, int Files, List<string> Warnings)> ExportPackagesAsync(
            IReadOnlyList<int> recipientIds, int packageId, string targetFolder);
        Task<int> GetBadgeCountAsync();

        Task<(int Missing, int Stale)> GetBadgeBreakdownAsync();
        Task<int?> GetDefaultPackageIdAsync();
        Task<int?> GetDefaultPackageIdAsync(int intakeId);

        Task<List<RecipientDocStatus>> GetRecipientStatusAsync(int recipientId, int packageId);
        Task<List<PackageGroupDocumentStatus>> GetPackageGroupDocumentsAsync(int packageId, int? intakeId, int? recipientId = null);
        Task<(int Generated, int Skipped, List<string> Errors)> GenerateMissingForRecipientAsync(
            int recipientId, int packageId, Dictionary<string, string> manualValues);

        Task<List<MatrixTemplateInfo>> GetPackageLinksAsync(int packageId);
        Task<List<(int Id, string Name)>> GetTemplatesNotInPackageAsync(int packageId);
        Task<List<int>> GetTemplateIdsWithDocumentsAsync(IReadOnlyList<int> templateIds);
        Task SaveRequirementsAsync(int packageId, IReadOnlyList<RequirementRow> rows);

        static TemplateRequirement Resolve(MatrixTemplateInfo template, string? fitnessCategory)
            => FitnessCategoryHelper.IsRegular(fitnessCategory)
                ? template.RequirementRegular
                : template.RequirementLimited;
    }
}
