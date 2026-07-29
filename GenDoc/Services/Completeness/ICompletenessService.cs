using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Documents;

namespace GenDoc.Services.Completeness
{
    public record MatrixTemplateInfo(
        int LinkId, int TemplateId, string Name, string? ShortName, int SortOrder,
        TemplateRequirement RequirementRegular, TemplateRequirement RequirementLimited);

    public record MatrixDocDto(
        int Id, int RecipientId, int TemplateId, int Version, bool HasContent, bool IsStale,
        DocumentSourceType SourceType);

    public record MatrixData(
        List<Recipient> People,
        List<MatrixTemplateInfo> Templates,
        Dictionary<(int RecipientId, int TemplateId), MatrixDocDto> Docs,
        bool DetectStale);

    public record RequirementRow(
        int? LinkId, int TemplateId, TemplateRequirement RequirementRegular,
        TemplateRequirement RequirementLimited, int SortOrder);

    public interface ICompletenessService
    {
        Task<MatrixData> BuildAsync(int intakeId, int packageId);
        Task<MatrixDocDto?> GetCellAsync(int recipientId, int templateId);
        Task<ArchiveOpResult> GenerateForPairAsync(int recipientId, int templateId, Dictionary<string, string> manualValues);
        Task<(int People, int Files, List<string> Warnings)> ExportPackagesAsync(
            IReadOnlyList<int> recipientIds, int packageId, string targetFolder);
        Task<int> GetBadgeCountAsync();
        Task<int?> GetDefaultPackageIdAsync();

        Task<List<MatrixTemplateInfo>> GetPackageLinksAsync(int packageId);
        Task<List<(int Id, string Name)>> GetTemplatesNotInPackageAsync(int packageId);
        Task<List<int>> GetTemplateIdsWithDocumentsAsync(IReadOnlyList<int> templateIds);
        Task SaveRequirementsAsync(int packageId, IReadOnlyList<RequirementRow> rows);

        // придатний → RequirementRegular; обмежено придатний і непридатний → RequirementLimited.
        static TemplateRequirement Resolve(MatrixTemplateInfo template, string? fitnessCategory)
            => fitnessCategory == "придатний" || string.IsNullOrWhiteSpace(fitnessCategory)
                ? template.RequirementRegular
                : template.RequirementLimited;
    }
}
