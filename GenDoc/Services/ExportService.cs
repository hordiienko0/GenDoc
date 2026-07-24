using System.IO;
using ClosedXML.Excel;

namespace GenDoc.Services
{
    // Генерик-обгортка над ClosedXML: ViewModel описує лише колонки (заголовок +
    // селектор значення з T), сервіс нічого не знає про конкретні моделі (Recipient,
    // AuditLogEntry, Room тощо) — тому один і той самий метод обслуговує будь-який
    // майбутній список без дублювання коду запису xlsx.
    public sealed class ExportService : IExportService
    {
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
