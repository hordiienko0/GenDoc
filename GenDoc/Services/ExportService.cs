using System.IO;
using ClosedXML.Excel;
using GenDoc.Data;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Generation;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services
{
    // Генерик-обгортка над ClosedXML: ViewModel описує лише колонки (заголовок +
    // селектор значення з T), сервіс нічого не знає про конкретні моделі (Recipient,
    // AuditLogEntry, Room тощо) - тому один і той самий метод обслуговує будь-який
    // майбутній список без дублювання коду запису xlsx.
    public sealed class ExportService : IExportService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly IAuditLogService _auditLogService;
        private readonly IXlsxGenerationService _xlsxGenerationService;

        public ExportService(
            IDbContextFactory<AppDbContext> dbFactory,
            IAuditLogService auditLogService,
            IXlsxGenerationService xlsxGenerationService)
        {
            _dbFactory = dbFactory;
            _auditLogService = auditLogService;
            _xlsxGenerationService = xlsxGenerationService;
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

        public Task<ExportResult> ExportByTemplateAsync(
            int templateId, IReadOnlyList<Recipient> items, IDictionary<string, string> manualValues, string filePath)
        {
            return Task.Run(() =>
            {
                try
                {
                    string templateName;
                    byte[] content;
                    List<ExportTemplateColumnMapping> mappings;
                    int templateRowIndex;
                    bool usesPlaceholders;
                    bool repeatSheetPerDate;

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
                        templateRowIndex = template.TemplateRowIndex;
                        usesPlaceholders = template.UsesPlaceholders;
                        repeatSheetPerDate = template.RepeatSheetPerDate;
                    }

                    OrganizationSettings? org;
                    string? courseOfficerSignature;
                    using (var db = _dbFactory.CreateDbContext())
                    {
                        org = db.OrganizationSettings.FirstOrDefault();
                        courseOfficerSignature = CourseOfficerSignature.Build(db);
                    }

                    // Той самий сторож, що й у пакетній генерації: відомість із
                    // порожнім місцем підпису гірша за явну відмову, бо порожнечу
                    // помічають уже після того, як папір пішов далі.
                    if (mappings.Any(m => m.FieldKey == nameof(ExportFieldKey.CourseOfficerSignature))
                        && string.IsNullOrEmpty(courseOfficerSignature))
                    {
                        return new ExportResult(false, 0, null,
                            "Немає жодного курсового офіцера серед постійного складу - " +
                            "відомість не сформовано. Позначте курсового офіцера в розділі «Постійний склад».");
                    }

                    var result = _xlsxGenerationService.Generate(
                        content, templateRowIndex, usesPlaceholders, mappings, items, org, manualValues,
                        repeatSheetPerDate, courseOfficerSignature);

                    if (!result.Success)
                        return new ExportResult(false, 0, null, result.ErrorMessage);

                    var directory = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);

                    File.WriteAllBytes(filePath, result.Content!);

                    using (var db = _dbFactory.CreateDbContext())
                    {
                        _auditLogService.LogExport(db, "Recipient", items.Count, $"{templateName} → {Path.GetFileName(filePath)}");
                        db.SaveChanges();
                    }

                    return new ExportResult(true, items.Count, filePath, null, result.UnfilledTags);
                }
                catch (Exception ex)
                {
                    return new ExportResult(false, 0, null, ex.Message);
                }
            });
        }

        private static void WriteCellValue(IXLCell cell, object? value)
        {
            switch (value)
            {
                case null:
                    cell.Value = "-";
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
