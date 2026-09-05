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

    private const int DefaultImportedRoomCapacity = 6;

    private static readonly StringComparer UkIgnoreCase = UkrainianCollation.IgnoreCase;

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
        var rawRowNumbers = new List<int>();
        foreach (var dataRow in dataRows)
        {
            var values = new string?[columnCount];
            for (var c = 1; c <= columnCount; c++)
            {
                values[c - 1] = GetCellText(dataRow.Cell(c));
            }
            rawRows.Add(values);

            rawRowNumbers.Add(dataRow.RowNumber());
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
        result.RawRowNumbers = rawRowNumbers;
        result.TotalRows = rawRows.Count;
        return result;
    }

    public List<ImportRowPreview> Validate(ImportParseResult parsed, ImportTarget? target = null)
    {
        target ??= ImportTarget.FromFile;

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
            var intakeId = ResolveTargetIntakeId(target, orgNodeIntakeMap, row.Fields.UnitName);
            var (status, note, existingRecipientId) = EvaluateRow(row.Fields, row.IncompleteFullName, row.CourseArrivalDateInvalid,
                existingServiceNumbers, seenInFile, intakeId, existingNameKeys, seenNameKeysInFile);
            previews.Add(new ImportRowPreview
            {
                RowNumber = row.RowNumber,
                FullNameDisplay = BuildFullNameDisplay(row.Fields),
                RankDisplay = row.Fields.Rank,
                UnitDisplay = row.Fields.UnitName,
                Status = status,
                Note = note,
                ExistingRecipientId = existingRecipientId
            });
        }

        return previews;
    }

    public ImportSummary Import(
        ImportParseResult parsed, ImportTarget? target = null, IReadOnlyCollection<int>? moveRowNumbers = null)
    {
        target ??= ImportTarget.FromFile;
        var moveRows = moveRowNumbers is null || moveRowNumbers.Count == 0
            ? null
            : new HashSet<int>(moveRowNumbers);

        var rows = ParseRows(parsed);
        using var db = _dbFactory.CreateDbContext();

        var existingServiceNumbers = LoadExistingServiceNumbers(db);
        var orgNodeIntakeMap = LoadOrgNodeIntakeMap(db);
        var existingNameKeys = LoadExistingNameKeys(db);
        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenNameKeysInFile = new HashSet<string>(StringComparer.Ordinal);
        var unitCache = new Dictionary<string, Unit>(UkIgnoreCase);
        var orgNodeCache = new Dictionary<string, OrgNode>(UkIgnoreCase);
        var roomCache = new Dictionary<(string Building, string Number), Room>(RoomKeyComparer.Instance);
        var intakeFolderCache = new Dictionary<int, Dictionary<string, int>>();

        PrefillLookups(db, unitCache, orgNodeCache, roomCache);

        var imported = 0;
        var skipped = 0;
        var errors = 0;
        var moved = 0;
        var errorMessages = new List<string>();

        foreach (var row in rows)
        {
            var intakeId = ResolveTargetIntakeId(target, orgNodeIntakeMap, row.Fields.UnitName);
            var (status, _, existingRecipientId) = EvaluateRow(row.Fields, row.IncompleteFullName, row.CourseArrivalDateInvalid,
                existingServiceNumbers, seenInFile, intakeId, existingNameKeys, seenNameKeysInFile);

            var moveThisRow = status == ImportRowStatus.Duplicate
                && existingRecipientId is not null
                && moveRows?.Contains(row.RowNumber) == true;

            if (moveThisRow)
            {
                try
                {
                    MoveExistingRecipient(
                        db, existingRecipientId!.Value, row.Fields, target,
                        unitCache, orgNodeCache, roomCache, intakeFolderCache, parsed.FilePath);
                    moved++;
                }
                catch (Exception ex)
                {
                    DetachPending(db);
                    errors++;
                    errorMessages.Add($"Рядок {row.RowNumber}: {ex.Message}");
                }
                continue;
            }

            if (status is ImportRowStatus.Error or ImportRowStatus.Duplicate)
            {
                skipped++;
                continue;
            }

            try
            {
                var unit = ResolveUnit(db, unitCache, row.Fields.UnitName);
                var room = ResolveRoom(db, roomCache, row.Fields.Building, row.Fields.RoomNumber);

                OrgNode? fileOrgNode = null;
                var fileOrgNodeResolved = false;
                OrgNode? FileOrgNode()
                {
                    if (fileOrgNodeResolved) return fileOrgNode;
                    fileOrgNode = ResolveOrgNode(db, orgNodeCache, row.Fields.UnitName);
                    fileOrgNodeResolved = true;
                    return fileOrgNode;
                }

                var resolvedIntakeId = target.Kind switch
                {
                    ImportTargetKind.PermanentStaff => null,
                    ImportTargetKind.Intake => target.IntakeId,
                    _ => FileOrgNode()?.IntakeId
                };

                var fitnessCategory = ParseFitnessCategory(row.Fields.FitnessRaw);

                int? resolvedOrgNodeId = resolvedIntakeId is int intakeIdForRouting
                    ? ResolveIntakeFitnessFolderId(db, intakeFolderCache, intakeIdForRouting, fitnessCategory)
                    : null;

                resolvedOrgNodeId ??= target.Kind == ImportTargetKind.Intake
                    ? target.OrgNodeId ?? FileOrgNode()?.Id
                    : FileOrgNode()?.Id;

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
                DetachPending(db);

                errors++;
                errorMessages.Add($"Рядок {row.RowNumber}: {ex.GetBaseException().Message}");
            }
        }

        if (imported > 0 || moved > 0)
        {
            if (imported > 0)
                _auditLogService.LogImport(db, "Recipient", imported, $"з файлу {Path.GetFileName(parsed.FilePath)}");

            db.SaveChanges();
            WeakReferenceMessenger.Default.Send(new CountsChangedMessage());
        }

        return new ImportSummary(imported, skipped, errors, moved) { ErrorMessages = errorMessages };
    }

    private static void DetachPending(AppDbContext db)
    {
        foreach (var entry in db.ChangeTracker.Entries()
                     .Where(e => e.State != EntityState.Unchanged).ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private void MoveExistingRecipient(
        AppDbContext db, int recipientId, RowFields fields, ImportTarget target,
        Dictionary<string, Unit> unitCache, Dictionary<string, OrgNode> orgNodeCache,
        Dictionary<(string Building, string Number), Room> roomCache,
        Dictionary<int, Dictionary<string, int>> intakeFolderCache, string filePath)
    {
        var person = db.Recipients.FirstOrDefault(r => r.Id == recipientId)
            ?? throw new InvalidOperationException("Картку не знайдено - можливо, її видалили в іншій сесії.");

        var previousIntakeId = person.IntakeId;

        var unit = ResolveUnit(db, unitCache, fields.UnitName);
        var room = ResolveRoom(db, roomCache, fields.Building, fields.RoomNumber);

        OrgNode? fileOrgNode = null;
        var fileOrgNodeResolved = false;
        OrgNode? FileOrgNode()
        {
            if (fileOrgNodeResolved) return fileOrgNode;
            fileOrgNode = ResolveOrgNode(db, orgNodeCache, fields.UnitName);
            fileOrgNodeResolved = true;
            return fileOrgNode;
        }

        var resolvedIntakeId = target.Kind switch
        {
            ImportTargetKind.PermanentStaff => null,
            ImportTargetKind.Intake => target.IntakeId,
            _ => FileOrgNode()?.IntakeId
        };

        var fitnessCategory = ParseFitnessCategory(fields.FitnessRaw) ?? person.FitnessCategory;

        int? resolvedOrgNodeId = resolvedIntakeId is int intakeIdForRouting
            ? ResolveIntakeFitnessFolderId(db, intakeFolderCache, intakeIdForRouting, fitnessCategory)
            : null;

        resolvedOrgNodeId ??= target.Kind == ImportTargetKind.Intake
            ? target.OrgNodeId ?? FileOrgNode()?.Id
            : FileOrgNode()?.Id;

        person.IntakeId = resolvedIntakeId;
        if (resolvedOrgNodeId is int nodeId) person.OrgNodeId = nodeId;
        if (unit is not null) person.UnitId = unit.Id;
        if (room is not null) person.RoomId = room.Id;
        person.FitnessCategory = fitnessCategory;

        person.LastName = Keep(fields.LastName, person.LastName);
        person.FirstName = Keep(fields.FirstName, person.FirstName);
        person.MiddleName = KeepNullable(fields.MiddleName, person.MiddleName);
        person.Rank = Keep(fields.Rank, person.Rank);
        person.Position = Keep(fields.Position, person.Position);
        person.ServiceNumber = Keep(fields.ServiceNumber, person.ServiceNumber);
        person.DateOfBirth = fields.DateOfBirth ?? person.DateOfBirth;
        person.CourseArrivalDate = fields.CourseArrivalDate ?? person.CourseArrivalDate;

        person.Nationality = KeepNullable(fields.Nationality, person.Nationality);
        person.Vos = KeepNullable(fields.Vos, person.Vos);
        person.MaritalStatus = KeepNullable(fields.MaritalStatus, person.MaritalStatus);
        person.RegistrationAddress = KeepNullable(fields.RegistrationAddress, person.RegistrationAddress);
        person.ResidenceAddress = KeepNullable(fields.ResidenceAddress, person.ResidenceAddress);
        person.Phone = KeepNullable(fields.Phone, person.Phone);
        person.Note = KeepNullable(fields.Note, person.Note);
        person.GroupName = KeepNullable(fields.GroupName, person.GroupName);
        person.NameTransliterated = KeepNullable(fields.NameTransliterated, person.NameTransliterated);
        person.ServedBefore = KeepNullable(fields.ServedBefore, person.ServedBefore);
        person.ExtraNote = KeepNullable(fields.ExtraNote, person.ExtraNote);
        person.CommanderContact = KeepNullable(fields.CommanderContact, person.CommanderContact);
        person.TravelCertificateNumber = KeepNullable(fields.TravelCertificateNumber, person.TravelCertificateNumber);
        person.FoodCertificate = KeepNullable(fields.FoodCertificate, person.FoodCertificate);
        person.IdDocumentNumber = KeepNullable(fields.IdDocumentNumber, person.IdDocumentNumber);
        person.MedicalBoard = KeepNullable(fields.MedicalBoard, person.MedicalBoard);
        person.MedicalBoardConclusion = KeepNullable(fields.MedicalBoardConclusion, person.MedicalBoardConclusion);
        person.OriginUnit = KeepNullable(fields.OriginUnit, person.OriginUnit);
        person.Vehicle = KeepNullable(fields.Vehicle, person.Vehicle);

        var weapons = ParseWeaponUnits(fields.WeaponRaw);
        if (weapons.Count > 0)
        {
            var existing = db.Weapons.Where(w => w.RecipientId == person.Id).ToList();
            db.Weapons.RemoveRange(existing);

            foreach (var (name, serialNumber, rawText) in weapons)
            {
                db.Weapons.Add(new Weapon
                {
                    RecipientId = person.Id,
                    Name = name,
                    SerialNumber = serialNumber,
                    RawText = rawText
                });
            }
        }

        _auditLogService.LogUpdate(
            db, "Recipient", person.Id,
            oldValue: $"набір {previousIntakeId?.ToString() ?? "постійний склад"}",
            newValue: $"набір {resolvedIntakeId?.ToString() ?? "постійний склад"}",
            details: $"перенесено імпортом з файлу {Path.GetFileName(filePath)}");

        db.SaveChanges();
    }

    private static string Keep(string? incoming, string current)
        => string.IsNullOrWhiteSpace(incoming) ? current : incoming.Trim();

    private static string? KeepNullable(string? incoming, string? current)
        => string.IsNullOrWhiteSpace(incoming) ? current : incoming.Trim();

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? GetCellText(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;

        if (cell.DataType == XLDataType.DateTime)
            return cell.GetDateTime().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

        var text = cell.GetString().Trim();
        return text.Length == 0 ? null : text;
    }

    internal static ImportTargetField AutoMapHeader(string header, ref bool noteColumnAssigned)
    {
        var normalized = HeaderNormalization.Normalize(header);

        if (normalized.Contains("№ з/п")) return ImportTargetField.NotImported;

        if (normalized.Contains("командир")) return ImportTargetField.CommanderContact;

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
                var trimmed = value?.Trim();

                if (values.TryGetValue(column.MappedField, out var already)
                    && !string.IsNullOrWhiteSpace(already))
                {
                    continue;
                }

                values[column.MappedField] = trimmed;
            }

            var fields = new RowFields();
            var incompleteFullName = false;

            if (values.TryGetValue(ImportTargetField.FullName, out var fullName) && !string.IsNullOrWhiteSpace(fullName))
            {
                var name = FullNameParser.Split(fullName);
                fields.LastName = name.LastName;
                fields.FirstName = name.FirstName;
                if (!string.IsNullOrEmpty(name.MiddleName)) fields.MiddleName = name.MiddleName;
                if (name.IsIncomplete) incompleteFullName = true;
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

            var rowNumber = i < parsed.RawRowNumbers.Count ? parsed.RawRowNumbers[i] : i + 2;
            result.Add(new RowInfo(rowNumber, fields, incompleteFullName, courseArrivalDateInvalid));
        }

        return result;
    }

    public const string TrashedDuplicateNote =
        "Особа з таким особовим номером є в кошику - рядок пропущено. "
        + "Відновіть її через «Кошик» і повторіть імпорт, щоб перенести в цей набір";

    private static (ImportRowStatus Status, string Note, int? ExistingRecipientId) EvaluateRow(
        RowFields fields, bool incompleteFullName, bool courseArrivalDateInvalid,
        Dictionary<string, ExistingByNumber> existingServiceNumbers, HashSet<string> seenInFile,
        int? intakeId, Dictionary<string, int> existingNameKeys, HashSet<string> seenNameKeysInFile)
    {
        if (string.IsNullOrWhiteSpace(fields.LastName) && string.IsNullOrWhiteSpace(fields.FirstName))
            return (ImportRowStatus.Error, "Порожнє поле ПІБ", null);

        if (!string.IsNullOrWhiteSpace(fields.DateOfBirthRaw) && fields.DateOfBirth is null)
            return (ImportRowStatus.Error, "Некоректна дата народження", null);

        var serviceNumber = fields.ServiceNumber.Trim();
        var nameCheckedInstead = false;
        if (serviceNumber.Length > 0)
        {
            if (existingServiceNumbers.TryGetValue(serviceNumber, out var byNumber))
            {
                return byNumber.InTrash
                    ? (ImportRowStatus.Duplicate, TrashedDuplicateNote, null)
                    : (ImportRowStatus.Duplicate, "Вже є в базі - рядок пропущено", byNumber.Id);
            }

            if (!seenInFile.Add(serviceNumber))
                return (ImportRowStatus.Duplicate, "Дублюється в файлі - рядок пропущено", null);
        }
        else
        {
            var nameKey = BuildNameKey(intakeId, fields.LastName, fields.FirstName, fields.MiddleName, fields.DateOfBirth);
            if (existingNameKeys.TryGetValue(nameKey, out var byName))
                return (ImportRowStatus.Duplicate, "Схожий запис (ПІБ і дата народження) вже є в наборі - рядок пропущено", byName);

            if (!seenNameKeysInFile.Add(nameKey))
                return (ImportRowStatus.Duplicate, "Дублюється в файлі - рядок пропущено", null);

            nameCheckedInstead = true;
        }

        if (incompleteFullName)
            return (ImportRowStatus.Warning, "Неповне ПІБ", null);

        if (courseArrivalDateInvalid)
            return (ImportRowStatus.Warning, "Некоректна дата прибуття - поле пропущено", null);

        if (string.IsNullOrWhiteSpace(fields.RoomNumber))
            return (ImportRowStatus.Warning, "Немає поля «Кімната» - додасться без розміщення", null);

        if (nameCheckedInstead)
            return (ImportRowStatus.Warning, "Без особового номера - дубль перевірено за ПІБ і датою народження", null);

        return (ImportRowStatus.Ok, string.Empty, null);
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

    internal static int? ResolveTargetIntakeId(
        ImportTarget target, Dictionary<string, int?> orgNodeIntakeMap, string unitName)
        => target.Kind switch
        {
            ImportTargetKind.PermanentStaff => null,
            ImportTargetKind.Intake => target.IntakeId,
            _ => ResolveIntakeId(orgNodeIntakeMap, unitName)
        };

    private static int? ResolveIntakeId(Dictionary<string, int?> orgNodeIntakeMap, string unitName)
    {
        var trimmed = unitName.Trim();
        return trimmed.Length > 0 && orgNodeIntakeMap.TryGetValue(trimmed, out var intakeId) ? intakeId : null;
    }

    private static string? ParseFitnessCategory(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var normalized = raw.Trim().ToLower(CultureInfo.GetCultureInfo("uk-UA"));

        if (normalized.Contains("обмежено")) return "обмежено придатний";
        if (normalized.Contains("неприда")) return "непридатний";
        if (normalized.Contains("придатн")) return "придатний";
        return null;
    }

    private static int? ResolveIntakeFitnessFolderId(
        AppDbContext db, Dictionary<int, Dictionary<string, int>> cache, int intakeId, string? fitnessCategory)
    {
        if (!cache.TryGetValue(intakeId, out var folders))
        {
            folders = new Dictionary<string, int>(UkIgnoreCase);
            cache[intakeId] = folders;
        }

        var targetName = Intakes.IntakeFitnessFolders.FolderNameFor(fitnessCategory);
        if (folders.TryGetValue(targetName, out var cached)) return cached;

        var resolved = Intakes.IntakeFitnessFolders.Resolve(db, intakeId, fitnessCategory);
        if (resolved is int id) folders[targetName] = id;
        return resolved;
    }

    private static readonly System.Text.RegularExpressions.Regex WeaponPrefixRegex = new(
        @"(?:^|(?<=\s))(АКМС|АКС|АКМ|АК|ПМ)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

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

    private static Dictionary<string, int> LoadExistingNameKeys(AppDbContext db)
    {
        var recipients = db.Recipients
            .Select(r => new { r.Id, r.IntakeId, r.LastName, r.FirstName, r.MiddleName, r.DateOfBirth })
            .ToList();

        var keys = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in recipients)
        {
            var key = BuildNameKey(r.IntakeId, r.LastName, r.FirstName, r.MiddleName, r.DateOfBirth);
            if (!keys.ContainsKey(key)) keys[key] = r.Id;
        }

        return keys;
    }

    private readonly record struct ExistingByNumber(int Id, bool InTrash);

    private static Dictionary<string, ExistingByNumber> LoadExistingServiceNumbers(AppDbContext db)
    {
        var people = db.Recipients
            .IgnoreQueryFilters()
            .Where(r => r.ServiceNumber != null && r.ServiceNumber != "")
            .Select(r => new { r.Id, r.ServiceNumber, InTrash = r.DeletedAt != null })
            .OrderBy(p => p.InTrash)
            .ToList();

        var map = new Dictionary<string, ExistingByNumber>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in people)
        {
            var key = p.ServiceNumber.Trim();
            if (!map.ContainsKey(key)) map[key] = new ExistingByNumber(p.Id, p.InTrash);
        }

        return map;
    }

    private sealed class RoomKeyComparer : IEqualityComparer<(string Building, string Number)>
    {
        public static readonly RoomKeyComparer Instance = new();

        public bool Equals((string Building, string Number) x, (string Building, string Number) y)
            => UkIgnoreCase.Equals(x.Building, y.Building) && UkIgnoreCase.Equals(x.Number, y.Number);

        public int GetHashCode((string Building, string Number) key)
            => HashCode.Combine(
                UkIgnoreCase.GetHashCode(key.Building),
                UkIgnoreCase.GetHashCode(key.Number));
    }

    private static void PrefillLookups(
        AppDbContext db,
        Dictionary<string, Unit> unitCache,
        Dictionary<string, OrgNode> orgNodeCache,
        Dictionary<(string Building, string Number), Room> roomCache)
    {
        foreach (var unit in db.Units.AsEnumerable())
            unitCache.TryAdd(unit.Name.Trim(), unit);

        foreach (var node in db.OrgNodes.AsEnumerable())
            orgNodeCache.TryAdd(node.Name.Trim(), node);

        foreach (var room in db.Rooms.AsEnumerable())
            roomCache.TryAdd(((room.Building ?? string.Empty).Trim(), room.Number.Trim()), room);
    }

    private static Unit? ResolveUnit(AppDbContext db, Dictionary<string, Unit> cache, string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return null;

        if (cache.TryGetValue(trimmed, out var cached)) return cached;

        var created = new Unit { Name = trimmed };
        db.Units.Add(created);
        db.SaveChanges();
        cache[trimmed] = created;
        return created;
    }

    private static OrgNode? ResolveOrgNode(AppDbContext db, Dictionary<string, OrgNode> cache, string name)
    {
        var root = db.OrgNodes.OrderBy(n => n.Depth).ThenBy(n => n.Id).FirstOrDefault(n => n.ParentId == null);
        if (root is null) return null;

        var trimmed = name.Trim();
        if (trimmed.Length == 0) return root;

        if (cache.TryGetValue(trimmed, out var cached)) return cached;

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
