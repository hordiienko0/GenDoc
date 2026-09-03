using GenDoc.Models;

namespace GenDoc.Services.Generation
{
    public record XlsxGenerationResult(bool Success, byte[]? Content, string? ErrorMessage, IReadOnlyList<string> UnfilledTags);

    public interface IXlsxGenerationService
    {
        XlsxGenerationResult Generate(
            byte[] templateContent,
            int templateRowIndex,
            bool usesPlaceholders,
            List<ExportTemplateColumnMapping> mappings,
            IReadOnlyList<Recipient> roster,
            OrganizationSettings? org,
            IDictionary<string, string> manualValues,
            bool repeatSheetPerDate = false,
            string? courseOfficerSignature = null);
    }
}
