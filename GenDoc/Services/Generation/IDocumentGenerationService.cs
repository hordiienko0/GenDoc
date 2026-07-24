using GenDoc.Models;

namespace GenDoc.Services.Generation
{
    public record GenerationItemResult(bool Success, string? ErrorMessage, List<string> UnfilledTags);

    public interface IDocumentGenerationService
    {
        // values — словник {{мітка}} (з фігурними дужками, як у TemplateFieldMapping.PlaceholderTag) → значення.
        GenerationItemResult GenerateOne(Template template, byte[] content, IDictionary<string, string> values, string outputPath);
    }
}
