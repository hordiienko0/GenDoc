using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using GenDoc.Models;
using GenDoc.Models.Enums;

namespace GenDoc.Services.Generation
{
    public sealed class XlsxGenerationService : IXlsxGenerationService
    {
        private static readonly Regex PlaceholderRegex = new(@"\{\{[^{}]+\}\}", RegexOptions.Compiled);

        public XlsxGenerationResult Generate(
            byte[] templateContent,
            int templateRowIndex,
            bool usesPlaceholders,
            List<ExportTemplateColumnMapping> mappings,
            IReadOnlyList<Recipient> roster,
            OrganizationSettings? org,
            IDictionary<string, string> manualValues)
        {
            try
            {
                // Копія байтів — MemoryStream(byte[]) інакше пише напряму у переданий
                // масив (кеш шаблону), а його не можна мутувати між генераціями.
                using var stream = new MemoryStream();
                stream.Write(templateContent, 0, templateContent.Length);
                stream.Position = 0;

                using var workbook = new XLWorkbook(stream);
                var sheet = workbook.Worksheets.First();

                var unfilledTags = new List<string>();

                if (usesPlaceholders)
                    FillByPlaceholders(sheet, mappings, templateRowIndex, roster, org, manualValues, unfilledTags);
                else
                    FillByHeaderColumns(sheet, mappings, roster);

                using var output = new MemoryStream();
                workbook.SaveAs(output);

                return new XlsxGenerationResult(true, output.ToArray(), null, unfilledTags.Distinct().ToList());
            }
            catch (Exception ex)
            {
                return new XlsxGenerationResult(false, null, ex.Message, Array.Empty<string>());
            }
        }

        // Стара header-driven поведінка — без жодних стильових властивостей, лише
        // значення в жорстко визначений рядок 2, як і раніше.
        private static void FillByHeaderColumns(IXLWorksheet sheet, List<ExportTemplateColumnMapping> mappings, IReadOnlyList<Recipient> items)
        {
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
        }

        private static void FillByPlaceholders(
            IXLWorksheet sheet,
            List<ExportTemplateColumnMapping> mappings,
            int templateRowIndex,
            IReadOnlyList<Recipient> items,
            OrganizationSettings? org,
            IDictionary<string, string> manualValues,
            List<string> unfilledTags)
        {
            var rowMappings = mappings.Where(m => m.ColumnIndex > 0).ToList();
            var outsideMappings = mappings.Where(m => m.ColumnIndex == 0).ToList();

            var t = templateRowIndex;
            var n = items.Count;
            if (n == 0) return;

            if (n > 1)
            {
                InsertClonedRows(sheet, t, n - 1);
            }

            var mappingsByColumn = rowMappings.GroupBy(m => m.ColumnIndex).ToList();

            for (var i = 0; i < n; i++)
            {
                var person = items[i];
                var targetRow = t + i;
                var rowNumber = i + 1;

                foreach (var group in mappingsByColumn)
                {
                    var cell = sheet.Cell(targetRow, group.Key);
                    var text = cell.GetString();

                    if (group.Count() == 1 && text.Trim() == group.First().PlaceholderTag)
                    {
                        var raw = ResolvePlaceholderValue(group.First(), person, org, manualValues, rowNumber, unfilledTags);
                        AssignTypedOrString(cell, raw);
                        continue;
                    }

                    foreach (var mapping in group)
                    {
                        var raw = ResolvePlaceholderValue(mapping, person, org, manualValues, rowNumber, unfilledTags);
                        text = text.Replace(mapping.PlaceholderTag, raw);
                    }

                    cell.Value = text;
                }
            }

            SubstituteOutsideTags(sheet, outsideMappings, org, manualValues, unfilledTags, t, t + n - 1);
            CollectResidualUnfilledTags(sheet, unfilledTags);
        }

        // Вставляє insertedCount порожніх рядків під шаблонним рядком і клонує туди
        // його вміст/стиль — усе нижче (підсумки, блок підписів) зсувається разом
        // з об'єднаннями. ClosedXML сам зсуває більшість merged-діапазонів при
        // InsertRowsBelow, але про всяк випадок звіряємо адреси й довиправляємо
        // ті, що лишились на старому місці.
        private static void InsertClonedRows(IXLWorksheet sheet, int templateRow, int insertedCount)
        {
            // Об'єднання, що лежать ЦІЛКОМ у межах шаблонного рядка (напр. підпис на
            // всю ширину) — CopyTo(row) не гарантує перенесення стану merge, тому
            // повторно застосовуємо їх на кожному клоні за тими самими колонками.
            var inRowMergeColumnSpans = sheet.MergedRanges
                .Where(m => m.RangeAddress.FirstAddress.RowNumber == templateRow && m.RangeAddress.LastAddress.RowNumber == templateRow)
                .Select(m => (m.RangeAddress.FirstAddress.ColumnNumber, m.RangeAddress.LastAddress.ColumnNumber))
                .ToList();

            var addressesBelow = sheet.MergedRanges
                .Where(m => m.RangeAddress.FirstAddress.RowNumber > templateRow)
                .Select(m => m.RangeAddress.ToString() ?? string.Empty)
                .ToList();

            sheet.Row(templateRow).InsertRowsBelow(insertedCount);

            var currentMerged = new HashSet<string>(sheet.MergedRanges.Select(m => m.RangeAddress.ToString() ?? string.Empty));

            foreach (var address in addressesBelow)
            {
                var original = sheet.Range(address);
                var firstAddr = original.RangeAddress.FirstAddress;
                var lastAddr = original.RangeAddress.LastAddress;
                var shifted = sheet.Range(
                    sheet.Cell(firstAddr.RowNumber + insertedCount, firstAddr.ColumnNumber),
                    sheet.Cell(lastAddr.RowNumber + insertedCount, lastAddr.ColumnNumber));
                var shiftedAddress = shifted.RangeAddress.ToString() ?? string.Empty;

                if (currentMerged.Contains(shiftedAddress)) continue;

                if (currentMerged.Contains(address))
                    sheet.Range(address).Unmerge();

                shifted.Merge();
            }

            for (var i = 1; i <= insertedCount; i++)
            {
                var targetRow = templateRow + i;
                sheet.Row(templateRow).CopyTo(sheet.Row(targetRow));

                foreach (var (firstCol, lastCol) in inRowMergeColumnSpans)
                    sheet.Range(sheet.Cell(targetRow, firstCol), sheet.Cell(targetRow, lastCol)).Merge();
            }
        }

        private static void AssignTypedOrString(IXLCell cell, string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                cell.Value = string.Empty;
            }
            else if (DateTime.TryParseExact(raw, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                cell.Value = date;
            }
            else if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
            {
                cell.Value = intValue;
            }
            else
            {
                cell.Value = raw;
            }
        }

        private static void SubstituteOutsideTags(
            IXLWorksheet sheet,
            List<ExportTemplateColumnMapping> outsideMappings,
            OrganizationSettings? org,
            IDictionary<string, string> manualValues,
            List<string> unfilledTags,
            int excludeFromRow,
            int excludeToRow)
        {
            if (outsideMappings.Count == 0) return;

            var valueByTag = new Dictionary<string, string>();
            foreach (var mapping in outsideMappings)
            {
                var value = mapping.SourceType switch
                {
                    MappingSourceType.Organization => GetOrganizationFieldValue(org, mapping.FieldKey),
                    MappingSourceType.Manual => manualValues.TryGetValue(mapping.PlaceholderTag, out var manual) ? manual : string.Empty,
                    _ => string.Empty
                };

                if (string.IsNullOrEmpty(value)) unfilledTags.Add(mapping.PlaceholderTag);
                valueByTag[mapping.PlaceholderTag] = value;
            }

            var usedRange = sheet.RangeUsed();
            if (usedRange is null) return;

            foreach (var cell in usedRange.CellsUsed())
            {
                var rowNum = cell.Address.RowNumber;
                if (rowNum >= excludeFromRow && rowNum <= excludeToRow) continue;

                var text = cell.GetString();
                if (!text.Contains("{{")) continue;

                var replaced = text;
                foreach (var (tag, value) in valueByTag)
                    replaced = replaced.Replace(tag, value);

                if (replaced != text) cell.Value = replaced;
            }
        }

        private static void CollectResidualUnfilledTags(IXLWorksheet sheet, List<string> unfilledTags)
        {
            var usedRange = sheet.RangeUsed();
            if (usedRange is null) return;

            foreach (var cell in usedRange.CellsUsed())
            {
                var text = cell.GetString();
                foreach (Match match in PlaceholderRegex.Matches(text))
                    unfilledTags.Add(match.Value);
            }
        }

        private static string ResolvePlaceholderValue(
            ExportTemplateColumnMapping mapping,
            Recipient? recipient,
            OrganizationSettings? org,
            IDictionary<string, string> manualValues,
            int rowNumber,
            List<string> unfilledTags)
        {
            var value = mapping.SourceType switch
            {
                MappingSourceType.Recipient => ResolveRecipientPlaceholderField(mapping.FieldKey, recipient, rowNumber),
                MappingSourceType.Organization => GetOrganizationFieldValue(org, mapping.FieldKey),
                MappingSourceType.Manual => manualValues.TryGetValue(mapping.PlaceholderTag, out var manual) ? manual : string.Empty,
                _ => string.Empty
            };

            if (string.IsNullOrEmpty(value)) unfilledTags.Add(mapping.PlaceholderTag);
            return value;
        }

        private static string ResolveRecipientPlaceholderField(string? fieldKey, Recipient? r, int rowNumber)
        {
            if (fieldKey == nameof(ExportFieldKey.RowNumber)) return rowNumber.ToString(CultureInfo.InvariantCulture);
            if (r is null || string.IsNullOrEmpty(fieldKey)) return string.Empty;
            if (!Enum.TryParse<ExportFieldKey>(fieldKey, out var key)) return string.Empty;

            return ResolveFieldValue(key, r, rowNumber);
        }

        private static string GetOrganizationFieldValue(OrganizationSettings? org, string? fieldName)
        {
            if (org is null) return string.Empty;

            return fieldName switch
            {
                "UnitNumber" => org.UnitNumber,
                "City" => org.City,
                "CommanderRank" => org.CommanderRank,
                "CommanderFullName" => org.CommanderFullName,
                "HrOfficerFullName" => org.HrOfficerFullName,
                "CommanderPosition" => org.CommanderPosition,
                "UnitFullName" => org.UnitFullName,
                _ => string.Empty
            };
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
            ExportFieldKey.ShortName => NameFormatter.ShortName(r.LastName, r.FirstName, r.MiddleName),
            ExportFieldKey.FitnessCategory => r.FitnessCategory ?? string.Empty,
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
    }
}
