namespace GenDoc.Services.Import;

public enum ImportTargetField
{
    NotImported,
    FullName,
    LastName,
    FirstName,
    MiddleName,
    Rank,
    Position,
    Unit,
    ServiceNumber,
    DateOfBirth,
    Building,
    RoomNumber
}

public static class ImportTargetFieldNames
{
    public static readonly IReadOnlyDictionary<ImportTargetField, string> DisplayNames = new Dictionary<ImportTargetField, string>
    {
        [ImportTargetField.NotImported] = "— не імпортувати —",
        [ImportTargetField.FullName] = "ПІБ",
        [ImportTargetField.LastName] = "Прізвище",
        [ImportTargetField.FirstName] = "Ім'я",
        [ImportTargetField.MiddleName] = "По батькові",
        [ImportTargetField.Rank] = "Звання",
        [ImportTargetField.Position] = "Посада",
        [ImportTargetField.Unit] = "Підрозділ",
        [ImportTargetField.ServiceNumber] = "Особовий номер",
        [ImportTargetField.DateOfBirth] = "Дата народження",
        [ImportTargetField.Building] = "Корпус",
        [ImportTargetField.RoomNumber] = "Кімната"
    };
}

public class ImportColumn
{
    public ImportColumn(int columnIndex, string header, string exampleValue)
    {
        ColumnIndex = columnIndex;
        Header = header;
        ExampleValue = exampleValue;
    }

    public int ColumnIndex { get; }
    public string Header { get; }
    public string ExampleValue { get; }
    public ImportTargetField MappedField { get; set; } = ImportTargetField.NotImported;
}

public enum ImportRowStatus
{
    Ok,
    Warning,
    Error,
    Duplicate
}

public class ImportRowPreview
{
    public int RowNumber { get; set; }
    public string FullNameDisplay { get; set; } = string.Empty;
    public string RankDisplay { get; set; } = string.Empty;
    public string UnitDisplay { get; set; } = string.Empty;
    public ImportRowStatus Status { get; set; }
    public string Note { get; set; } = string.Empty;
}

public class ImportParseResult
{
    public string FilePath { get; set; } = string.Empty;
    public List<ImportColumn> Columns { get; set; } = new();
    public List<string?[]> RawRows { get; set; } = new();
    public int TotalRows { get; set; }
}

public record ImportSummary(int Imported, int Skipped, int Errors);
