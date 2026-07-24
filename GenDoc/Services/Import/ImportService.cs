using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using GenDoc.Data;
using GenDoc.Models;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services.Import;

public class ImportService : IImportService
{
    private static readonly string[] DateFormats = { "dd.MM.yyyy", "d.M.yyyy" };

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
        for (var c = 1; c <= columnCount; c++)
        {
            var header = headerRow.Cell(c).GetString().Trim();
            var example = rawRows.Select(r => r[c - 1]).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
            var column = new ImportColumn(c - 1, header, example)
            {
                MappedField = AutoMapHeader(header)
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
        var rows = ParseRows(parsed);
        var existingServiceNumbers = LoadExistingServiceNumbers();
        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var previews = new List<ImportRowPreview>();
        foreach (var row in rows)
        {
            var (status, note) = EvaluateRow(row.Fields, row.IncompleteFullName, existingServiceNumbers, seenInFile);
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

    public ImportSummary Import(ImportParseResult parsed)
    {
        var rows = ParseRows(parsed);
        using var db = _dbFactory.CreateDbContext();

        var existingServiceNumbers = LoadExistingServiceNumbers(db);
        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unitCache = new Dictionary<string, Unit>(StringComparer.Ordinal);
        var roomCache = new Dictionary<(string Building, string Number), Room>();

        var imported = 0;
        var skipped = 0;
        var errors = 0;

        foreach (var row in rows)
        {
            var (status, _) = EvaluateRow(row.Fields, row.IncompleteFullName, existingServiceNumbers, seenInFile);
            if (status is ImportRowStatus.Error or ImportRowStatus.Duplicate)
            {
                skipped++;
                continue;
            }

            try
            {
                var unit = ResolveUnit(db, unitCache, row.Fields.UnitName);
                var room = ResolveRoom(db, roomCache, row.Fields.Building, row.Fields.RoomNumber);

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
                    RoomId = room?.Id
                };

                db.Recipients.Add(recipient);
                db.SaveChanges();
                imported++;
            }
            catch
            {
                errors++;
            }
        }

        if (imported > 0)
        {
            _auditLogService.LogImport(db, "Recipient", imported, $"з файлу {Path.GetFileName(parsed.FilePath)}");
            db.SaveChanges();
        }

        return new ImportSummary(imported, skipped, errors);
    }

    private static string? GetCellText(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;

        if (cell.DataType == XLDataType.DateTime)
            return cell.GetDateTime().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

        var text = cell.GetString().Trim();
        return text.Length == 0 ? null : text;
    }

    private static ImportTargetField AutoMapHeader(string header)
    {
        var normalized = header.ToLowerInvariant().Trim()
            .Replace("'", string.Empty)
            .Replace("’", string.Empty)
            .Replace("-", string.Empty);

        return normalized switch
        {
            "піб" => ImportTargetField.FullName,
            "прізвище" => ImportTargetField.LastName,
            "імя" => ImportTargetField.FirstName,
            "по батькові" => ImportTargetField.MiddleName,
            "звання" => ImportTargetField.Rank,
            "посада" => ImportTargetField.Position,
            "підрозділ" => ImportTargetField.Unit,
            "особовий номер" => ImportTargetField.ServiceNumber,
            "дата народження" => ImportTargetField.DateOfBirth,
            "корпус" => ImportTargetField.Building,
            "кімната" => ImportTargetField.RoomNumber,
            _ => ImportTargetField.NotImported
        };
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

            fields.DateOfBirthRaw = values.GetValueOrDefault(ImportTargetField.DateOfBirth);
            if (!string.IsNullOrWhiteSpace(fields.DateOfBirthRaw) &&
                DateOnly.TryParseExact(fields.DateOfBirthRaw, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dob))
            {
                fields.DateOfBirth = dob;
            }

            result.Add(new RowInfo(i + 2, fields, incompleteFullName));
        }

        return result;
    }

    private static (ImportRowStatus Status, string Note) EvaluateRow(
        RowFields fields, bool incompleteFullName, HashSet<string> existingServiceNumbers, HashSet<string> seenInFile)
    {
        if (string.IsNullOrWhiteSpace(fields.LastName) && string.IsNullOrWhiteSpace(fields.FirstName))
            return (ImportRowStatus.Error, "Порожнє поле ПІБ");

        if (!string.IsNullOrWhiteSpace(fields.DateOfBirthRaw) && fields.DateOfBirth is null)
            return (ImportRowStatus.Error, "Некоректна дата народження");

        var serviceNumber = fields.ServiceNumber.Trim();
        if (serviceNumber.Length > 0)
        {
            if (existingServiceNumbers.Contains(serviceNumber))
                return (ImportRowStatus.Duplicate, "Вже є в базі — рядок пропущено");

            if (!seenInFile.Add(serviceNumber))
                return (ImportRowStatus.Duplicate, "Дублюється в файлі — рядок пропущено");
        }

        if (incompleteFullName)
            return (ImportRowStatus.Warning, "Неповне ПІБ");

        if (string.IsNullOrWhiteSpace(fields.RoomNumber))
            return (ImportRowStatus.Warning, "Немає поля «Кімната» — додасться без розміщення");

        if (serviceNumber.Length == 0)
            return (ImportRowStatus.Warning, "Без особового номера — дубль не перевірено");

        return (ImportRowStatus.Ok, string.Empty);
    }

    private static string BuildFullNameDisplay(RowFields fields)
        => string.Join(' ', new[] { fields.LastName, fields.FirstName, fields.MiddleName }
            .Where(p => !string.IsNullOrWhiteSpace(p)));

    private HashSet<string> LoadExistingServiceNumbers()
    {
        using var db = _dbFactory.CreateDbContext();
        return LoadExistingServiceNumbers(db);
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

        var existing = db.Units.FirstOrDefault(u => u.Name == trimmed);
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

        var created = new Room { Building = trimmedBuilding, Number = trimmedNumber, Capacity = 1 };
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
    }

    private record RowInfo(int RowNumber, RowFields Fields, bool IncompleteFullName);
}
