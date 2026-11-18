using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Data;
using GenDoc.Models;
using GenDoc.Models.Enums;
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
        var people = ExistingPeople.Load(db);
        var orgNodeIntakeMap = LoadOrgNodeIntakeMap(db);
        var state = new FileState();
        var roomColumnMapped = HasRoomColumn(parsed);
        var serviceNumberColumnMapped = HasColumn(parsed, ImportTargetField.ServiceNumber);

        var evaluated = new List<(RowInfo Row, int? IntakeId, RowEvaluation Eval)>();
        foreach (var row in rows)
        {
            var intakeId = ResolveTargetIntakeId(target, orgNodeIntakeMap, row.Fields.UnitName);
            evaluated.Add((row, intakeId,
                EvaluateRow(row, people, state, intakeId, roomColumnMapped, serviceNumberColumnMapped)));
        }

        var matchedIds = evaluated
            .Where(e => e.Eval.Existing is not null)
            .Select(e => e.Eval.Existing!.Id)
            .Distinct()
            .ToList();
        var cards = LoadCards(db, matchedIds);
        var locations = matchedIds.Count == 0 ? null : LocationDescriber.Load(db);

        var previews = new List<ImportRowPreview>();
        foreach (var (row, intakeId, eval) in evaluated)
        {
            var preview = new ImportRowPreview
            {
                RowNumber = row.RowNumber,
                FullNameDisplay = BuildFullNameDisplay(row.Fields),
                RankDisplay = row.Fields.Rank,
                UnitDisplay = row.Fields.UnitName,
                Status = eval.Status,
                Note = eval.Note
            };

            if (eval.Existing is { } existing && cards.TryGetValue(existing.Id, out var card))
            {
                preview.ExistingRecipientId = existing.Id;
                preview.ExistingLocation = locations!.Describe(existing.IntakeId, existing.OrgNodeId);
                preview.Differences = FindDifferences(row.Fields, card);
                preview.CanMove = existing.IntakeId != intakeId;
                preview.CanAddAsNew = eval.WeakMatch;
                preview.Note = DescribeExisting(eval, preview.ExistingLocation, preview.Differences.Count);
            }

            previews.Add(preview);
        }

        return previews;
    }

    public ImportSummary Import(
        ImportParseResult parsed, ImportTarget? target = null,
        IReadOnlyDictionary<int, ImportExistingAction>? rowActions = null)
    {
        target ??= ImportTarget.FromFile;

        var rows = ParseRows(parsed);
        using var db = _dbFactory.CreateDbContext();

        var people = ExistingPeople.Load(db);
        var orgNodeIntakeMap = LoadOrgNodeIntakeMap(db);
        var state = new FileState();
        var unitCache = new Dictionary<string, Unit>(UkIgnoreCase);
        var orgNodeCache = new Dictionary<string, OrgNode>(UkIgnoreCase);
        var roomCache = new Dictionary<(string Building, string Number), Room>(RoomKeyComparer.Instance);
        var intakeFolderCache = new Dictionary<int, Dictionary<string, int>>();

        PrefillLookups(db, unitCache, orgNodeCache, roomCache);

        var imported = 0;
        var skipped = 0;
        var errors = 0;
        var moved = 0;
        var updated = 0;
        var errorMessages = new List<string>();

        foreach (var row in rows)
        {
            var intakeId = ResolveTargetIntakeId(target, orgNodeIntakeMap, row.Fields.UnitName);
            var eval = EvaluateRow(row, people, state, intakeId,
                roomColumnMapped: true, serviceNumberColumnMapped: true);

            if (eval.Existing is { } existing)
            {
                var action = rowActions?.GetValueOrDefault(row.RowNumber, ImportExistingAction.Skip)
                             ?? ImportExistingAction.Skip;
                // «Додати як нового» - лише коли збіг непевний (тільки ПІБ): з тим самим особовим
                // номером чи ПІБ і датою народження це точно та сама людина, і друга картка - дубль.
                if (action == ImportExistingAction.AddAsNew && !eval.WeakMatch)
                    action = ImportExistingAction.Skip;

                if (action is ImportExistingAction.Move or ImportExistingAction.Update)
                {
                    try
                    {
                        if (action == ImportExistingAction.Move)
                        {
                            MoveExistingRecipient(
                                db, existing.Id, row.Fields, target,
                                unitCache, orgNodeCache, roomCache, intakeFolderCache, parsed.FilePath);
                            moved++;
                        }
                        else if (UpdateExistingRecipient(
                                     db, existing.Id, row.Fields, unitCache, roomCache, intakeFolderCache,
                                     parsed.FilePath))
                        {
                            updated++;
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                    catch (Exception ex)
                    {
                        DetachPending(db);
                        errors++;
                        errorMessages.Add($"Рядок {row.RowNumber}: {ex.Message}");
                    }
                    continue;
                }

                if (action == ImportExistingAction.Skip)
                {
                    skipped++;
                    continue;
                }
            }
            else if (eval.Status is ImportRowStatus.Error or ImportRowStatus.Duplicate)
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
                    Vehicle = NullIfEmpty(row.Fields.Vehicle),

                    CivilEducation = NullIfEmpty(row.Fields.CivilEducation),
                    MilitaryEducation = NullIfEmpty(row.Fields.MilitaryEducation),
                    ArmedForcesSince = row.Fields.ArmedForcesSince,
                    ConscriptionOffice = NullIfEmpty(row.Fields.ConscriptionOffice),
                    TaxNumber = NullIfEmpty(row.Fields.TaxNumber),
                    Passport = NullIfEmpty(row.Fields.Passport),
                    PlaceOfBirth = NullIfEmpty(row.Fields.PlaceOfBirth),
                    RankOrder = NullIfEmpty(row.Fields.RankOrder)
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

        if (imported > 0 || moved > 0 || updated > 0)
        {
            if (imported > 0)
                _auditLogService.LogImport(db, "Recipient", imported, $"з файлу {Path.GetFileName(parsed.FilePath)}");

            db.SaveChanges();
            WeakReferenceMessenger.Default.Send(new CountsChangedMessage());
        }

        return new ImportSummary(imported, skipped, errors, moved, updated) { ErrorMessages = errorMessages };
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

        ApplyIncomingFields(db, person, fields, unit, room, fitnessCategory);

        _auditLogService.LogUpdate(
            db, "Recipient", person.Id,
            oldValue: $"набір {previousIntakeId?.ToString() ?? "постійний склад"}",
            newValue: $"набір {resolvedIntakeId?.ToString() ?? "постійний склад"}",
            details: $"перенесено імпортом з файлу {Path.GetFileName(filePath)}");

        db.SaveChanges();
    }

    /// <summary>
    /// «Актуалізувати»: людина лишається там, де є, а картка отримує значення з файлу (порожні клітинки нічого не стирають).
    /// Повертає false, якщо оновлювати нічого - дані вже збігаються.
    /// </summary>
    private bool UpdateExistingRecipient(
        AppDbContext db, int recipientId, RowFields fields,
        Dictionary<string, Unit> unitCache,
        Dictionary<(string Building, string Number), Room> roomCache,
        Dictionary<int, Dictionary<string, int>> intakeFolderCache, string filePath)
    {
        var person = db.Recipients
                         .Include(r => r.Unit)
                         .Include(r => r.Room)
                         .Include(r => r.Weapons)
                         .FirstOrDefault(r => r.Id == recipientId)
                     ?? throw new InvalidOperationException("Картку не знайдено - можливо, її видалили в іншій сесії.");

        var differences = FindDifferences(fields, person);
        if (differences.Count == 0) return false;

        // Усе, що може створити запис і зберегти контекст (підрозділ, кімната, папка), - до змін у картці,
        // щоб помилка посередині не лишила половину полів збереженими.
        var unit = ResolveUnit(db, unitCache, fields.UnitName);
        var room = ResolveRoom(db, roomCache, fields.Building, fields.RoomNumber);
        var fitnessCategory = ParseFitnessCategory(fields.FitnessRaw) ?? person.FitnessCategory;

        // Як у картці людини: змінилася придатність - людина переходить у відповідну папку свого набору
        // (лише якщо зараз вона в папці цього ж набору).
        int? fitnessFolderId = null;
        if (person.IntakeId is int intakeId
            && !UkIgnoreCase.Equals(person.FitnessCategory ?? string.Empty, fitnessCategory ?? string.Empty))
        {
            var nodeIntakeId = db.OrgNodes
                .Where(n => n.Id == person.OrgNodeId)
                .Select(n => n.IntakeId)
                .FirstOrDefault();
            if (nodeIntakeId == intakeId)
                fitnessFolderId = ResolveIntakeFitnessFolderId(db, intakeFolderCache, intakeId, fitnessCategory);
        }

        ApplyIncomingFields(db, person, fields, unit, room, fitnessCategory);
        if (fitnessFolderId is int folderId) person.OrgNodeId = folderId;

        _auditLogService.LogUpdate(
            db, "Recipient", person.Id,
            oldValue: string.Join("; ", differences.Select(d => $"{d.Field}: {d.Current}")),
            newValue: string.Join("; ", differences.Select(d => $"{d.Field}: {d.Incoming}")),
            details: $"актуалізовано імпортом з файлу {Path.GetFileName(filePath)}");

        db.SaveChanges();
        return true;
    }

    private static void ApplyIncomingFields(
        AppDbContext db, Recipient person, RowFields fields, Unit? unit, Room? room, string? fitnessCategory)
    {
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

        person.CivilEducation = KeepNullable(fields.CivilEducation, person.CivilEducation);
        person.MilitaryEducation = KeepNullable(fields.MilitaryEducation, person.MilitaryEducation);
        person.ArmedForcesSince = fields.ArmedForcesSince ?? person.ArmedForcesSince;
        person.ConscriptionOffice = KeepNullable(fields.ConscriptionOffice, person.ConscriptionOffice);
        person.TaxNumber = KeepNullable(fields.TaxNumber, person.TaxNumber);
        person.Passport = KeepNullable(fields.Passport, person.Passport);
        person.PlaceOfBirth = KeepNullable(fields.PlaceOfBirth, person.PlaceOfBirth);
        person.RankOrder = KeepNullable(fields.RankOrder, person.RankOrder);

        var weapons = ParseWeaponUnits(fields.WeaponRaw);
        if (weapons.Count > 0)
        {
            var existing = db.Weapons.Where(w => w.RecipientId == person.Id).ToList();

            // Та сама зброя - не перестворюємо записи, щоб не загубити дату видачі й примітку до неї.
            if (SameText(WeaponsText(existing), WeaponsText(weapons), compact: false)) return;

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

        // «Сімейний стан (ПІП дружини рік народження; ім'я і рік народження дитини)» містить «ім'я» -
        // перевірка після імен віддавала колонку полю «Ім'я», і сімейний стан губився.
        if (normalized.Contains("сімейний стан")) return ImportTargetField.MaritalStatus;

        if (normalized.Contains("іноземній мові")) return ImportTargetField.NameTransliterated;
        if (normalized.Contains("піб")) return ImportTargetField.FullName;

        if (normalized.Contains("прізвище")) return ImportTargetField.LastName;
        if (normalized.Contains("по батькові")) return ImportTargetField.MiddleName;
        if (normalized.Contains("імя")) return ImportTargetField.FirstName;

        if (normalized.Contains("наказ") && normalized.Contains("звання")) return ImportTargetField.RankOrder;
        if (normalized.Contains("звання")) return ImportTargetField.Rank;
        if (normalized.Contains("національність")) return ImportTargetField.Nationality;
        // «Освіта військова (ВОС)» - заклад і ВОС за дипломом, а не ВОС, на яку людина навчається.
        if (normalized.Contains("освіта"))
            return normalized.Contains("військов") ? ImportTargetField.MilitaryEducation : ImportTargetField.CivilEducation;
        if (normalized.Contains("вос")) return ImportTargetField.Vos;
        if (normalized.Contains("прибув на курси")) return ImportTargetField.CourseArrivalDate;
        if (normalized.Contains("з якого часу") && normalized.Contains("зсу")) return ImportTargetField.ArmedForcesSince;
        if (normalized.Contains("призван")) return ImportTargetField.ConscriptionOffice;
        if (normalized.Contains("адреса реєстрації")) return ImportTargetField.RegistrationAddress;
        if (normalized.Contains("фактичного проживання")) return ImportTargetField.ResidenceAddress;

        if (normalized.Contains("телефон")) return ImportTargetField.Phone;
        if (normalized.Contains("іпн")) return ImportTargetField.TaxNumber;
        if (normalized.Contains("паспорт")) return ImportTargetField.Passport;
        if (normalized.Contains("місце народження")) return ImportTargetField.PlaceOfBirth;

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

            // Заповнені лише колонки, яких не імпортуємо (у бланку анкети - порожні рядки з одним «№ з/п»):
            // це не людина, тож не показуємо рядок помилкою «Порожнє поле ПІБ» і не рахуємо його.
            if (values.Values.All(string.IsNullOrWhiteSpace)) continue;

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
            fields.CivilEducation = values.GetValueOrDefault(ImportTargetField.CivilEducation) ?? string.Empty;
            fields.MilitaryEducation = values.GetValueOrDefault(ImportTargetField.MilitaryEducation) ?? string.Empty;
            fields.ConscriptionOffice = values.GetValueOrDefault(ImportTargetField.ConscriptionOffice) ?? string.Empty;
            fields.TaxNumber = values.GetValueOrDefault(ImportTargetField.TaxNumber) ?? string.Empty;
            fields.Passport = values.GetValueOrDefault(ImportTargetField.Passport) ?? string.Empty;
            fields.PlaceOfBirth = values.GetValueOrDefault(ImportTargetField.PlaceOfBirth) ?? string.Empty;
            fields.RankOrder = values.GetValueOrDefault(ImportTargetField.RankOrder) ?? string.Empty;
            // В анкетах окремої колонки придатності немає - категорія записана у «Висновку ВЛК»
            // («придатний» / «обмежено придатний»), а в анкеті 31 набору висновку окремо немає зовсім -
            // «придатний до в/с» дописано в кінці «Довідки ВЛК». Без цього всі люди йшли в «Без категорії».
            var fitnessRaw = values.GetValueOrDefault(ImportTargetField.Fitness);
            fields.FitnessRaw = new[] { fitnessRaw, fields.MedicalBoardConclusion, fields.MedicalBoard }
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

            fields.DateOfBirthRaw = values.GetValueOrDefault(ImportTargetField.DateOfBirth);
            if (!string.IsNullOrWhiteSpace(fields.DateOfBirthRaw) &&
                DateOnly.TryParseExact(fields.DateOfBirthRaw, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dob))
            {
                fields.DateOfBirth = dob;
            }

            var courseArrivalDateInvalid = !TryParseOptionalDate(
                values.GetValueOrDefault(ImportTargetField.CourseArrivalDate), out fields.CourseArrivalDate);
            var armedForcesSinceInvalid = !TryParseOptionalDate(
                values.GetValueOrDefault(ImportTargetField.ArmedForcesSince), out fields.ArmedForcesSince);

            var rowNumber = i < parsed.RawRowNumbers.Count ? parsed.RawRowNumbers[i] : i + 2;
            result.Add(new RowInfo(rowNumber, fields, incompleteFullName, courseArrivalDateInvalid, armedForcesSinceInvalid));
        }

        return result;
    }

    // Порожня клітинка - не помилка: помилка лише текст, який не читається як дата.
    private static bool TryParseOptionalDate(string? raw, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;
        if (!DateOnly.TryParseExact(raw, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return false;
        date = parsed;
        return true;
    }

    public static bool HasColumn(ImportParseResult parsed, ImportTargetField field)
        => parsed.Columns.Any(c => c.MappedField == field);

    private static bool HasRoomColumn(ImportParseResult parsed) => HasColumn(parsed, ImportTargetField.RoomNumber);

    public const string TrashedDuplicateNote =
        "Особа з таким особовим номером є в кошику - рядок пропущено. "
        + "Відновіть її через «Кошик» і повторіть імпорт, щоб перенести в цей набір";

    public const string InFileDuplicateNote = "Дублюється в файлі - рядок пропущено";

    /// <summary>
    /// Чи людина вже є в особовому складі - у будь-якому наборі чи в постійному складі, а не лише в тому,
    /// куди імпортуємо. Спершу за особовим номером, далі за ПІБ (з датою народження, коли вона є з обох боків):
    /// раніше ПІБ перевірявся лише в межах набору й лише для рядків без номера, і та сама людина
    /// з іншого файлу (чи з номером, якого в картці ще не було) заходила другою карткою.
    /// </summary>
    private static RowEvaluation EvaluateRow(
        RowInfo row, ExistingPeople people, FileState state, int? intakeId,
        bool roomColumnMapped, bool serviceNumberColumnMapped)
    {
        var fields = row.Fields;
        if (string.IsNullOrWhiteSpace(fields.LastName) && string.IsNullOrWhiteSpace(fields.FirstName))
            return new RowEvaluation(ImportRowStatus.Error, "Порожнє поле ПІБ");

        if (!string.IsNullOrWhiteSpace(fields.DateOfBirthRaw) && fields.DateOfBirth is null)
            return new RowEvaluation(ImportRowStatus.Error, "Некоректна дата народження");

        var serviceNumber = fields.ServiceNumber.Trim();
        if (serviceNumber.Length > 0)
        {
            if (people.ByNumber.TryGetValue(serviceNumber, out var byNumber))
            {
                return byNumber.InTrash
                    ? new RowEvaluation(ImportRowStatus.Duplicate, TrashedDuplicateNote)
                    : Matched(state, new ExistingMatch(byNumber, Weak: false, Reason: string.Empty, Count: 1));
            }

            if (!state.ServiceNumbers.Add(serviceNumber))
                return new RowEvaluation(ImportRowStatus.Duplicate, InFileDuplicateNote);
        }
        else
        {
            var nameKey = BuildNameKey(intakeId, fields.LastName, fields.FirstName, fields.MiddleName, fields.DateOfBirth);
            if (!state.NameKeys.Add(nameKey))
                return new RowEvaluation(ImportRowStatus.Duplicate, InFileDuplicateNote);
        }

        if (people.FindByName(fields, intakeId) is { } byName)
            return Matched(state, byName);

        if (row.IncompleteFullName)
            return new RowEvaluation(ImportRowStatus.Warning, "Неповне ПІБ");

        if (row.CourseArrivalDateInvalid)
            return new RowEvaluation(ImportRowStatus.Warning, "Некоректна дата прибуття - поле пропущено");

        if (row.ArmedForcesSinceInvalid)
            return new RowEvaluation(ImportRowStatus.Warning, "Некоректна дата «З якого часу в ЗСУ» - поле пропущено");

        // Колонки «Кімната» у файлі немає зовсім (так в анкетах) - про це одне повідомлення над таблицею,
        // а не «УВАГА» на кожному рядку.
        if (roomColumnMapped && string.IsNullOrWhiteSpace(fields.RoomNumber))
            return new RowEvaluation(ImportRowStatus.Warning, "Кімнату не вказано - додасться без розміщення");

        // Як і з кімнатою: якщо колонки особового номера немає взагалі, це одне повідомлення над таблицею.
        if (serviceNumber.Length == 0 && serviceNumberColumnMapped)
            return new RowEvaluation(ImportRowStatus.Warning, "Без особового номера - дубль перевірено за ПІБ і датою народження");

        return new RowEvaluation(ImportRowStatus.Ok, string.Empty);
    }

    // Та сама картка вже знайдена вище у файлі - другий рядок про неї лише дубль у файлі.
    private static RowEvaluation Matched(FileState state, ExistingMatch match)
        => state.MatchedIds.Add(match.Person.Id)
            ? new RowEvaluation(ImportRowStatus.Duplicate, string.Empty, match.Person, match.Weak, match.Reason, match.Count)
            : new RowEvaluation(ImportRowStatus.Duplicate, InFileDuplicateNote);

    private static string DescribeExisting(RowEvaluation eval, string location, int differenceCount)
    {
        var head = eval.WeakMatch
            ? $"Схоже, вже є в базі ({eval.MatchReason}): {location}"
            : $"Вже є в базі: {location}";
        if (eval.Namesakes > 1)
            head += $" (осіб з таким ПІБ у базі: {eval.Namesakes})";

        var tail = differenceCount == 0
            ? "Дані збігаються - рядок пропущено"
            : $"У файлі інші дані ({differenceCount} {PluralHelper.Pluralize(differenceCount, "поле", "поля", "полів")}) - можна актуалізувати";

        return $"{head}. {tail}";
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
        // «непридатний» містить «придатн» - перевірка потрібна, щоб не записати таку особу придатною;
        // непридатних у наборі не буває, тож категорію не ставимо (особа потрапить у «Без категорії»).
        if (normalized.Contains("неприда")) return null;
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

    private sealed record ExistingPerson(
        int Id, string LastName, string FirstName, string? MiddleName, DateOnly? DateOfBirth,
        string ServiceNumber, int? IntakeId, int? OrgNodeId, bool InTrash);

    private sealed record ExistingMatch(ExistingPerson Person, bool Weak, string Reason, int Count);

    private sealed record RowEvaluation(
        ImportRowStatus Status, string Note,
        ExistingPerson? Existing = null, bool WeakMatch = false, string MatchReason = "", int Namesakes = 0);

    /// <summary>Що вже трапилося вище в цьому ж файлі.</summary>
    private sealed class FileState
    {
        public HashSet<string> ServiceNumbers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> NameKeys { get; } = new(StringComparer.Ordinal);
        public HashSet<int> MatchedIds { get; } = new();
    }

    /// <summary>Увесь особовий склад бази - для пошуку тієї самої людини за номером чи ПІБ.</summary>
    private sealed class ExistingPeople
    {
        private readonly Dictionary<string, List<ExistingPerson>> _byName = new(StringComparer.Ordinal);

        /// <summary>Особовий номер → картка; жива картка перемагає таку саму в кошику.</summary>
        public Dictionary<string, ExistingPerson> ByNumber { get; } = new(StringComparer.OrdinalIgnoreCase);

        public static ExistingPeople Load(AppDbContext db)
        {
            var all = db.Recipients
                .IgnoreQueryFilters()
                .Select(r => new
                {
                    r.Id, r.LastName, r.FirstName, r.MiddleName, r.DateOfBirth, r.ServiceNumber,
                    r.IntakeId, r.OrgNodeId, InTrash = r.DeletedAt != null
                })
                .ToList()
                .Select(r => new ExistingPerson(
                    r.Id, r.LastName ?? string.Empty, r.FirstName ?? string.Empty, r.MiddleName, r.DateOfBirth,
                    r.ServiceNumber ?? string.Empty, r.IntakeId, r.OrgNodeId, r.InTrash))
                .OrderBy(p => p.InTrash)
                .ToList();

            var people = new ExistingPeople();
            foreach (var p in all)
            {
                var number = p.ServiceNumber.Trim();
                if (number.Length > 0) people.ByNumber.TryAdd(number, p);

                // Кошик за ПІБ не шукаємо: видалену людину з тим самим ім'ям можна завести знову.
                if (p.InTrash) continue;

                var key = PersonNameKey(p.LastName, p.FirstName);
                if (key.Length == 0) continue;
                if (!people._byName.TryGetValue(key, out var list))
                    people._byName[key] = list = new List<ExistingPerson>();
                list.Add(p);
            }

            return people;
        }

        /// <summary>
        /// Прізвище й ім'я однакові, по батькові й дата народження не суперечать (збігаються або десь не вказані).
        /// Певний збіг - коли дата народження є з обох боків і однакова; без неї (чи з іншим особовим номером)
        /// це може бути однофамілець, тож рядок можна буде додати як нову людину.
        /// </summary>
        public ExistingMatch? FindByName(RowFields fields, int? targetIntakeId)
        {
            var key = PersonNameKey(fields.LastName, fields.FirstName);
            if (key.Length == 0 || !_byName.TryGetValue(key, out var list)) return null;

            var fileMiddle = NormalizeNamePart(fields.MiddleName);
            var fileNumber = fields.ServiceNumber.Trim();

            var candidates = new List<(ExistingPerson Person, bool Weak, string Reason)>();
            foreach (var p in list)
            {
                var middle = NormalizeNamePart(p.MiddleName);
                if (fileMiddle.Length > 0 && middle.Length > 0 && fileMiddle != middle) continue;

                if (fields.DateOfBirth is { } dob && p.DateOfBirth is { } existingDob && dob != existingDob) continue;
                var sameDob = fields.DateOfBirth is not null && p.DateOfBirth is not null;

                var number = p.ServiceNumber.Trim();
                var otherNumber = fileNumber.Length > 0 && number.Length > 0
                                  && !string.Equals(fileNumber, number, StringComparison.OrdinalIgnoreCase);

                // Інший особовий номер і нічим не підтверджене ПІБ - це різні люди.
                if (otherNumber && !sameDob) continue;

                var reason = otherNumber
                    ? "ПІБ і дата народження збігаються, але особовий номер інший"
                    : sameDob
                        ? string.Empty
                        : "збігається ПІБ, дати народження для порівняння немає";
                candidates.Add((p, Weak: otherNumber || !sameDob, reason));
            }

            if (candidates.Count == 0) return null;

            var best = candidates
                .OrderBy(c => c.Weak)
                .ThenBy(c => c.Person.IntakeId == targetIntakeId ? 0 : 1)
                .ThenByDescending(c => c.Person.Id)
                .First();
            return new ExistingMatch(best.Person, best.Weak, best.Reason, candidates.Count);
        }
    }

    private static string PersonNameKey(string? lastName, string? firstName)
    {
        var last = NormalizeNamePart(lastName);
        return last.Length == 0 ? string.Empty : $"{last}|{NormalizeNamePart(firstName)}";
    }

    /// <summary>
    /// Великі літери, один пробіл, один вид апострофа, латинські двійники кириличних літер - кирилицею
    /// (у списках з Excel трапляється «I» латиницею замість «І» і т. п.).
    /// </summary>
    internal static string NormalizeNamePart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var upper = value.Trim().ToUpper(UkCulture);
        var sb = new System.Text.StringBuilder(upper.Length);
        var pendingSpace = false;
        foreach (var c in upper)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(c switch
            {
                'A' => 'А', 'B' => 'В', 'C' => 'С', 'E' => 'Е', 'H' => 'Н', 'I' => 'І', 'K' => 'К',
                'M' => 'М', 'O' => 'О', 'P' => 'Р', 'T' => 'Т', 'X' => 'Х', 'Y' => 'У',
                '’' or 'ʼ' or '‘' or '`' or '´' or 'ʻ' => '\'',
                '–' or '—' => '-',
                _ => c
            });
        }

        return sb.ToString();
    }

    private static readonly CultureInfo UkCulture = CultureInfo.GetCultureInfo("uk-UA");

    private static Dictionary<int, Recipient> LoadCards(AppDbContext db, List<int> ids)
    {
        if (ids.Count == 0) return new Dictionary<int, Recipient>();

        return db.Recipients
            .AsNoTracking()
            .Include(r => r.Unit)
            .Include(r => r.Room)
            .Include(r => r.Weapons)
            .Where(r => ids.Contains(r.Id))
            .ToDictionary(r => r.Id);
    }

    /// <summary>«Набір №15 / Всі / Придатні» чи «Постійний склад / …» - де людина вже є.</summary>
    private sealed class LocationDescriber
    {
        private readonly Dictionary<int, (string Name, int? ParentId)> _nodes;
        private readonly Dictionary<int, (string Label, bool Completed)> _intakes;

        private LocationDescriber(
            Dictionary<int, (string Name, int? ParentId)> nodes, Dictionary<int, (string Label, bool Completed)> intakes)
        {
            _nodes = nodes;
            _intakes = intakes;
        }

        public static LocationDescriber Load(AppDbContext db)
        {
            var nodes = db.OrgNodes.IgnoreQueryFilters()
                .Select(n => new { n.Id, n.Name, n.ParentId })
                .ToList()
                .ToDictionary(n => n.Id, n => ((n.Name ?? string.Empty).Trim(), n.ParentId));

            var intakes = db.Intakes.IgnoreQueryFilters()
                .Select(i => new { i.Id, i.Number, i.DisplayNumber, i.Status })
                .ToList()
                .ToDictionary(
                    i => i.Id,
                    i => (Intakes.IntakeLabel.Of(i.Number, i.DisplayNumber), i.Status == IntakeStatus.Completed));

            return new LocationDescriber(nodes, intakes);
        }

        public string Describe(int? intakeId, int? orgNodeId)
        {
            var path = new List<string>();
            var current = orgNodeId;
            var guard = 0;
            while (current is int id && _nodes.TryGetValue(id, out var node) && guard++ < 64)
            {
                // Корінь дерева (назва частини) спільний для всіх - у шляху він лише заважає.
                if (node.ParentId is null) break;
                path.Insert(0, node.Name);
                current = node.ParentId;
            }

            string head;
            if (intakeId is int iid)
            {
                var (label, completed) = _intakes.TryGetValue(iid, out var intake) ? intake : ($"Набір #{iid}", false);
                // Коренева папка набору називається так само, як набір.
                if (path.Count > 0 && UkIgnoreCase.Equals(path[0], label)) path.RemoveAt(0);
                head = completed ? $"{label} (завершений)" : label;
            }
            else
            {
                head = "Постійний склад";
            }

            return path.Count == 0 ? head : $"{head} / {string.Join(" / ", path)}";
        }
    }

    /// <summary>
    /// Поля, у яких файл каже інше, ніж картка. Порожня клітинка файлу - не відмінність:
    /// імпорт її й так не записує (див. <see cref="ApplyIncomingFields"/>).
    /// </summary>
    private static List<ImportFieldDifference> FindDifferences(RowFields f, Recipient card)
    {
        var diffs = new List<ImportFieldDifference>();

        void Name(ImportTargetField field, string? incoming, string? current)
        {
            if (string.IsNullOrWhiteSpace(incoming)) return;
            if (NormalizeNamePart(incoming) == NormalizeNamePart(current)) return;
            diffs.Add(new ImportFieldDifference(Label(field), (current ?? string.Empty).Trim(), incoming.Trim()));
        }

        void Text(ImportTargetField field, string? incoming, string? current, bool compact = false)
        {
            if (string.IsNullOrWhiteSpace(incoming)) return;
            if (SameText(incoming, current, compact)) return;
            diffs.Add(new ImportFieldDifference(Label(field), (current ?? string.Empty).Trim(), incoming.Trim()));
        }

        void Date(ImportTargetField field, DateOnly? incoming, DateOnly? current)
        {
            if (incoming is null || incoming == current) return;
            diffs.Add(new ImportFieldDifference(Label(field),
                current?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? string.Empty,
                incoming.Value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)));
        }

        Name(ImportTargetField.LastName, f.LastName, card.LastName);
        Name(ImportTargetField.FirstName, f.FirstName, card.FirstName);
        Name(ImportTargetField.MiddleName, f.MiddleName, card.MiddleName);
        Text(ImportTargetField.Rank, f.Rank, card.Rank);
        Text(ImportTargetField.Position, f.Position, card.Position);
        Text(ImportTargetField.ServiceNumber, f.ServiceNumber, card.ServiceNumber, compact: true);
        Date(ImportTargetField.DateOfBirth, f.DateOfBirth, card.DateOfBirth);
        Text(ImportTargetField.Unit, f.UnitName, card.Unit?.Name);

        if (!string.IsNullOrWhiteSpace(f.RoomNumber))
        {
            var currentBuilding = card.Room?.Building ?? string.Empty;
            var currentNumber = card.Room?.Number ?? string.Empty;
            if (!UkIgnoreCase.Equals(currentNumber.Trim(), f.RoomNumber.Trim())
                || !UkIgnoreCase.Equals(currentBuilding.Trim(), f.Building.Trim()))
            {
                diffs.Add(new ImportFieldDifference(Label(ImportTargetField.RoomNumber),
                    card.Room is null ? string.Empty : RoomText(currentBuilding, currentNumber),
                    RoomText(f.Building, f.RoomNumber)));
            }
        }

        var fitness = ParseFitnessCategory(f.FitnessRaw);
        if (fitness is not null && !SameText(fitness, card.FitnessCategory, compact: false))
            diffs.Add(new ImportFieldDifference(Label(ImportTargetField.Fitness), card.FitnessCategory ?? string.Empty, fitness));

        Text(ImportTargetField.Nationality, f.Nationality, card.Nationality);
        Text(ImportTargetField.Vos, f.Vos, card.Vos);
        Date(ImportTargetField.CourseArrivalDate, f.CourseArrivalDate, card.CourseArrivalDate);
        Text(ImportTargetField.MaritalStatus, f.MaritalStatus, card.MaritalStatus);
        Text(ImportTargetField.RegistrationAddress, f.RegistrationAddress, card.RegistrationAddress);
        Text(ImportTargetField.ResidenceAddress, f.ResidenceAddress, card.ResidenceAddress);
        Text(ImportTargetField.Phone, f.Phone, card.Phone, compact: true);
        Text(ImportTargetField.Note, f.Note, card.Note);
        Text(ImportTargetField.GroupName, f.GroupName, card.GroupName);
        Text(ImportTargetField.NameTransliterated, f.NameTransliterated, card.NameTransliterated);
        Text(ImportTargetField.ServedBefore, f.ServedBefore, card.ServedBefore);
        Text(ImportTargetField.ExtraNote, f.ExtraNote, card.ExtraNote);
        Text(ImportTargetField.CommanderContact, f.CommanderContact, card.CommanderContact);
        Text(ImportTargetField.TravelCertificateNumber, f.TravelCertificateNumber, card.TravelCertificateNumber);
        Text(ImportTargetField.FoodCertificate, f.FoodCertificate, card.FoodCertificate);
        Text(ImportTargetField.IdDocumentNumber, f.IdDocumentNumber, card.IdDocumentNumber, compact: true);
        Text(ImportTargetField.MedicalBoard, f.MedicalBoard, card.MedicalBoard);
        Text(ImportTargetField.MedicalBoardConclusion, f.MedicalBoardConclusion, card.MedicalBoardConclusion);
        Text(ImportTargetField.OriginUnit, f.OriginUnit, card.OriginUnit);
        Text(ImportTargetField.Vehicle, f.Vehicle, card.Vehicle);

        var weapons = ParseWeaponUnits(f.WeaponRaw);
        if (weapons.Count > 0)
            Text(ImportTargetField.Weapon, WeaponsText(weapons), WeaponsText(card.Weapons));

        Text(ImportTargetField.CivilEducation, f.CivilEducation, card.CivilEducation);
        Text(ImportTargetField.MilitaryEducation, f.MilitaryEducation, card.MilitaryEducation);
        Date(ImportTargetField.ArmedForcesSince, f.ArmedForcesSince, card.ArmedForcesSince);
        Text(ImportTargetField.ConscriptionOffice, f.ConscriptionOffice, card.ConscriptionOffice);
        Text(ImportTargetField.TaxNumber, f.TaxNumber, card.TaxNumber, compact: true);
        Text(ImportTargetField.Passport, f.Passport, card.Passport, compact: true);
        Text(ImportTargetField.PlaceOfBirth, f.PlaceOfBirth, card.PlaceOfBirth);
        Text(ImportTargetField.RankOrder, f.RankOrder, card.RankOrder);

        return diffs;
    }

    private static string Label(ImportTargetField field) => ImportTargetFieldNames.DisplayNames[field];

    private static string RoomText(string? building, string number)
        => string.IsNullOrWhiteSpace(building) ? number.Trim() : $"корп. {building.Trim()}, кімн. {number.Trim()}";

    private static string WeaponsText(IEnumerable<Weapon> weapons)
        => string.Join("; ", weapons
            .OrderBy(w => w.Id)
            .Select(w => !string.IsNullOrWhiteSpace(w.RawText)
                ? w.RawText.Trim()
                : string.IsNullOrWhiteSpace(w.SerialNumber) ? w.Name.Trim() : $"{w.Name.Trim()} № {w.SerialNumber.Trim()}"));

    private static string WeaponsText(IEnumerable<(string Name, string SerialNumber, string RawText)> weapons)
        => string.Join("; ", weapons.Select(w => w.RawText.Trim()));

    /// <summary>
    /// Регістр і зайві пробіли відмінністю не вважаються; для номерів і телефонів (compact) - також пробіли,
    /// дефіси й дужки всередині («067 123-45-67» і «0671234567» - той самий номер).
    /// </summary>
    private static bool SameText(string? a, string? b, bool compact)
        => UkIgnoreCase.Equals(Squash(a, compact), Squash(b, compact));

    private static string Squash(string? value, bool compact)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var joined = string.Join(compact ? string.Empty : " ", parts);
        return compact ? joined.Replace("-", string.Empty).Replace("(", string.Empty).Replace(")", string.Empty) : joined;
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

        public string CivilEducation = string.Empty;
        public string MilitaryEducation = string.Empty;
        public DateOnly? ArmedForcesSince;
        public string ConscriptionOffice = string.Empty;
        public string TaxNumber = string.Empty;
        public string Passport = string.Empty;
        public string PlaceOfBirth = string.Empty;
        public string RankOrder = string.Empty;
    }

    private record RowInfo(
        int RowNumber, RowFields Fields, bool IncompleteFullName, bool CourseArrivalDateInvalid, bool ArmedForcesSinceInvalid);
}
