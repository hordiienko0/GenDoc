namespace GenDoc.Services.Import;

public interface IImportService
{
    ImportParseResult ParseFile(string filePath);

    /// <summary>Ціль впливає й на перевірку, а не лише на запис: дубль шукається
    /// в межах набору, тож для обраного набору відповідь інша, ніж для того, що
    /// виводиться з файлу.</summary>
    List<ImportRowPreview> Validate(ImportParseResult parsed, ImportTarget? target = null);

    /// <summary>moveRowNumbers - рядки-дублі, які оператор позначив колонкою
    /// «ДІЯ»: замість пропуску вони пишуть у НАЯВНУ картку (переносять людину в
    /// цільовий набір і оновлюють непорожні поля). Порожній перелік лишає стару
    /// поведінку - дубль просто пропускається.</summary>
    ImportSummary Import(
        ImportParseResult parsed, ImportTarget? target = null, IReadOnlyCollection<int>? moveRowNumbers = null);
}
