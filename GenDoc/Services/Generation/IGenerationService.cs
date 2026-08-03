using GenDoc.Models.Enums;

namespace GenDoc.Services.Generation
{
    public record RunResult(int Generated, int Skipped, int Errors, int GroupGenerated, int GroupSkipped, int GroupErrors);

    public interface IGenerationService
    {
        List<(int Id, string Name, string? Description, int TemplateCount)> GetPackages();
        List<(int Id, string Name)> GetAllTemplates();
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
        int GetRecipientCount();
        int GetRecipientCount(FitnessFilter filter);

        RunResult RunPackage(
            int packageId,
            string outputFolder,
            Dictionary<string, string> manualValues,
            bool regenerateExisting,
            IProgress<string> progress);
    }
}
