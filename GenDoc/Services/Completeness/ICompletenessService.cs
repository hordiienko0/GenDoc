using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Documents;

namespace GenDoc.Services.Completeness
{
    /// <summary>Колонка матриці. IsGroup - один документ на весь склад
    /// (клітинка каже, чи людина в ньому), а не документ на людину.
    ///
    /// IsExport розрізняє ТАБЛИЦЮ, на яку вказує TemplateId: ExportTemplate
    /// (відомість Excel) чи Template (документ Word). Лічильники в них
    /// незалежні, тож без цієї ознаки відомість і шаблон з однаковим Id ділили
    /// б одну клітинку.</summary>
    public record MatrixTemplateInfo(
        int LinkId, int TemplateId, string Name, string? ShortName, int SortOrder,
        TemplateRequirement RequirementRegular, TemplateRequirement RequirementLimited,
        bool IsGroup = false, bool IsExport = false);

    // Групові відомості пакета (один документ на весь склад) - показуються в підвалі
    // картки особи, а не серед її персональних документів. IsParticipant/RosterUnknown
    // мають сенс, коли передано recipientId (участь конкретної людини, v25).
    public record PackageGroupDocumentStatus(
        int TemplateId, string TemplateName, int? GroupDocumentId, int Version,
        bool IsParticipant = false, bool RosterUnknown = false);

    // IsGroup - Id вказує на GeneratedGroupDocument (участь людини в групповому наказі),
    // RosterUnknown - документ згенеровано до v25, складу не записано.
    public record MatrixDocDto(
        int Id, int RecipientId, int TemplateId, int Version, bool HasContent, bool IsStale,
        DocumentSourceType SourceType, bool IsGroup = false, bool RosterUnknown = false,
        bool IsExport = false);

    /// <summary>Ключ клітинки містить IsExport з тієї самої причини, що й
    /// MatrixTemplateInfo: Template.Id і ExportTemplate.Id - різні лічильники.</summary>
    public record MatrixData(
        List<Recipient> People,
        List<MatrixTemplateInfo> Templates,
        Dictionary<(int RecipientId, int TemplateId, bool IsExport), MatrixDocDto> Docs,
        bool DetectStale);

    public record RequirementRow(
        int? LinkId, int TemplateId, TemplateRequirement RequirementRegular,
        TemplateRequirement RequirementLimited, int SortOrder);

    // Стан одного шаблону пакета для однієї людини - картка «Особовий склад» → вкладка «Документи».
    //
    // Requirement резолвиться за категорією придатності КОНКРЕТНОЇ людини. Без
    // нього картка вважала бракуючим і генерувала шаблон, позначений «не
    // потрібен» для обмежено придатних, - документ лягав в архів, а в матриці
    // його не було видно взагалі (аудит 2026-08-28).
    public record RecipientDocStatus(
        int TemplateId, string TemplateName, int? DocumentId, int Version, bool HasContent, bool IsStale,
        TemplateRequirement Requirement = TemplateRequirement.Required);

    public interface ICompletenessService
    {
        Task<MatrixData> BuildAsync(int intakeId, int packageId);
        Task<(int Percent, int IncompletePeople, int RequiredCells, int SatisfiedCells)> GetIntakeSummaryAsync(int intakeId, int packageId);
        Task<MatrixDocDto?> GetCellAsync(int recipientId, int templateId);
        Task<ArchiveOpResult> GenerateForPairAsync(int recipientId, int templateId, Dictionary<string, string> manualValues);
        Task<(int People, int Files, List<string> Warnings)> ExportPackagesAsync(
            IReadOnlyList<int> recipientIds, int packageId, string targetFolder);
        Task<int> GetBadgeCountAsync();

        /// <summary>Ті самі два числа окремо: бейдж навігації їх складає, домашня
        /// картка показує роздільно - «бракує» і «застарілих» це різні дії.</summary>
        Task<(int Missing, int Stale)> GetBadgeBreakdownAsync();
        Task<int?> GetDefaultPackageIdAsync();

        Task<List<RecipientDocStatus>> GetRecipientStatusAsync(int recipientId, int packageId);
        Task<List<PackageGroupDocumentStatus>> GetPackageGroupDocumentsAsync(int packageId, int? intakeId, int? recipientId = null);
        Task<(int Generated, int Skipped, List<string> Errors)> GenerateMissingForRecipientAsync(
            int recipientId, int packageId, Dictionary<string, string> manualValues);

        Task<List<MatrixTemplateInfo>> GetPackageLinksAsync(int packageId);
        Task<List<(int Id, string Name)>> GetTemplatesNotInPackageAsync(int packageId);
        Task<List<int>> GetTemplateIdsWithDocumentsAsync(IReadOnlyList<int> templateIds);
        Task SaveRequirementsAsync(int packageId, IReadOnlyList<RequirementRow> rows);

        // придатний → RequirementRegular; обмежено придатний і непридатний → RequirementLimited.
        // Порівняння - через FitnessCategoryHelper, єдину точку в проєкті: власне
        // ordinal-порівняння з рядком робило «Придатний» з великої «обмежено
        // придатним» у матриці, тоді як фільтри генерації вважали його придатним
        // (аудит 2026-08-28).
        static TemplateRequirement Resolve(MatrixTemplateInfo template, string? fitnessCategory)
            => FitnessCategoryHelper.IsRegular(fitnessCategory)
                ? template.RequirementRegular
                : template.RequirementLimited;
    }
}
