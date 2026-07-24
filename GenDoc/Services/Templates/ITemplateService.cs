using GenDoc.Models.Enums;

namespace GenDoc.Services.Templates
{
    public record UploadResult(bool Success, string? ErrorMessage);

    public interface ITemplateService
    {
        UploadResult Upload(string filePath);

        List<(int Id, string Name, string OriginalFileName, DateTime UploadedAt, int TagCount)> GetTemplateListItems();
        List<(int Id, string PlaceholderTag, MappingSourceType SourceType, string? FieldName)> GetMappings(int templateId);
        void SaveMappings(int templateId, List<(int Id, MappingSourceType SourceType, string? FieldName)> mappings);
        (bool Success, string? ErrorMessage) Delete(int templateId);
    }
}
