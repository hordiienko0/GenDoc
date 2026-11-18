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
    RoomNumber,

    Nationality,
    Vos,
    CourseArrivalDate,
    MaritalStatus,
    RegistrationAddress,
    ResidenceAddress,
    Phone,
    Note,
    GroupName,
    NameTransliterated,
    ServedBefore,
    ExtraNote,
    CommanderContact,
    TravelCertificateNumber,
    FoodCertificate,
    IdDocumentNumber,
    MedicalBoard,
    MedicalBoardConclusion,
    OriginUnit,
    Vehicle,
    Weapon,
    Fitness,

    CivilEducation,
    MilitaryEducation,
    ArmedForcesSince,
    ConscriptionOffice,
    TaxNumber,
    Passport,
    PlaceOfBirth,
    RankOrder
}

public static class ImportTargetFieldNames
{
    public static readonly IReadOnlyDictionary<ImportTargetField, string> DisplayNames = new Dictionary<ImportTargetField, string>
    {
        [ImportTargetField.NotImported] = "- не імпортувати -",
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
        [ImportTargetField.RoomNumber] = "Кімната",

        [ImportTargetField.Nationality] = "Національність",
        [ImportTargetField.Vos] = "ВОС на який навчається",
        [ImportTargetField.CourseArrivalDate] = "З якого часу прибув на курси П та ПК",
        [ImportTargetField.MaritalStatus] = "Сімейний стан",
        [ImportTargetField.RegistrationAddress] = "Адреса реєстрації",
        [ImportTargetField.ResidenceAddress] = "Адреса фактичного проживання",
        [ImportTargetField.Phone] = "Телефон",
        [ImportTargetField.Note] = "Примітка",
        [ImportTargetField.GroupName] = "Група",
        [ImportTargetField.NameTransliterated] = "ПІБ на іноземній мові",
        [ImportTargetField.ServedBefore] = "Служив/не служив",
        [ImportTargetField.ExtraNote] = "Примітка (2)",
        [ImportTargetField.CommanderContact] = "Командир (ПІП та телефон)",
        [ImportTargetField.TravelCertificateNumber] = "№ посвідчення про відрядження",
        [ImportTargetField.FoodCertificate] = "Прод атестат",
        [ImportTargetField.IdDocumentNumber] = "Номер посвідчення офіцера/військового квитка",
        [ImportTargetField.MedicalBoard] = "ВЛК, №, дата",
        [ImportTargetField.MedicalBoardConclusion] = "Висновок ВЛК",
        [ImportTargetField.OriginUnit] = "З якої частини прибув",
        [ImportTargetField.Vehicle] = "Автомобіль, номер авто",
        [ImportTargetField.Weapon] = "Особиста зброя (найменування, серія, номер)",
        [ImportTargetField.Fitness] = "Категорія придатності",

        [ImportTargetField.CivilEducation] = "Освіта цивільна",
        [ImportTargetField.MilitaryEducation] = "Освіта військова (ВОС)",
        [ImportTargetField.ArmedForcesSince] = "З якого часу в ЗСУ",
        [ImportTargetField.ConscriptionOffice] = "Яким РТЦК та СП призваний",
        [ImportTargetField.TaxNumber] = "ІПН",
        [ImportTargetField.Passport] = "Паспорт",
        [ImportTargetField.PlaceOfBirth] = "Місце народження",
        [ImportTargetField.RankOrder] = "Наказ про присвоєння в/звання"
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

/// <summary>Що зробити з рядком, людина з якого вже є в базі.</summary>
public enum ImportExistingAction
{
    /// <summary>Нічого не міняти - рядок пропускається.</summary>
    Skip,

    /// <summary>Оновити картку даними з файлу, людина лишається там, де є.</summary>
    Update,

    /// <summary>Перенести в обраний набір (чи постійний склад) і оновити дані.</summary>
    Move,

    /// <summary>Збіг лише за ПІБ (без підтвердження датою чи номером) - це інша людина, додати нову картку.</summary>
    AddAsNew
}

public static class ImportRowActions
{
    public static IReadOnlyDictionary<int, ImportExistingAction> MoveRows(params int[] rowNumbers)
        => rowNumbers.ToDictionary(n => n, _ => ImportExistingAction.Move);
}

/// <summary>Поле, у якому файл розходиться з наявною карткою.</summary>
public record ImportFieldDifference(string Field, string Current, string Incoming)
{
    public string Display => $"{Field}: {Show(Current)} → {Show(Incoming)}";

    private static string Show(string value) => string.IsNullOrWhiteSpace(value) ? "(порожньо)" : value;
}

public class ImportRowPreview
{
    public int RowNumber { get; set; }
    public string FullNameDisplay { get; set; } = string.Empty;
    public string RankDisplay { get; set; } = string.Empty;
    public string UnitDisplay { get; set; } = string.Empty;
    public ImportRowStatus Status { get; set; }
    public string Note { get; set; } = string.Empty;

    public int? ExistingRecipientId { get; set; }

    /// <summary>Де людина вже є: набір і папка дерева (або постійний склад).</summary>
    public string ExistingLocation { get; set; } = string.Empty;

    /// <summary>Поля, де у файлі не те, що в картці (порожні клітинки файлу відмінністю не вважаються).</summary>
    public List<ImportFieldDifference> Differences { get; set; } = new();

    public bool CanUpdate => ExistingRecipientId is not null && Differences.Count > 0;

    /// <summary>Людина в іншому наборі (чи в постійному складі), ніж той, куди імпортуємо.</summary>
    public bool CanMove { get; set; }

    /// <summary>Збіг лише за ПІБ - можливо, однофамілець, тож дозволяємо додати окремою карткою.</summary>
    public bool CanAddAsNew { get; set; }

    public ImportExistingAction Action { get; set; }
}

public enum ImportTargetKind
{
    FromFile,

    Intake,

    PermanentStaff
}

public record ImportTarget(
    ImportTargetKind Kind = ImportTargetKind.FromFile,
    int? IntakeId = null,
    int? OrgNodeId = null)
{
    public static ImportTarget FromFile { get; } = new(ImportTargetKind.FromFile);
}

public class ImportParseResult
{
    public string FilePath { get; set; } = string.Empty;
    public List<ImportColumn> Columns { get; set; } = new();
    public List<string?[]> RawRows { get; set; } = new();

    public List<int> RawRowNumbers { get; set; } = new();

    public int TotalRows { get; set; }
}

public record ImportSummary(int Imported, int Skipped, int Errors, int Moved = 0, int Updated = 0)
{
    public List<string> ErrorMessages { get; init; } = new();
}
