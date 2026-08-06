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

        // Зарезервовані теги для «аркуш на кожну дату періоду».
        internal const string PeriodTag = "{{період}}";
        internal const string DateSheetTag = "{{дата_аркуша}}";

        public XlsxGenerationResult Generate(
            byte[] templateContent,
            int templateRowIndex,
            bool usesPlaceholders,
            List<ExportTemplateColumnMapping> mappings,
            IReadOnlyList<Recipient> roster,
            OrganizationSettings? org,
            IDictionary<string, string> manualValues,
            bool repeatSheetPerDate = false,
            string? courseOfficerSignature = null)
        {
            try
            {
                // Копія байтів — MemoryStream(byte[]) інакше пише напряму у переданий
                // масив (кеш шаблону), а його не можна мутувати між генераціями.
                using var stream = new MemoryStream();
                stream.Write(templateContent, 0, templateContent.Length);
                stream.Position = 0;

                using var workbook = new XLWorkbook(stream);
                var unfilledTags = new List<string>();

                if (!usesPlaceholders)
                {
                    // Стара header-driven поведінка — лише перший аркуш, як і раніше.
                    FillByHeaderColumns(workbook.Worksheets.First(), mappings, roster);
                }
                else if (repeatSheetPerDate)
                {
                    if (!manualValues.TryGetValue(PeriodTag, out var periodRaw) || string.IsNullOrWhiteSpace(periodRaw))
                    {
                        return new XlsxGenerationResult(
                            false, null,
                            $"Не заповнено тег {PeriodTag} — потрібен для «аркуш на кожну дату періоду».",
                            Array.Empty<string>());
                    }

                    List<DateOnly> dates;
                    try
                    {
                        dates = ParsePeriodDates(periodRaw);
                    }
                    catch (FormatException ex)
                    {
                        return new XlsxGenerationResult(false, null, ex.Message, Array.Empty<string>());
                    }

                    var templateSheet = workbook.Worksheets.First();
                    var templateSheetName = templateSheet.Name;
                    var clonedNames = new List<(string Name, DateOnly Date)>();

                    foreach (var date in dates)
                    {
                        var sheetName = date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
                        templateSheet.CopyTo(sheetName);
                        clonedNames.Add((sheetName, date));
                    }

                    workbook.Worksheet(templateSheetName).Delete();

                    foreach (var (sheetName, date) in clonedNames)
                    {
                        var perSheetValues = new Dictionary<string, string>(manualValues)
                        {
                            [DateSheetTag] = date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
                        };
                        FillSheetByPlaceholders(
                            workbook.Worksheet(sheetName), mappings, templateRowIndex, roster, org,
                            courseOfficerSignature, perSheetValues, unfilledTags);
                    }

                    // Інші (довідкові) аркуші книги — заповнити один раз, без клонування.
                    var clonedNameSet = new HashSet<string>(clonedNames.Select(c => c.Name));
                    foreach (var sheet in workbook.Worksheets.Where(s => !clonedNameSet.Contains(s.Name)))
                    {
                        FillSheetByPlaceholders(
                            sheet, mappings, templateRowIndex, roster, org,
                            courseOfficerSignature, manualValues, unfilledTags);
                    }
                }
                else
                {
                    // Кожен аркуш книги — аркуші без тегів у templateRowIndex просто
                    // не отримають клонованих рядків (перевірка всередині), але й досі
                    // отримають підстановку "зовнішніх" (org/manual) тегів, якщо є.
                    foreach (var sheet in workbook.Worksheets)
                    {
                        FillSheetByPlaceholders(
                            sheet, mappings, templateRowIndex, roster, org,
                            courseOfficerSignature, manualValues, unfilledTags);
                    }
                }

                using var output = new MemoryStream();
                workbook.SaveAs(output);

                return new XlsxGenerationResult(true, output.ToArray(), null, unfilledTags.Distinct().ToList());
            }
            catch (Exception ex)
            {
                return new XlsxGenerationResult(false, null, ex.Message, Array.Empty<string>());
            }
        }

        // "03.08.2026-07.08.2026, 09.08.2026" → відсортований список унікальних дат.
        // Діапазони через дефіс, перелік через кому; кожна дата — дд.мм.рррр.
        internal static List<DateOnly> ParsePeriodDates(string raw)
        {
            var segments = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0)
            {
                throw new FormatException(
                    $"Тег {PeriodTag} порожній — вкажіть дати у форматі «дд.мм.рррр-дд.мм.рррр, дд.мм.рррр».");
            }

            var result = new HashSet<DateOnly>();

            foreach (var segment in segments)
            {
                var dashIndex = segment.IndexOf('-');
                if (dashIndex < 0)
                {
                    if (!DateOnly.TryParseExact(segment, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var single))
                    {
                        throw new FormatException(
                            $"Не розпізнано дату «{segment}» у тегу {PeriodTag} — очікується формат дд.мм.рррр.");
                    }
                    result.Add(single);
                }
                else
                {
                    var startText = segment[..dashIndex].Trim();
                    var endText = segment[(dashIndex + 1)..].Trim();

                    if (!DateOnly.TryParseExact(startText, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
                        !DateOnly.TryParseExact(endText, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
                    {
                        throw new FormatException(
                            $"Не розпізнано діапазон «{segment}» у тегу {PeriodTag} — очікується дд.мм.рррр-дд.мм.рррр.");
                    }

                    if (end < start)
                        throw new FormatException($"У діапазоні «{segment}» кінцева дата раніша за початкову.");

                    for (var d = start; d <= end; d = d.AddDays(1))
                        result.Add(d);
                }
            }

            return result.OrderBy(d => d).ToList();
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
                    var value = ResolveFieldValue(fieldKey, item, rowNumber, mapping.ColumnIndex);

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

        // Один аркуш: якщо в templateRowIndex справді є хоч один з очікуваних тегів —
        // клонує рядок по одному на людину (як і раніше). Якщо ні — це довідковий
        // аркуш (напр. "Слухачі"): рядки не чіпаємо, лише підставляємо org/manual-теги
        // будь-де на аркуші.
        private static void FillSheetByPlaceholders(
            IXLWorksheet sheet,
            List<ExportTemplateColumnMapping> mappings,
            int templateRowIndex,
            IReadOnlyList<Recipient> items,
            OrganizationSettings? org,
            string? courseOfficerSignature,
            IDictionary<string, string> manualValues,
            List<string> unfilledTags)
        {
            var rowMappings = mappings.Where(m => m.ColumnIndex > 0).ToList();
            var outsideMappings = mappings.Where(m => m.ColumnIndex == 0).ToList();

            var t = templateRowIndex;
            var n = items.Count;

            var hasRowTemplate = n > 0 && rowMappings.Any(m =>
                sheet.Cell(t, m.ColumnIndex).GetString().Contains(m.PlaceholderTag, StringComparison.Ordinal));

            var excludeFrom = t;
            var excludeTo = t - 1; // порожній діапазон — за замовчуванням нічого не виключає

            if (hasRowTemplate)
            {
                if (n > 1) InsertClonedRows(sheet, t, n - 1);

                var mappingsByColumn = rowMappings.GroupBy(m => m.ColumnIndex).ToList();
                var gradeRandomColumns = rowMappings
                    .Where(m => m.FieldKey == nameof(ExportFieldKey.GradeRandom34))
                    .Select(m => m.ColumnIndex)
                    .ToList();

                for (var i = 0; i < n; i++)
                {
                    var person = items[i];
                    var targetRow = t + i;
                    var rowNumber = i + 1;

                    string ResolveOne(ExportTemplateColumnMapping mapping)
                    {
                        // Загальна оцінка — середнє по GradeRandom34-колонках ЦЬОГО рядка,
                        // а не самостійне поле r-> ... — потребує сусідніх колонок мапінгу.
                        if (mapping.SourceType == MappingSourceType.Recipient &&
                            mapping.FieldKey == nameof(ExportFieldKey.GradeOverall34))
                        {
                            if (gradeRandomColumns.Count == 0)
                            {
                                unfilledTags.Add(mapping.PlaceholderTag);
                                return string.Empty;
                            }

                            var avg = gradeRandomColumns.Average(col => ComputeGradeRandom34(person.Id, col));
                            return Math.Round(avg, MidpointRounding.AwayFromZero)
                                .ToString(CultureInfo.InvariantCulture);
                        }

                        if (mapping.FieldKey == nameof(ExportFieldKey.CourseOfficerSignature))
                            return ResolveCourseOfficerSignature(courseOfficerSignature, mapping, unfilledTags);

                        return ResolvePlaceholderValue(mapping, person, org, manualValues, rowNumber, unfilledTags);
                    }

                    foreach (var group in mappingsByColumn)
                    {
                        var cell = sheet.Cell(targetRow, group.Key);
                        var text = cell.GetString();

                        if (group.Count() == 1 && text.Trim() == group.First().PlaceholderTag)
                        {
                            var raw = ResolveOne(group.First());
                            AssignTypedOrString(cell, raw);
                            continue;
                        }

                        foreach (var mapping in group)
                        {
                            var raw = ResolveOne(mapping);
                            text = text.Replace(mapping.PlaceholderTag, raw);
                        }

                        cell.Value = text;
                    }
                }

                excludeFrom = t;
                excludeTo = t + n - 1;
            }

            SubstituteOutsideTags(sheet, outsideMappings, org, courseOfficerSignature, manualValues, unfilledTags, excludeFrom, excludeTo);
            CollectResidualUnfilledTags(sheet, unfilledTags);
        }

        // Стабільна "випадкова" оцінка 3 або 4: той самий (RecipientId, ColumnIndex)
        // завжди дає те саме число — і між перегенераціями, і між колонками рядка
        // відрізняється, бо колонка теж входить у seed.
        private static int ComputeGradeRandom34(int recipientId, int columnIndex)
        {
            var seed = HashCode.Combine(recipientId, columnIndex);
            return new Random(seed).Next(3, 5);
        }

        private static string ResolveCourseOfficerSignature(
            string? courseOfficerSignature, ExportTemplateColumnMapping mapping, List<string> unfilledTags)
        {
            if (string.IsNullOrEmpty(courseOfficerSignature))
            {
                unfilledTags.Add(mapping.PlaceholderTag);
                return string.Empty;
            }

            return courseOfficerSignature;
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
            string? courseOfficerSignature,
            IDictionary<string, string> manualValues,
            List<string> unfilledTags,
            int excludeFromRow,
            int excludeToRow)
        {
            if (outsideMappings.Count == 0) return;

            var usedRange = sheet.RangeUsed();
            if (usedRange is null) return;

            var cellsOutsideRowTemplate = usedRange.CellsUsed()
                .Where(c => c.Address.RowNumber < excludeFromRow || c.Address.RowNumber > excludeToRow)
                .ToList();

            // Довідкові аркуші (напр. "Слухачі") не містять теги, мапнуті лише
            // для іншого аркуша — рахуємо "незаповненим" тег лише тоді, коли він
            // справді присутній хоч в одній клітинці ЦЬОГО аркуша.
            var relevantMappings = outsideMappings
                .Where(m => cellsOutsideRowTemplate.Any(c => c.GetString().Contains(m.PlaceholderTag, StringComparison.Ordinal)))
                .ToList();

            if (relevantMappings.Count == 0) return;

            var valueByTag = new Dictionary<string, string>();
            foreach (var mapping in relevantMappings)
            {
                var value = mapping.FieldKey == nameof(ExportFieldKey.CourseOfficerSignature)
                    ? courseOfficerSignature ?? string.Empty
                    : mapping.SourceType switch
                    {
                        MappingSourceType.Organization => GetOrganizationFieldValue(org, mapping.FieldKey),
                        MappingSourceType.Manual => manualValues.TryGetValue(mapping.PlaceholderTag, out var manual) ? manual : string.Empty,
                        _ => string.Empty
                    };

                if (string.IsNullOrEmpty(value)) unfilledTags.Add(mapping.PlaceholderTag);
                valueByTag[mapping.PlaceholderTag] = value;
            }

            foreach (var cell in cellsOutsideRowTemplate)
            {
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
                MappingSourceType.Recipient => ResolveRecipientPlaceholderField(mapping.FieldKey, recipient, rowNumber, mapping.ColumnIndex),
                MappingSourceType.Organization => GetOrganizationFieldValue(org, mapping.FieldKey),
                MappingSourceType.Manual => manualValues.TryGetValue(mapping.PlaceholderTag, out var manual) ? manual : string.Empty,
                _ => string.Empty
            };

            if (string.IsNullOrEmpty(value)) unfilledTags.Add(mapping.PlaceholderTag);
            return value;
        }

        private static string ResolveRecipientPlaceholderField(string? fieldKey, Recipient? r, int rowNumber, int columnIndex)
        {
            if (fieldKey == nameof(ExportFieldKey.RowNumber)) return rowNumber.ToString(CultureInfo.InvariantCulture);
            if (r is null || string.IsNullOrEmpty(fieldKey)) return string.Empty;
            if (!Enum.TryParse<ExportFieldKey>(fieldKey, out var key)) return string.Empty;

            return ResolveFieldValue(key, r, rowNumber, columnIndex);
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

        private static string ResolveFieldValue(ExportFieldKey key, Recipient r, int rowNumber, int columnIndex) => key switch
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
            ExportFieldKey.WeaponName => r.Weapons.OrderBy(w => w.Id).FirstOrDefault()?.Name ?? string.Empty,
            ExportFieldKey.WeaponSerialNumber => r.Weapons.OrderBy(w => w.Id).FirstOrDefault()?.SerialNumber ?? string.Empty,
            ExportFieldKey.WeaponFull => r.Weapons.OrderBy(w => w.Id).FirstOrDefault() is { } w
                ? $"{w.Name} № {w.SerialNumber}".Trim()
                : string.Empty,
            ExportFieldKey.GradeRandom34 => ComputeGradeRandom34(r.Id, columnIndex).ToString(CultureInfo.InvariantCulture),
            // GradeOverall34/CourseOfficerSignature обробляються окремо в
            // FillSheetByPlaceholders — потребують сусідніх колонок рядка /
            // даних поза поточним ростером відповідно.
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
