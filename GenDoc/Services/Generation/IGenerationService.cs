using GenDoc.Models.Enums;
using GenDoc.Services;

namespace GenDoc.Services.Generation
{
    public record RunResult(
        int Generated, int Skipped, int Errors,
        int GroupGenerated, int GroupSkipped, int GroupErrors,
        int DocxGroupGenerated, int DocxGroupSkipped, int DocxGroupErrors,
        int RunId = 0, IReadOnlyList<RunIssue>? Issues = null);

    public record LastRunInfo(
        int PackageId, string PackageName, DateTime RunAt,
        int GeneratedCount, int SkippedCount, int ErrorCount);

    public sealed record RosterSelection(
        bool AllRecipients,
        IReadOnlyList<int> RecipientIds,
        FitnessFilter FitnessFilter,
        bool PermanentStaffOnly,
        IReadOnlyList<RankCategory> RankCategories,
        IReadOnlyList<string> Ranks,
        int? IntakeId = null,
        bool IncludePermanentStaff = false)
    {
        public static RosterSelection Everyone { get; } =
            new(true, Array.Empty<int>(), FitnessFilter.All, false, Array.Empty<RankCategory>(), Array.Empty<string>());

        public static bool InScope(int? recipientIntakeId, int? intakeId, bool includePermanentStaff)
            => intakeId is not int id
               || recipientIntakeId == id
               || (includePermanentStaff && recipientIntakeId is null);

        public bool Covers(int? recipientIntakeId) => InScope(recipientIntakeId, IntakeId, IncludePermanentStaff);
    }

    public interface IGenerationService
    {
        List<(int Id, string Name, string? Description, int TemplateCount)> GetPackages();
        List<(int Id, string Name)> GetAllTemplates(
            Models.Enums.TemplateAudience audience = Models.Enums.TemplateAudience.Intake);

        List<(int Id, string Name)> GetPerRecipientTemplates(
            Models.Enums.TemplateAudience audience = Models.Enums.TemplateAudience.Intake);
        List<(int Id, string Name)> GetAllExportTemplates();

        void CreatePackage(
            string name, string? description, List<int> templateIds,
            List<(int ExportTemplateId, FitnessFilter Filter)> exportTemplates);

        void DeletePackage(int packageId);
        List<(int TemplateId, string TemplateName)> GetPackageTemplates(int packageId);

        List<(int LinkId, int ExportTemplateId, string Name, int SortOrder, FitnessFilter FitnessFilter)> GetPackageExportTemplates(int packageId);
        List<(int Id, string Name)> GetExportTemplatesNotInPackage(int packageId);
        void SaveExportTemplates(int packageId, List<(int? LinkId, int ExportTemplateId, int SortOrder, FitnessFilter FitnessFilter)> rows);

        List<string> GetManualTags(int packageId);
        bool PackageNeedsCourseOfficer(int packageId);

        LastRunInfo? GetLastRun();

        int GetRecipientCount(RosterSelection selection);

        RunResult RunPackage(
            int packageId,
            string outputFolder,
            Dictionary<string, string> manualValues,
            bool regenerateExisting,
            RosterSelection rosterSelection,
            IProgress<string> progress,
            int? courseOfficerId = null);

        RunResult GenerateTemplatesForRecipients(
            IReadOnlyList<int> templateIds,
            IReadOnlyList<int> exportTemplateIds,
            IReadOnlyList<int> recipientIds,
            string outputFolder,
            Dictionary<string, string> manualValues,
            IProgress<string> progress,
            int? courseOfficerId = null);

        List<string> GetManualTagsForTemplates(IReadOnlyList<int> templateIds, IReadOnlyList<int> exportTemplateIds);

        bool ExportTemplatesNeedCourseOfficer(IReadOnlyList<int> exportTemplateIds);

        bool TemplatesNeedCourseOfficer(IReadOnlyList<int> templateIds, IReadOnlyList<int> exportTemplateIds);
    }
}
