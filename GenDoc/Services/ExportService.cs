using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using GenDoc.Data;
using GenDoc.Models;
using GenDoc.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services
{
    // Генерик-обгортка над ClosedXML: ViewModel описує лише колонки (заголовок +
    // селектор значення з T), сервіс нічого не знає про конкретні моделі (Recipient,
    // AuditLogEntry, Room тощо) — тому один і той самий метод обслуговує будь-який
    // майбутній список без дублювання коду запису xlsx.
    public sealed class ExportService : IExportService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly IAuditLogService _auditLogService;

        public ExportService(IDbContextFactory<AppDbContext> dbFactory, IAuditLogService auditLogService)
        {
            _dbFactory = dbFactory;
            _auditLogService = auditLogService;
        }

        public Task<ExportResult> ExportToXlsxAsync<T>(
            IEnumerable<T> items,
            IReadOnlyList<ExportColumn<T>> columns,
            string filePath,
            string sheetName = "Аркуш1")
        {
            return Task.Run(() =>
            {
                try
                {
                    using var workbook = new XLWorkbook();
                    var sheet = workbook.Worksheets.Add(sheetName);

                    for (var c = 0; c < columns.Count; c++)
                    {
                        var headerCell = sheet.Cell(1, c + 1);
                        headerCell.Value = columns[c].Header;
                        headerCell.Style.Font.Bold = true;
                        headerCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8E4E1");
                    }

                    var row = 2;
                    var rowCount = 0;
                    foreach (var item in items)
                    {
                        for (var c = 0; c < columns.Count; c++)
                        {
                            WriteCellValue(sheet.Cell(row, c + 1), columns[c].Selector(item));
                        }
                        row++;
                        rowCount++;
                    }

                    sheet.SheetView.FreezeRows(1);
                    sheet.Columns().AdjustToContents();

                    var directory = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);

                    workbook.SaveAs(filePath);

                    return new ExportResult(true, rowCount, filePath, null);
                }
                catch (Exception ex)
                {
                    return new ExportResult(false, 0, null, ex.Message);
                }
            });
        }

        public Task<ExportResult> ExportByTemplateAsync(int templateId, IReadOnlyList<Recipient> items, string filePath)
        {
            return Task.Run(() =>
            {
                try
                {
                    string templateName;
                    byte[] content;
                    List<ExportTemplateColumnMapping> mappings;

                    using (var db = _dbFactory.CreateDbContext())
                    {
                        var template = db.ExportTemplates
                            .Include(t => t.ColumnMappings)
                            .FirstOrDefault(t => t.Id == templateId);

                        if (template is null)
                            return new ExportResult(false, 0, null, "Шаблон експорту не знайдено.");

                        templateName = template.Name;
                        content = template.Content;
                        mappings = template.ColumnMappings.OrderBy(m => m.ColumnIndex).ToList();
                    }

                    using var workbook = new XLWorkbook(new MemoryStream(content));
                    var sheet = workbook.Worksheets.First();

                    var row = 2;
                    var rowNumber = 1;
                    foreach (var item in items)
                    {
                        foreach (var mapping in mappings)
                        {
                            var fieldKey = Enum.Parse<ExportFieldKey>(mapping.FieldKey);
                            var value = ResolveFieldValue(fieldKey, item, rowNumber);

                            var cell = sheet.Cell(row, mapping.ColumnIndex);
                            cell.SetValue(value);
                            cell.Style.Font.FontName = "Times New Roman";
                            cell.Style.Font.FontSize = 10;
                            cell.Style.Alignment.WrapText = true;
                            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                            cell.Style.Border.TopBorder = XLBorderStyleValues.Thin;
                            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                            cell.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
                            cell.Style.Border.RightBorder = XLBorderStyleValues.Thin;
                        }

                        sheet.Row(row).Height = 27.75;
                        row++;
                        rowNumber++;
                    }

                    var directory = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);

                    workbook.SaveAs(filePath);

                    using (var db = _dbFactory.CreateDbContext())
                    {
                        _auditLogService.LogExport(db, "Recipient", items.Count, $"{templateName} → {Path.GetFileName(filePath)}");
                        db.SaveChanges();
                    }

                    return new ExportResult(true, items.Count, filePath, null);
                }
                catch (Exception ex)
                {
                    return new ExportResult(false, 0, null, ex.Message);
                }
            });
        }

        private static string ResolveFieldValue(ExportFieldKey key, Recipient r, int rowNumber) => key switch
        {
            ExportFieldKey.RowNumber => rowNumber.ToString(CultureInfo.InvariantCulture),
            ExportFieldKey.Rank => r.Rank,
            ExportFieldKey.FullNameFormatted => FormatFullName(r),
            ExportFieldKey.LastName => r.LastName,
            ExportFieldKey.FirstName => r.FirstName,
            ExportFieldKey.MiddleName => r.MiddleName ?? string.Empty,
            ExportFieldKey.DateOfBirth => r.DateOfBirth?.ToString("dd.MM.yyyy") ?? string.Empty,
            ExportFieldKey.Nationality => r.Nationality ?? string.Empty,
            ExportFieldKey.Vos => r.Vos ?? string.Empty,
            ExportFieldKey.CourseArrivalDate => r.CourseArrivalDate?.ToString("dd.MM.yyyy") ?? string.Empty,
            ExportFieldKey.MaritalStatus => r.MaritalStatus ?? string.Empty,
            ExportFieldKey.RegistrationAddress => r.RegistrationAddress ?? string.Empty,
            ExportFieldKey.ResidenceAddress => r.ResidenceAddress ?? string.Empty,
            ExportFieldKey.Phone => r.Phone ?? string.Empty,
            ExportFieldKey.Note => r.Note ?? string.Empty,
            ExportFieldKey.GroupName => r.GroupName ?? string.Empty,
            ExportFieldKey.NameTransliterated => r.NameTransliterated ?? string.Empty,
            ExportFieldKey.ServedBefore => r.ServedBefore ?? string.Empty,
            ExportFieldKey.ExtraNote => r.ExtraNote ?? string.Empty,
            ExportFieldKey.CommanderContact => r.CommanderContact ?? string.Empty,
            ExportFieldKey.TravelCertificateNumber => r.TravelCertificateNumber ?? string.Empty,
            ExportFieldKey.FoodCertificate => r.FoodCertificate ?? string.Empty,
            ExportFieldKey.IdDocumentNumber => r.IdDocumentNumber ?? string.Empty,
            ExportFieldKey.MedicalBoard => r.MedicalBoard ?? string.Empty,
            ExportFieldKey.MedicalBoardConclusion => r.MedicalBoardConclusion ?? string.Empty,
            ExportFieldKey.OriginUnit => r.OriginUnit ?? string.Empty,
            ExportFieldKey.Position => r.Position,
            ExportFieldKey.Vehicle => r.Vehicle ?? string.Empty,
            ExportFieldKey.ServiceNumber => r.ServiceNumber,
            ExportFieldKey.UnitName => r.Unit?.Name ?? string.Empty,
            ExportFieldKey.RoomDisplay => FormatRoom(r.Room),
            _ => string.Empty
        };

        private static string FormatFullName(Recipient r)
        {
            var lastName = (r.LastName ?? string.Empty).ToUpper(new CultureInfo("uk-UA"));
            return string.Join(' ', new[] { lastName, r.FirstName, r.MiddleName }.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        private static string FormatRoom(Room? room)
        {
            if (room is null || string.IsNullOrWhiteSpace(room.Number)) return string.Empty;
            return string.IsNullOrWhiteSpace(room.Building) ? room.Number : $"{room.Building} {room.Number}";
        }

        private static void WriteCellValue(IXLCell cell, object? value)
        {
            switch (value)
            {
                case null:
                    cell.Value = "—";
                    break;
                case DateTime dateTime:
                    cell.Value = dateTime;
                    cell.Style.DateFormat.Format = "dd.MM.yyyy";
                    break;
                case int i:
                    cell.Value = i;
                    break;
                default:
                    cell.Value = value.ToString();
                    break;
            }
        }
    }
}
