using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Data;
using GenDoc.Models;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services.Import;

public class ImportService : IImportService
{
    private static readonly string[] DateFormats = { "dd.MM.yyyy", "d.M.yyyy" };

    // Кімнати, яких ще нема в базі, під час імпорту створюються "наосліп" (лише
    // за корпусом/номером з файлу) — реальну місткість тоді ніхто не вказує,
    // тож ставимо стандартну на 6 місць замість 1.
    private const int DefaultImportedRoomCapacity = 6;

    private static readonly StringComparer UkIgnoreCase =
        StringComparer.Create(CultureInfo.GetCultureInfo("uk-UA"), ignoreCase: true);

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IAuditLogService _auditLogService;

    public ImportService(IDbContextFactory<AppDbContext> dbFactory, IAuditLogService auditLogService)
    {
        _dbFactory = dbFactory;
        _auditLogService = auditLogService;
    }

    public ImportParseResult ParseFile(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheets.First();
        var result = new ImportParseResult { FilePath = filePath };

        var usedRange = worksheet.RangeUsed();
        if (usedRange is null) return result;

        var allRows = usedRange.RowsUsed().ToList();
        if (allRows.Count == 0) return result;

        var headerRow = allRows[0];
        var dataRows = allRows.Skip(1).ToList();
        var columnCount = usedRange.ColumnCount();

        var rawRows = new List<string?[]>();
        foreach (var dataRow in dataRows)
        {
            var values = new string?[columnCount];
            for (var c = 1; c <= columnCount; c++)
            {
                values[c - 1] = GetCellText(dataRow.Cell(c));
            }
            rawRows.Add(values);
        }

        var columns = new List<ImportColumn>();
        var noteColumnAssigned = false;
        for (var c = 1; c <= columnCount; c++)
        {
            var header = headerRow.Cell(c).GetString().Trim();
            var example = rawRows.Select(r => r[c - 1]).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
            var mappedField = AutoMapHeader(header, ref noteColumnAssigned);
            var column = new ImportColumn(c - 1, header, example)
            {
                MappedField = mappedField
            };
            columns.Add(column);
        }

        result.Columns = columns;
        result.RawRows = rawRows;
        result.TotalRows = rawRows.Count;
        return result;
    }

    public List<ImportRowPreview> Validate(ImportParseResult parsed)
    {
        using var db = _dbFactory.CreateDbContext();

        var rows = ParseRows(parsed);
        var existingServiceNumbers = LoadExistingServiceNumbers(db);
        var orgNodeIntakeMap = LoadOrgNodeIntakeMap(db);
        var existingNameKeys = LoadExistingNameKeys(db);
        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenNameKeysInFile = new HashSet<string>(StringComparer.Ordinal);

        var previews = new List<ImportRowPreview>();
        foreach (var row in rows)
        {
            var intakeId = ResolveIntakeId(orgNodeIntakeMap, row.Fields.UnitName);
            var (status, note) = EvaluateRow(row.Fields, row.IncompleteFullName, row.CourseArrivalDateInvalid,
                existingServiceNumbers, seenInFile, intakeId, existingNameKeys, seenNameKeysInFile);
            previews.Add(new ImportRowPreview
            {
                RowNumber = row.RowNumber,
                FullNameDisplay = BuildFullNameDisplay(row.Fields),
                RankDisplay = row.Fields.Rank,
                UnitDisplay = row.Fields.UnitName,
                Status = status,
                Note = note
            });
        }

        return previews;
    }

    public ImportSummary Import(ImportParseResult parsed, bool importAsPermanentStaff = false)
    {
        var rows = ParseRows(parsed);
        using var db = _dbFactory.CreateDbContext();

        var existingServiceNumbers = LoadExistingServiceNumbers(db);
        var orgNodeIntakeMap = LoadOrgNodeIntakeMap(db);
        var existingNameKeys = LoadExistingNameKeys(db);
        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenNameKeysInFile = new HashSet<string>(StringComparer.Ordinal);
        var unitCache = new Dictionary<string, Unit>(UkIgnoreCase);
        var orgNodeCache = new Dictionary<string, OrgNode>(UkIgnoreCase);
        var roomCache = new Dictionary<(string Building, string Number), Room>();
        var intakeFolderCache = new Dictionary<int, Dictionary<string, int>>();

        var imported = 0;
        var skipped = 0;
        var errors = 0;
        var errorMessages = new List<string>();

        foreach (var row in rows)
        {
            var intakeId = importAsPermanentStaff ? null : ResolveIntakeId(orgNodeIntakeMap, row.Fields.UnitName);
            var (status, _) = EvaluateRow(row.Fields, row.IncompleteFullName, row.CourseArrivalDateInvalid,
                existingServiceNumbers, seenInFile, intakeId, existingNameKeys, seenNameKeysInFile);
            if (status is ImportRowStatus.Error or ImportRowStatus.Duplicate)
            {
                skipped++;
                continue;
            }

            try
            {
                var unit = ResolveUnit(db, unitCache, row.Fields.UnitName);
                var orgNode = ResolveOrgNode(db, orgNodeCache, row.Fields.UnitName);
                var room = ResolveRoom(db, roomCache, row.Fields.Building, row.Fields.RoomNumber);

                // "Це постійний склад" — людина не належить жодному набору,
                // навіть якщо підрозділ з файлу технічно прив'язаний до набору.
                var resolvedIntakeId = importAsPermanentStaff ? null : orgNode?.IntakeId;
                var fitnessCategory = ParseFitnessCategory(row.Fields.FitnessRaw);

                // Якщо людина потрапляє в набір — розкласти її по «Придатні»/
                // «Обмежено придатні»/«Всі» замість того вузла, куди її поставило
                // саме лише зіставлення підрозділу. Старі набори без цих трьох
                // папок — лишаємо як є, без падіння.
                var resolvedOrgNodeId = orgNode?.Id;
                if (resolvedIntakeId is int intakeIdForRouting)
                {
                    var folderId = ResolveIntakeFitnessFolderId(db, intakeFolderCache, intakeIdForRouting, fitnessCategory);
                    if (folderId is int fid) resolvedOrgNodeId = fid;
                }

                var recipient = new Recipient
                {
                    LastName = row.Fields.LastName,
                    FirstName = row.Fields.FirstName,
                    MiddleName = string.IsNullOrWhiteSpace(row.Fields.MiddleName) ? null : row.Fields.MiddleName,
                    Rank = row.Fields.Rank,
                    Position = row.Fields.Position,
                    ServiceNumber = row.Fields.ServiceNumber,
                    DateOfBirth = row.Fields.DateOfBirth,
                    UnitId = unit?.Id,
                    OrgNodeId = resolvedOrgNodeId,
                    IntakeId = resolvedIntakeId,
                    RoomId = room?.Id,
                    FitnessCategory = fitnessCategory,

                    Nationality = NullIfEmpty(row.Fields.Nationality),
                    Vos = NullIfEmpty(row.Fields.Vos),
                    CourseArrivalDate = row.Fields.CourseArrivalDate,
                    MaritalStatus = NullIfEmpty(row.Fields.MaritalStatus),
                    RegistrationAddress = NullIfEmpty(row.Fields.RegistrationAddress),
                    ResidenceAddress = NullIfEmpty(row.Fields.ResidenceAddress),
                    Phone = NullIfEmpty(row.Fields.Phone),
                    Note = NullIfEmpty(row.Fields.Note),
                    GroupName = NullIfEmpty(row.Fields.GroupName),
                    NameTransliterated = NullIfEmpty(row.Fields.NameTransliterated),
                    ServedBefore = NullIfEmpty(row.Fields.ServedBefore),
                    ExtraNote = NullIfEmpty(row.Fields.ExtraNote),
                    CommanderContact = NullIfEmpty(row.Fields.CommanderContact),
                    TravelCertificateNumber = NullIfEmpty(row.Fields.TravelCertificateNumber),
                    FoodCertificate = NullIfEmpty(row.Fields.FoodCertificate),
                    IdDocumentNumber = NullIfEmpty(row.Fields.IdDocumentNumber),
                    MedicalBoard = NullIfEmpty(row.Fields.MedicalBoard),
                    MedicalBoardConclusion = NullIfEmpty(row.Fields.MedicalBoardConclusion),
                    OriginUnit = NullIfEmpty(row.Fields.OriginUnit),
                    Vehicle = NullIfEmpty(row.Fields.Vehicle)
                };

                // Зброя — через навігаційну колекцію, до єдиного SaveChanges: рядок
                // зберігається "все або нічого". Два окремі SaveChanges лишали людину
                // в базі без її зброї, якщо друга вставка падала.
                foreach (var (name, serialNumber, rawText) in ParseWeaponUnits(row.Fields.WeaponRaw))
                {
                    recipient.Weapons.Add(new Weapon
                    {
                        Name = name,
                        SerialNumber = serialNumber,
                        RawText = rawText
                    });
                }

                db.Recipients.Add(recipient);
                db.SaveChanges();

                imported++;
            }
            catch (Exception ex)
            {
                // Невдалий SaveChanges лишає сутності в ChangeTracker у стані Added,
                // і тоді КОЖЕН наступний SaveChanges падає на них знову — один битий
                // рядок валив увесь подальший імпорт. Від'єднуємо незбережене.
                // Кеші unitCache/orgNodeCache/roomCache від цього не страждають:
                // Resolve* роблять SaveChanges одразу, тож їхні сутності вже Unchanged.
                foreach (var entry in db.ChangeTracker.Entries()
                             .Where(e => e.State != EntityState.Unchanged).ToList())
                {
                    entry.State = EntityState.Detached;
                }

                errors++;
                errorMessages.Add($"Рядок {row.RowNumber}: {ex.GetBaseException().Message}");
            }
        }

        if (imported > 0)
        {
            _auditLogService.LogImport(db, "Recipient", imported, $"з файлу {Path.GetFileName(parsed.FilePath)}");
            db.SaveChanges();
            WeakReferenceMessenger.Default.Send(new CountsChangedMessage());
        }

        return new ImportSummary(imported, skipped, errors) { ErrorMessages = errorMessages };
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? GetCellText(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;

        if (cell.DataType == XLDataType.DateTime)
            return cell.GetDateTime().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

        var text = cell.GetString().Trim();
        return text.Length == 0 ? null : text;
    }

    private static ImportTargetField AutoMapHeader(string header, ref bool noteColumnAssigned)
    {
        var normalized = HeaderNormalization.Normalize(header);

        if (normalized.Contains("№ з/п")) return ImportTargetField.NotImported;

        if (normalized.Contains("іноземній мові")) return ImportTargetField.NameTransliterated;
        if (normalized.Contains("піб")) return ImportTargetField.FullName;

        if (normalized.Contains("прізвище")) return ImportTargetField.LastName;
        if (normalized.Contains("по батькові")) return ImportTargetField.MiddleName;
        if (normalized.Contains("імя")) return ImportTargetField.FirstName;

        if (normalized.Contains("звання")) return ImportTargetField.Rank;
        if (normalized.Contains("національність")) return ImportTargetField.Nationality;
        if (normalized.Contains("вос")) return ImportTargetField.Vos;
        if (normalized.Contains("прибув на курси")) return ImportTargetField.CourseArrivalDate;
        if (normalized.Contains("сімейний стан")) return ImportTargetField.MaritalStatus;
        if (normalized.Contains("адреса реєстрації")) return ImportTargetField.RegistrationAddress;
        if (normalized.Contains("фактичного проживання")) return ImportTargetField.ResidenceAddress;

        if (normalized.Contains("командир")) return ImportTargetField.CommanderContact;
        if (normalized.Contains("телефон")) return ImportTargetField.Phone;

        if (normalized.Contains("примітка"))
        {
            if (noteColumnAssigned) return ImportTargetField.ExtraNote;
            noteColumnAssigned = true;
            return ImportTargetField.Note;
        }

        if (normalized.Contains("група")) return ImportTargetField.GroupName;
        if (normalized.Contains("служив")) return ImportTargetField.ServedBefore;

        if (normalized.Contains("посвідчення про відрядження")) return ImportTargetField.TravelCertificateNumber;
        if (normalized.Contains("прод")) return ImportTargetField.FoodCertificate;
        if (normalized.Contains("посвідчення офіцера") || normalized.Contains("військового квитка"))
            return ImportTargetField.IdDocumentNumber;

        if (normalized.Contains("висновок влк")) return ImportTargetField.MedicalBoardConclusion;
        if (normalized.Contains("влк")) return ImportTargetField.MedicalBoard;

        if (normalized.Contains("з якої військової частини")) return ImportTargetField.OriginUnit;

        if (normalized.Contains("посада")) return ImportTargetField.Position;
        if (normalized.Contains("автомобіль")) return ImportTargetField.Vehicle;

        // "висновок" вище за "придатн" навмисно — інакше "Висновок ВЛК" міг би
        // перехопитися тут, якщо колись міститиме слово "придатний" у заголовку.
        if (normalized.Contains("зброї") || normalized.Contains("зброя")) return ImportTargetField.Weapon;
        if (normalized.Contains("придатн")) return ImportTargetField.Fitness;

        if (normalized.Contains("підрозділ")) return ImportTargetField.Unit;
        if (normalized.Contains("особовий номер")) return ImportTargetField.ServiceNumber;
        if (normalized.Contains("дата народження")) return ImportTargetField.DateOfBirth;
        if (normalized.Contains("корпус")) return ImportTargetField.Building;
        if (normalized.Contains("кімната")) return ImportTargetField.RoomNumber;

        return ImportTargetField.NotImported;
    }

    private static List<RowInfo> ParseRows(ImportParseResult parsed)
    {
        var result = new List<RowInfo>();

        for (var i = 0; i < parsed.RawRows.Count; i++)
        {
            var raw = parsed.RawRows[i];
            var values = new Dictionary<ImportTargetField, string?>();
            foreach (var column in parsed.Columns)
            {
                if (column.MappedField == ImportTargetField.NotImported) continue;
                var value = column.ColumnIndex < raw.Length ? raw[column.ColumnIndex] : null;
                values[column.MappedField] = value?.Trim();
            }

            var fields = new RowFields();
            var incompleteFullName = false;

            if (values.TryGetValue(ImportTargetField.FullName, out var fullName) && !string.IsNullOrWhiteSpace(fullName))
            {
                var parts = fullName.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    fields.LastName = parts[0];
                    fields.FirstName = parts[1];
                    fields.MiddleName = string.Join(' ', parts[2..]);
                }
                else if (parts.Length == 2)
                {
                    fields.LastName = parts[0];
                    fields.FirstName = parts[1];
                }
                else if (parts.Length == 1)
                {
                    fields.LastName = parts[0];
                    fields.FirstName = string.Empty;
                    incompleteFullName = true;
                }
            }
            else
            {
                fields.LastName = values.GetValueOrDefault(ImportTargetField.LastName) ?? string.Empty;
                fields.FirstName = values.GetValueOrDefault(ImportTargetField.FirstName) ?? string.Empty;
                fields.MiddleName = values.GetValueOrDefault(ImportTargetField.MiddleName);
            }

            fields.Rank = values.GetValueOrDefault(ImportTargetField.Rank) ?? string.Empty;
            fields.Position = values.GetValueOrDefault(ImportTargetField.Position) ?? string.Empty;
            fields.UnitName = values.GetValueOrDefault(ImportTargetField.Unit) ?? string.Empty;
            fields.ServiceNumber = values.GetValueOrDefault(ImportTargetField.ServiceNumber) ?? string.Empty;
            fields.Building = values.GetValueOrDefault(ImportTargetField.Building) ?? string.Empty;
            fields.RoomNumber = values.GetValueOrDefault(ImportTargetField.RoomNumber) ?? string.Empty;

            fields.Nationality = values.GetValueOrDefault(ImportTargetField.Nationality) ?? string.Empty;
            fields.Vos = values.GetValueOrDefault(ImportTargetField.Vos) ?? string.Empty;
            fields.MaritalStatus = values.GetValueOrDefault(ImportTargetField.MaritalStatus) ?? string.Empty;
            fields.RegistrationAddress = values.GetValueOrDefault(ImportTargetField.RegistrationAddress) ?? string.Empty;
            fields.ResidenceAddress = values.GetValueOrDefault(ImportTargetField.ResidenceAddress) ?? string.Empty;
            fields.Phone = values.GetValueOrDefault(ImportTargetField.Phone) ?? string.Empty;
            fields.Note = values.GetValueOrDefault(ImportTargetField.Note) ?? string.Empty;
            fields.GroupName = values.GetValueOrDefault(ImportTargetField.GroupName) ?? string.Empty;
            fields.NameTransliterated = values.GetValueOrDefault(ImportTargetField.NameTransliterated) ?? string.Empty;
            fields.ServedBefore = values.GetValueOrDefault(ImportTargetField.ServedBefore) ?? string.Empty;
            fields.ExtraNote = values.GetValueOrDefault(ImportTargetField.ExtraNote) ?? string.Empty;
            fields.CommanderContact = values.GetValueOrDefault(ImportTargetField.CommanderContact) ?? string.Empty;
            fields.TravelCertificateNumber = values.GetValueOrDefault(ImportTargetField.TravelCertificateNumber) ?? string.Empty;
            fields.FoodCertificate = values.GetValueOrDefault(ImportTargetField.FoodCertificate) ?? string.Empty;
            fields.IdDocumentNumber = values.GetValueOrDefault(ImportTargetField.IdDocumentNumber) ?? string.Empty;
            fields.MedicalBoard = values.GetValueOrDefault(ImportTargetField.MedicalBoard) ?? string.Empty;
            fields.MedicalBoardConclusion = values.GetValueOrDefault(ImportTargetField.MedicalBoardConclusion) ?? string.Empty;
            fields.OriginUnit = values.GetValueOrDefault(ImportTargetField.OriginUnit) ?? string.Empty;
            fields.Vehicle = values.GetValueOrDefault(ImportTargetField.Vehicle) ?? string.Empty;
            fields.WeaponRaw = values.GetValueOrDefault(ImportTargetField.Weapon) ?? string.Empty;
            fields.FitnessRaw = values.GetValueOrDefault(ImportTargetField.Fitness);

            fields.DateOfBirthRaw = values.GetValueOrDefault(ImportTargetField.DateOfBirth);
            if (!string.IsNullOrWhiteSpace(fields.DateOfBirthRaw) &&
                DateOnly.TryParseExact(fields.DateOfBirthRaw, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dob))
            {
                fields.DateOfBirth = dob;
            }

            var courseArrivalDateRaw = values.GetValueOrDefault(ImportTargetField.CourseArrivalDate);
            var courseArrivalDateInvalid = false;
            if (!string.IsNullOrWhiteSpace(courseArrivalDateRaw))
            {
                if (DateOnly.TryParseExact(courseArrivalDateRaw, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var arrivalDate))
                    fields.CourseArrivalDate = arrivalDate;
                else
                    courseArrivalDateInvalid = true;
            }

            result.Add(new RowInfo(i + 2, fields, incompleteFullName, courseArrivalDateInvalid));
        }

        return result;
    }

    private static (ImportRowStatus Status, string Note) EvaluateRow(
        RowFields fields, bool incompleteFullName, bool courseArrivalDateInvalid,
        HashSet<string> existingServiceNumbers, HashSet<string> seenInFile,
        int? intakeId, HashSet<string> existingNameKeys, HashSet<string> seenNameKeysInFile)
    {
        if (string.IsNullOrWhiteSpace(fields.LastName) && string.IsNullOrWhiteSpace(fields.FirstName))
            return (ImportRowStatus.Error, "Порожнє поле ПІБ");

        if (!string.IsNullOrWhiteSpace(fields.DateOfBirthRaw) && fields.DateOfBirth is null)
            return (ImportRowStatus.Error, "Некоректна дата народження");

        var serviceNumber = fields.ServiceNumber.Trim();
        var nameCheckedInstead = false;
        if (serviceNumber.Length > 0)
        {
            if (existingServiceNumbers.Contains(serviceNumber))
                return (ImportRowStatus.Duplicate, "Вже є в базі — рядок пропущено");

            if (!seenInFile.Add(serviceNumber))
                return (ImportRowStatus.Duplicate, "Дублюється в файлі — рядок пропущено");
        }
        else
        {
            // Без особового номера єдиний спосіб відсіяти дубль — ПІБ + дата
            // народження в межах того самого набору (в іншому наборі однакове
            // ПІБ — це не обов'язково та сама людина).
            var nameKey = BuildNameKey(intakeId, fields.LastName, fields.FirstName, fields.MiddleName, fields.DateOfBirth);
            if (existingNameKeys.Contains(nameKey))
                return (ImportRowStatus.Duplicate, "Схожий запис (ПІБ і дата народження) вже є в наборі — рядок пропущено");

            if (!seenNameKeysInFile.Add(nameKey))
                return (ImportRowStatus.Duplicate, "Дублюється в файлі — рядок пропущено");

            nameCheckedInstead = true;
        }

        if (incompleteFullName)
            return (ImportRowStatus.Warning, "Неповне ПІБ");

        if (courseArrivalDateInvalid)
            return (ImportRowStatus.Warning, "Некоректна дата прибуття — поле пропущено");

        if (string.IsNullOrWhiteSpace(fields.RoomNumber))
            return (ImportRowStatus.Warning, "Немає поля «Кімната» — додасться без розміщення");

        if (nameCheckedInstead)
            return (ImportRowStatus.Warning, "Без особового номера — дубль перевірено за ПІБ і датою народження");

        return (ImportRowStatus.Ok, string.Empty);
    }

    private static string BuildNameKey(int? intakeId, string lastName, string firstName, string? middleName, DateOnly? dateOfBirth)
    {
        var normalizedName = string.Join(' ', new[] { lastName, firstName, middleName }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p!.Trim()))
            .ToUpper(CultureInfo.GetCultureInfo("uk-UA"));
        var intakePart = intakeId?.ToString(CultureInfo.InvariantCulture) ?? "-";
        var dobPart = dateOfBirth?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-";
        return $"{intakePart}|{normalizedName}|{dobPart}";
    }

    private static int? ResolveIntakeId(Dictionary<string, int?> orgNodeIntakeMap, string unitName)
    {
        var trimmed = unitName.Trim();
        return trimmed.Length > 0 && orgNodeIntakeMap.TryGetValue(trimmed, out var intakeId) ? intakeId : null;
    }

    // Порядок перевірок навмисний: "обмежено придатний" містить "придатний",
    // тому "обмежено" мусить перевірятись першим.
    private static string? ParseFitnessCategory(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var normalized = raw.Trim().ToLower(CultureInfo.GetCultureInfo("uk-UA"));

        if (normalized.Contains("обмежено")) return "обмежено придатний";
        if (normalized.Contains("неприда")) return "непридатний";
        if (normalized.Contains("придатн")) return "придатний";
        return null;
    }

    // «Придатні»/«Обмежено придатні»/«Всі» — фіксовані назви папок усередині
    // набору (Intakes.IntakeFolderNames). Старі набори, створені до цієї
    // структури, їх не мають — тоді повертає null, і виклик лишає людину
    // там, куди її поставило зіставлення підрозділу.
    private static int? ResolveIntakeFitnessFolderId(
        AppDbContext db, Dictionary<int, Dictionary<string, int>> cache, int intakeId, string? fitnessCategory)
    {
        if (!cache.TryGetValue(intakeId, out var folders))
        {
            folders = db.OrgNodes
                .Where(n => n.IntakeId == intakeId &&
                    (n.Name == Intakes.IntakeFolderNames.Fit ||
                     n.Name == Intakes.IntakeFolderNames.LimitedFit ||
                     n.Name == Intakes.IntakeFolderNames.All))
                .ToDictionary(n => n.Name, n => n.Id);
            cache[intakeId] = folders;
        }

        var targetName = fitnessCategory switch
        {
            "придатний" => Intakes.IntakeFolderNames.Fit,
            "обмежено придатний" => Intakes.IntakeFolderNames.LimitedFit,
            _ => Intakes.IntakeFolderNames.All
        };

        return folders.TryGetValue(targetName, out var id) ? id : null;
    }

    private static readonly System.Text.RegularExpressions.Regex WeaponPrefixRegex = new(
        @"(?:^|(?<=\s))(АКМС|АКС|АКМ|АК|ПМ)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    // Розбиває сирий рядок «Найменування, серія та номер особистої зброї» на
    // окремі одиниці за появою нового найменування (АК/АКС/АКМ/АКМС/ПМ...).
    // Якщо рядок не починається з розпізнаваного найменування — розбір
    // непевний, повертаємо один запис із усім рядком у Name, без втрат.
    internal static List<(string Name, string SerialNumber, string RawText)> ParseWeaponUnits(string? raw)
    {
        var result = new List<(string Name, string SerialNumber, string RawText)>();
        var trimmedRaw = raw?.Trim() ?? string.Empty;
        if (trimmedRaw.Length == 0) return result;

        var matches = WeaponPrefixRegex.Matches(trimmedRaw);
        if (matches.Count == 0 || matches[0].Index != 0)
        {
            result.Add((trimmedRaw, string.Empty, trimmedRaw));
            return result;
        }

        var starts = matches.Select(m => m.Index).ToList();
        for (var i = 0; i < starts.Count; i++)
        {
            var start = starts[i];
            var end = i + 1 < starts.Count ? starts[i + 1] : trimmedRaw.Length;
            var segment = trimmedRaw[start..end].Trim();
            if (segment.Length == 0) continue;

            var markerIndex = segment.IndexOfAny(new[] { '№', '#' });
            if (markerIndex >= 0)
            {
                var name = segment[..markerIndex].Trim();
                var serial = segment[(markerIndex + 1)..].Trim();
                result.Add((name, serial, segment));
            }
            else
            {
                result.Add((segment, string.Empty, segment));
            }
        }

        return result.Count > 0 ? result : new List<(string, string, string)> { (trimmedRaw, string.Empty, trimmedRaw) };
    }

    private static string BuildFullNameDisplay(RowFields fields)
        => string.Join(' ', new[] { fields.LastName, fields.FirstName, fields.MiddleName }
            .Where(p => !string.IsNullOrWhiteSpace(p)));

    private static Dictionary<string, int?> LoadOrgNodeIntakeMap(AppDbContext db)
    {
        var nodes = db.OrgNodes
            .Select(n => new { n.Name, n.IntakeId })
            .ToList();

        var map = new Dictionary<string, int?>(UkIgnoreCase);
        foreach (var node in nodes)
        {
            var key = node.Name.Trim();
            if (key.Length > 0)
                map[key] = node.IntakeId;
        }

        return map;
    }

    private static HashSet<string> LoadExistingNameKeys(AppDbContext db)
    {
        var recipients = db.Recipients
            .Select(r => new { r.IntakeId, r.LastName, r.FirstName, r.MiddleName, r.DateOfBirth })
            .ToList();

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in recipients)
            keys.Add(BuildNameKey(r.IntakeId, r.LastName, r.FirstName, r.MiddleName, r.DateOfBirth));

        return keys;
    }

    private static HashSet<string> LoadExistingServiceNumbers(AppDbContext db)
    {
        var numbers = db.Recipients
            .Where(r => r.ServiceNumber != null && r.ServiceNumber != "")
            .Select(r => r.ServiceNumber)
            .ToList();

        return new HashSet<string>(numbers.Select(n => n.Trim()), StringComparer.OrdinalIgnoreCase);
    }

    private static Unit? ResolveUnit(AppDbContext db, Dictionary<string, Unit> cache, string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return null;

        if (cache.TryGetValue(trimmed, out var cached)) return cached;

        var existing = db.Units.AsEnumerable().FirstOrDefault(u => UkIgnoreCase.Equals(u.Name, trimmed));
        if (existing is not null)
        {
            cache[trimmed] = existing;
            return existing;
        }

        var created = new Unit { Name = trimmed };
        db.Units.Add(created);
        db.SaveChanges();
        cache[trimmed] = created;
        return created;
    }

    // Прив'язка до дерева підрозділів: вузол з назвою підрозділу шукається серед
    // живих, за відсутності — створюється під коренем. Порожній підрозділ → корінь.
    private static OrgNode? ResolveOrgNode(AppDbContext db, Dictionary<string, OrgNode> cache, string name)
    {
        var root = db.OrgNodes.OrderBy(n => n.Depth).ThenBy(n => n.Id).FirstOrDefault(n => n.ParentId == null);
        if (root is null) return null;

        var trimmed = name.Trim();
        if (trimmed.Length == 0) return root;

        if (cache.TryGetValue(trimmed, out var cached)) return cached;

        var existing = db.OrgNodes.AsEnumerable().FirstOrDefault(n => UkIgnoreCase.Equals(n.Name, trimmed));
        if (existing is not null)
        {
            cache[trimmed] = existing;
            return existing;
        }

        var maxSort = db.OrgNodes.Where(n => n.ParentId == root.Id)
            .Select(n => (int?)n.SortOrder).Max() ?? -1;
        var created = new OrgNode
        {
            Name = trimmed,
            ParentId = root.Id,
            Depth = root.Depth + 1,
            SortOrder = maxSort + 1
        };
        db.OrgNodes.Add(created);
        db.SaveChanges();
        created.Path = $"{root.Path}{created.Id}/";
        db.SaveChanges();

        cache[trimmed] = created;
        return created;
    }

    private static Room? ResolveRoom(AppDbContext db, Dictionary<(string Building, string Number), Room> cache, string building, string number)
    {
        var trimmedNumber = number.Trim();
        if (trimmedNumber.Length == 0) return null;

        var trimmedBuilding = building.Trim();
        var key = (trimmedBuilding, trimmedNumber);
        if (cache.TryGetValue(key, out var cached)) return cached;

        var existing = db.Rooms.FirstOrDefault(r => r.Building == trimmedBuilding && r.Number == trimmedNumber);
        if (existing is not null)
        {
            cache[key] = existing;
            return existing;
        }

        var created = new Room { Building = trimmedBuilding, Number = trimmedNumber, Capacity = DefaultImportedRoomCapacity };
        db.Rooms.Add(created);
        db.SaveChanges();
        cache[key] = created;
        return created;
    }

    private class RowFields
    {
        public string LastName = string.Empty;
        public string FirstName = string.Empty;
        public string? MiddleName;
        public string Rank = string.Empty;
        public string Position = string.Empty;
        public string UnitName = string.Empty;
        public string ServiceNumber = string.Empty;
        public string? DateOfBirthRaw;
        public DateOnly? DateOfBirth;
        public string Building = string.Empty;
        public string RoomNumber = string.Empty;

        public string Nationality = string.Empty;
        public string Vos = string.Empty;
        public DateOnly? CourseArrivalDate;
        public string MaritalStatus = string.Empty;
        public string RegistrationAddress = string.Empty;
        public string ResidenceAddress = string.Empty;
        public string Phone = string.Empty;
        public string Note = string.Empty;
        public string GroupName = string.Empty;
        public string NameTransliterated = string.Empty;
        public string ServedBefore = string.Empty;
        public string ExtraNote = string.Empty;
        public string CommanderContact = string.Empty;
        public string TravelCertificateNumber = string.Empty;
        public string FoodCertificate = string.Empty;
        public string IdDocumentNumber = string.Empty;
        public string MedicalBoard = string.Empty;
        public string MedicalBoardConclusion = string.Empty;
        public string OriginUnit = string.Empty;
        public string Vehicle = string.Empty;
        public string WeaponRaw = string.Empty;
        public string? FitnessRaw;
    }

    private record RowInfo(int RowNumber, RowFields Fields, bool IncompleteFullName, bool CourseArrivalDateInvalid);
}
