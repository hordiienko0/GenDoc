using GenDoc.Models.Enums;

namespace GenDoc.Services.Templates
{
    public record UploadResult(bool Success, string? ErrorMessage);

    public interface ITemplateService
    {
        UploadResult Upload(string filePath);

        List<(int Id, string Name, string? ShortName, string OriginalFileName, DateTime UploadedAt, int TagCount)> GetTemplateListItems();
        void SaveShortName(int templateId, string? shortName);
        List<(int Id, string PlaceholderTag, MappingSourceType SourceType, string? FieldName, string? DateFormat)> GetMappings(int templateId);
        void SaveMappings(int templateId, List<(int Id, MappingSourceType SourceType, string? FieldName, string? DateFormat)> mappings);
        (bool Success, string? ErrorMessage) Delete(int templateId);
    }
}
