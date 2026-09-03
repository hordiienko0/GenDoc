namespace GenDoc.Services.Import;

public interface IImportService
{
    ImportParseResult ParseFile(string filePath);

    List<ImportRowPreview> Validate(ImportParseResult parsed, ImportTarget? target = null);

    ImportSummary Import(
        ImportParseResult parsed, ImportTarget? target = null, IReadOnlyCollection<int>? moveRowNumbers = null);
}
