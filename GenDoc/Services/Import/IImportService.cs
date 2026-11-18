namespace GenDoc.Services.Import;

public interface IImportService
{
    ImportParseResult ParseFile(string filePath);

    List<ImportRowPreview> Validate(ImportParseResult parsed, ImportTarget? target = null);

    /// <param name="rowActions">
    /// Для рядків, людина з яких уже є в базі: номер рядка → що зробити. Без запису рядок пропускається.
    /// </param>
    ImportSummary Import(
        ImportParseResult parsed, ImportTarget? target = null,
        IReadOnlyDictionary<int, ImportExistingAction>? rowActions = null);
}
