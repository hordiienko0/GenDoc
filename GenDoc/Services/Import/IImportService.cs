namespace GenDoc.Services.Import;

public interface IImportService
{
    ImportParseResult ParseFile(string filePath);

    List<ImportRowPreview> Validate(ImportParseResult parsed);

    ImportSummary Import(ImportParseResult parsed);
}
