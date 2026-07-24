namespace GenDoc.Services.Generation
{
    public record RunResult(int Generated, int Skipped, int Errors);

    public interface IGenerationService
    {
        List<(int Id, string Name, string? Description, int TemplateCount)> GetPackages();
        List<(int Id, string Name)> GetAllTemplates();
        void CreatePackage(string name, string? description, List<int> templateIds);
        void DeletePackage(int packageId);
        List<(int TemplateId, string TemplateName)> GetPackageTemplates(int packageId);
        List<string> GetManualTags(int packageId);
        int GetRecipientCount();

        RunResult RunPackage(
            int packageId,
            string outputFolder,
            Dictionary<string, string> manualValues,
            bool regenerateExisting,
            IProgress<string> progress);
    }
}
