namespace GenDoc.Services
{
    public interface IExportTemplateService
    {
        void EnsureBuiltInTemplate();
        List<(int Id, string Name)> GetTemplates();
    }
}
