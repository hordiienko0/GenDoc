using GenDoc.Models;

namespace GenDoc.Services.Generation
{
    public record GenerationItemResult(bool Success, string? ErrorMessage, List<string> UnfilledTags);

    public interface IDocumentGenerationService
    {
        GenerationItemResult GenerateOne(Template template, byte[] content, IDictionary<string, string> values, string outputPath);

        GenerationItemResult GenerateGroup(
            Template template,
            byte[] content,
            IReadOnlyList<IDictionary<string, string>> perRecipientValues,
            IDictionary<string, string> sharedValues,
            string outputPath);
    }
}
