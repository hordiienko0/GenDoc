namespace GenDoc.Services
{
    public interface IExportTemplateService
    {
        void EnsureBuiltInTemplate();
        List<(int Id, string Name)> GetTemplates();

        List<(int Id, string Name, string OriginalFileName, DateTime UploadedAt, bool IsBuiltIn)> GetTemplateListItems();
        List<(int ColumnIndex, string HeaderText, string FieldKey)> GetMappings(int templateId);
        void SaveMappings(int templateId, List<(int ColumnIndex, string FieldKey)> mappings);
        void UploadTemplate(string filePath);
        void Delete(int templateId);
    }
}
