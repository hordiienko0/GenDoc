using GenDoc.Models.Enums;

namespace GenDoc.Services.Templates
{
    public record UploadResult(bool Success, string? ErrorMessage);

    public interface ITemplateService
    {
        UploadResult Upload(string filePath);

        List<(int Id, string Name, string? ShortName, string OriginalFileName, DateTime UploadedAt, int TagCount, bool IsFromBuilder, TemplateAudience Audience, TemplateKind Kind)> GetTemplateListItems();
        void SaveShortName(int templateId, string? shortName);

        void SaveAudience(int templateId, TemplateAudience audience);
        List<(int Id, string PlaceholderTag, MappingSourceType SourceType, string? FieldName, string? DateFormat)> GetMappings(int templateId);
        void SaveMappings(int templateId, List<(int Id, MappingSourceType SourceType, string? FieldName, string? DateFormat)> mappings);
        (bool Success, string? ErrorMessage) Delete(int templateId);
    }
}
