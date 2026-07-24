namespace GenDoc.Services
{
    public sealed record ExportColumn<T>(string Header, Func<T, object?> Selector);

    public sealed record ExportResult(bool Success, int RowCount, string? FilePath, string? ErrorMessage);

    public interface IExportService
    {
        Task<ExportResult> ExportToXlsxAsync<T>(
            IEnumerable<T> items,
            IReadOnlyList<ExportColumn<T>> columns,
            string filePath,
            string sheetName = "Аркуш1");
    }
}
