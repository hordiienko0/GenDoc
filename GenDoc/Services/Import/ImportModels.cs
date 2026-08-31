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

    // Анкетні дані (прикомандировані)
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
    Fitness
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
        [ImportTargetField.Fitness] = "Категорія придатності"
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

    /// <summary>Картка, з якою зіткнувся рядок, якщо вона вже є в базі.
    /// Заповнена рівно для дублів, які МОЖНА перенести: «дублюється у файлі»
    /// такої картки не має, тож там колонка «ДІЯ» лишається порожньою.
    /// Розрізняти дублі за текстом примітки не можна - тексти змінюються.</summary>
    public int? ExistingRecipientId { get; set; }

    /// <summary>Оператор позначив рядок до перенесення (колонка «ДІЯ»).
    /// Має сенс лише разом із ExistingRecipientId.</summary>
    public bool Move { get; set; }
}

/// <summary>Звідки береться набір, у який лягають імпортовані люди.</summary>
public enum ImportTargetKind
{
    /// <summary>Набір виводиться з колонки «Підрозділ» у файлі - так імпорт
    /// поводився до появи майстра, і так він поводиться, якщо ціль не задана.</summary>
    FromFile,

    /// <summary>Оператор обрав конкретний набір і гілку в ньому (крок «Набір і
    /// гілка»). Колонка «Підрозділ» тоді описує лише підрозділ людини, а не те,
    /// куди її класти.</summary>
    Intake,

    /// <summary>Постійний склад - поза наборами.</summary>
    PermanentStaff
}

/// <summary>Куди імпортувати. OrgNodeId - гілка всередині набору; null означає
/// корінь набору.</summary>
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

    /// <summary>Номер рядка в аркуші для кожного елемента <see cref="RawRows"/>.
    /// Порядковий номер для цього не годиться: RowsUsed() пропускає порожні
    /// рядки, тож «Рядок N» у звіті переставав збігатися з файлом. Порожній
    /// список означає «номерів немає» - тоді діє старий розрахунок.</summary>
    public List<int> RawRowNumbers { get; set; } = new();

    public int TotalRows { get; set; }
}

/// <summary>Moved - люди, чиї картки вже були в базі й яких оператор позначив
/// до перенесення. Це не Imported (нових рядків не з'явилось) і не Skipped
/// (рядок таки щось змінив), тож окремий лічильник.</summary>
public record ImportSummary(int Imported, int Skipped, int Errors, int Moved = 0)
{
    // Причини збоїв, а не лише їх кількість: анонімна "31 помилка" колись приховала
    // цілком конкретне 'no such column: w.RawText'.
    public List<string> ErrorMessages { get; init; } = new();
}
