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

    public record CompletenessGaps(
        int MissingRequired, int MissingOptional, int Stale,
        int RequiredCells, int SatisfiedCells, int IncompletePeople);

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

        static bool IsApplicable(MatrixTemplateInfo template, string? fitnessCategory)
            => Resolve(template, fitnessCategory) != TemplateRequirement.NotApplicable;

        static CompletenessGaps CountGaps(MatrixData data, IReadOnlySet<int>? recipientIds = null)
        {
            var people = recipientIds is null
                ? data.People
                : data.People.Where(p => recipientIds.Contains(p.Id)).ToList();

            var missingRequired = 0;
            var missingOptional = 0;
            var stale = 0;
            var requiredCells = 0;
            var satisfiedCells = 0;
            var incompletePeople = 0;

            foreach (var person in people)
            {
                var personIncomplete = false;
                foreach (var template in data.Templates)
                {
                    var requirement = Resolve(template, person.FitnessCategory);
                    if (requirement == TemplateRequirement.NotApplicable) continue;

                    var hasDoc = data.Docs.TryGetValue((person.Id, template.TemplateId, template.IsExport), out var doc);
                    var present = hasDoc && !doc!.RosterUnknown;

                    if (requirement == TemplateRequirement.Required)
                    {
                        requiredCells++;
                        if (present && !doc!.IsStale) satisfiedCells++;
                        else personIncomplete = true;
                    }

                    if (template.IsGroup) continue;

                    if (!present)
                    {
                        if (requirement == TemplateRequirement.Required) missingRequired++;
                        else missingOptional++;
                    }
                    else if (doc!.IsStale)
                    {
                        stale++;
                    }
                }
                if (personIncomplete) incompletePeople++;
            }

            foreach (var column in data.Templates.Where(t => t.IsGroup))
            {
                var lacksRequired = false;
                var lacksOptional = false;
                var columnStale = false;
                foreach (var person in people)
                {
                    var requirement = Resolve(column, person.FitnessCategory);
                    if (requirement == TemplateRequirement.NotApplicable) continue;

                    var hasDoc = data.Docs.TryGetValue((person.Id, column.TemplateId, column.IsExport), out var doc);
                    if (!hasDoc || doc!.RosterUnknown)
                    {
                        if (requirement == TemplateRequirement.Required) lacksRequired = true;
                        else lacksOptional = true;
                    }
                    else if (doc.IsStale)
                    {
                        columnStale = true;
                    }
                }
                if (lacksRequired) missingRequired++;
                else if (lacksOptional) missingOptional++;
                if (columnStale) stale++;
            }

            return new CompletenessGaps(
                missingRequired, missingOptional, stale, requiredCells, satisfiedCells, incompletePeople);
        }
    }
}
