using GenDoc.Models;

namespace GenDoc.Services.Generation
{
    public record GenerationItemResult(bool Success, string? ErrorMessage, List<string> UnfilledTags);

    public interface IDocumentGenerationService
    {
        // values - словник {{мітка}} (з фігурними дужками, як у TemplateFieldMapping.PlaceholderTag) → значення.
        GenerationItemResult GenerateOne(Template template, byte[] content, IDictionary<string, string> values, string outputPath);

        // Груповий DOCX: один документ на весь список одержувачів. perRecipientValues -
        // по одному словнику на кожен елемент списку, у тому порядку, в якому вони мають
        // з'явитись у документі. sharedValues - теги поза повторюваним блоком (ручні/організаційні).
        GenerationItemResult GenerateGroup(
            Template template,
            byte[] content,
            IReadOnlyList<IDictionary<string, string>> perRecipientValues,
            IDictionary<string, string> sharedValues,
            string outputPath);
    }
}
