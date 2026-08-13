namespace GenDoc.Services.Import;

public interface IImportService
{
    ImportParseResult ParseFile(string filePath);

    /// <summary>Ціль впливає й на перевірку, а не лише на запис: дубль шукається
    /// в межах набору, тож для обраного набору відповідь інша, ніж для того, що
    /// виводиться з файлу.</summary>
    List<ImportRowPreview> Validate(ImportParseResult parsed, ImportTarget? target = null);

    ImportSummary Import(ImportParseResult parsed, ImportTarget? target = null);
}
