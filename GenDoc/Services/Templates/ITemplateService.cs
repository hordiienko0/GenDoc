using GenDoc.Models.Enums;

namespace GenDoc.Services.Templates
{
    public record UploadResult(bool Success, string? ErrorMessage, int TemplateId = 0);

    public interface ITemplateService
    {
        UploadResult Upload(string filePath);

        List<(int Id, string Name, string? ShortName, string OriginalFileName, DateTime UploadedAt, int TagCount, bool IsFromBuilder, TemplateAudience Audience, TemplateKind Kind, StaffEventKind? StaffEvent)> GetTemplateListItems();
        void SaveShortName(int templateId, string? shortName);

        void SaveAudience(int templateId, TemplateAudience audience);

        /// <summary>Прив'язує шаблон до події постійного складу (або знімає прив'язку).</summary>
        void SaveStaffEvent(int templateId, StaffEventKind? staffEvent);
        List<(int Id, string PlaceholderTag, MappingSourceType SourceType, string? FieldName, string? DateFormat)> GetMappings(int templateId);
        void SaveMappings(int templateId, List<(int Id, MappingSourceType SourceType, string? FieldName, string? DateFormat)> mappings);
        (bool Success, string? ErrorMessage) Delete(int templateId);
    }
}
