using GenDoc.Models.Enums;
using GenDoc.Services;

namespace GenDoc.Services.Generation
{
    public record RunResult(
        int Generated, int Skipped, int Errors,
        int GroupGenerated, int GroupSkipped, int GroupErrors,
        int DocxGroupGenerated, int DocxGroupSkipped, int DocxGroupErrors);

    /// <summary>Що показати на порожньому боці екрана генерації. Теки виводу
    /// тут навмисно немає: у прогоні вона не зберігається, та й повторний
    /// запуск переписує документи - такого в один клік бути не повинно.</summary>
    public record LastRunInfo(
        int PackageId, string PackageName, DateTime RunAt,
        int GeneratedCount, int SkippedCount, int ErrorCount);

    // AllRecipients=true - весь особовий склад (RecipientIds ігнорується);
    // AllRecipients=false - лише RecipientIds. FitnessFilter, PermanentStaffOnly,
    // RankCategories і Ranks застосовуються завжди, поверх будь-якого з двох варіантів,
    // усі фільтри комбінуються через AND. Порожні RankCategories/Ranks = без обмеження.
    public sealed record RosterSelection(
        bool AllRecipients,
        IReadOnlyList<int> RecipientIds,
        FitnessFilter FitnessFilter,
        bool PermanentStaffOnly,
        IReadOnlyList<RankCategory> RankCategories,
        IReadOnlyList<string> Ranks)
    {
        public static RosterSelection Everyone { get; } =
            new(true, Array.Empty<int>(), FitnessFilter.All, false, Array.Empty<RankCategory>(), Array.Empty<string>());
    }

    public interface IGenerationService
    {
        List<(int Id, string Name, string? Description, int TemplateCount)> GetPackages();
        /// <summary>Аудиторія розділяє видимість шаблонів; замовчування Intake -
        /// звичайна генерація й пакети постійного складу не бачать.</summary>
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

        /// <summary>Останній запуск - щоб порожній бік екрана показував, що
        /// саме запускали минулого разу, а не самий лише напис «оберіть пакет».
        /// null, якщо запусків не було або пакет уже видалили.</summary>
        LastRunInfo? GetLastRun();
        int GetRecipientCount();
        int GetRecipientCount(FitnessFilter filter);

        RunResult RunPackage(
            int packageId,
            string outputFolder,
            Dictionary<string, string> manualValues,
            bool regenerateExisting,
            RosterSelection rosterSelection,
            IProgress<string> progress,
            int? courseOfficerId = null);
    }
}
