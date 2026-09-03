using GenDoc.Models.Enums;

namespace GenDoc.Services
{
    public interface IExportTemplateService
    {
        void EnsureBuiltInTemplate();
        List<(int Id, string Name)> GetTemplates();

        List<(int Id, string Name, string OriginalFileName, DateTime UploadedAt, bool IsBuiltIn, bool UsesPlaceholders, int TagCount, bool RepeatSheetPerDate, bool IsFromBuilder)> GetTemplateListItems();
        List<(int Id, int ColumnIndex, string HeaderText, string FieldKey, string PlaceholderTag, MappingSourceType SourceType)> GetMappings(int templateId);
        List<string> GetManualTags(int templateId);
        void SaveMappings(int templateId, List<(int ColumnIndex, string FieldKey)> mappings);
        void SavePlaceholderMappings(int templateId, List<(int Id, MappingSourceType SourceType, string? FieldKey)> mappings);
        void SetRepeatSheetPerDate(int templateId, bool value);
        void UploadTemplate(string filePath);
        void Delete(int templateId);
    }
}
