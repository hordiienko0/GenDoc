using GenDoc.Models;

namespace GenDoc.Services.Generation
{
    public record XlsxGenerationResult(bool Success, byte[]? Content, string? ErrorMessage, IReadOnlyList<string> UnfilledTags);

    // Спільне ядро заповнення XLSX-шаблону (за тегами або за заголовками колонок) —
    // використовується і меню експорту на Особовому складі (один запуск = один файл
    // для довільного списку), і Phase B пакетної генерації (той самий рендер, інший викликач).
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
