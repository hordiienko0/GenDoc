using System.IO;
using ClosedXML.Excel;
using GenDoc.Models.TemplateBuilder;

namespace GenDoc.Services.Templates
{
    /// <summary>Байти книги + номер рядка-шаблону, який далі клонується по одному
    /// на людину. Рядок обчислює саме writer - тільки він знає, скільки рядків
    /// зайняли заголовки над таблицею.</summary>
    public record XlsxBuildResult(byte[] Content, int TemplateRowIndex);

    /// <summary>
    /// Збирає .xlsx-відомість із блоків конструктора. Як і для Word, це навмисно
    /// звичайний шаблон із {{тегами}}: рядок під шапкою таблиці - той самий
    /// «рядок-шаблон», який шукає ExportTemplateService.FindTemplateRow і клонує
    /// XlsxGenerationService. Окремого каналу генерації тут не з'являється.
    ///
    /// Розкладку рядків рахує TemplateSheetLayout - та сама, за якою конструктор
    /// підписує оператору, що куди ляже.
    /// </summary>
    public static class TemplateBlockXlsxWriter
    {
        private const string FontName = "Times New Roman";
        private const string SignatureRule = "_______________";

        private const double MinColumnWidth = 12;
        private const double MaxColumnWidth = 42;

        public static XlsxBuildResult Write(
            TemplateBuilderDocument document,
            IReadOnlyDictionary<int, SignatoryInfo>? signatories = null)
        {
            using var workbook = new XLWorkbook();

            var sheetNames = document.ResolvedSheetNames();
            var templateRowIndex = 0;

            for (var sheetIndex = 0; sheetIndex < sheetNames.Count; sheetIndex++)
            {
                var blocks = document.Blocks.Where(b => b.SheetIndex == sheetIndex).ToList();
                var sheet = workbook.Worksheets.Add(SafeSheetName(sheetNames[sheetIndex], sheetIndex));
                var layout = TemplateSheetLayout.Compute(blocks);

                WriteSheet(sheet, blocks, layout, signatories);

                // Рядок-шаблон у книзі один на всі аркуші (так влаштований
                // XlsxGenerationService), тому беремо перший знайдений.
                if (templateRowIndex == 0 && layout.TemplateRowIndex > 0)
                    templateRowIndex = layout.TemplateRowIndex;
            }

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            return new XlsxBuildResult(stream.ToArray(), templateRowIndex);
        }

        private static void WriteSheet(
            IXLWorksheet sheet,
            IReadOnlyList<TemplateBlock> blocks,
            SheetLayout layout,
            IReadOnlyDictionary<int, SignatoryInfo>? signatories)
        {
            foreach (var placement in layout.Placements)
            {
                var block = blocks[placement.BlockIndex];
                var row = placement.FirstRow;

                // Ті самі типові значення, що й у docx-writer'а: вирівнювання і
                // жирність більше не константи по місцю, а розв'язаний стиль блока.
                var style = BlockStyleDefaults.Resolve(block.Kind, block.Style);

                switch (block.Kind)
                {
                    case TemplateBlockKind.Title:
                        WriteBanner(sheet, row, layout.ColumnCount, block.Text, style);
                        break;

                    case TemplateBlockKind.Header:
                    case TemplateBlockKind.Paragraph:
                    case TemplateBlockKind.DateAndCity:
                        foreach (var line in SplitLines(block.Text))
                            WriteBanner(sheet, row++, layout.ColumnCount, line, style);
                        break;

                    case TemplateBlockKind.Signatures:
                        foreach (var line in block.Signatures ?? Array.Empty<SignatureLine>())
                            WriteBanner(sheet, row++, layout.ColumnCount,
                                RenderSignature(line, signatories), style);
                        break;

                    case TemplateBlockKind.Table when block.Table is not null:
                        WriteHeaderRow(sheet, placement.FirstRow, block.Table, style);
                        WriteTemplateRow(sheet, placement.FirstRow + 1, block.Table, style);
                        break;
                }
            }
        }

        /// <summary>Excel не приймає порожню назву, довшу за 31 символ і символи
        /// []:*?/\ - оператор може вписати будь-що, тож підчищаємо тут.</summary>
        internal static string SafeSheetName(string? name, int sheetIndex)
        {
            var cleaned = new string((name ?? string.Empty)
                .Where(c => !"[]:*?/\\".Contains(c))
                .ToArray())
                .Trim();

            if (string.IsNullOrWhiteSpace(cleaned))
                cleaned = sheetIndex == 0
                    ? TemplateBuilderDocument.DefaultSheetName
                    : $"{TemplateBuilderDocument.DefaultSheetName} {sheetIndex + 1}";

            return cleaned.Length > 31 ? cleaned[..31] : cleaned;
        }

        private static void WriteHeaderRow(IXLWorksheet sheet, int row, TableSpec table, ResolvedBlockStyle style)
        {
            // Шапка бере від блока гарнітуру, розмір і колір, але лишається
            // жирною й центрованою - це структура книги, а не оформлення.
            var headerStyle = BlockStyleDefaults.ForTableHeader(style, BlockAlignment.Center);

            for (var i = 0; i < table.Columns.Count; i++)
            {
                var cell = sheet.Cell(row, i + 1);
                cell.Value = table.Columns[i].Title;
                Style(cell, headerStyle);
                Border(cell);

                // Ширина за довжиною заголовка: AdjustToContents тут марний -
                // у рядку-шаблоні стоять {{теги}}, а не майбутні значення.
                sheet.Column(i + 1).Width = Math.Clamp(
                    table.Columns[i].Title.Length + 4, MinColumnWidth, MaxColumnWidth);
            }

            sheet.Row(row).Height = 30;
        }

        private static void WriteTemplateRow(IXLWorksheet sheet, int row, TableSpec table, ResolvedBlockStyle style)
        {
            for (var i = 0; i < table.Columns.Count; i++)
            {
                var cell = sheet.Cell(row, i + 1);
                // SetValue, а не Value: текст, що починається з "=" або схожий на
                // число, інакше пішов би у формулу/число, а нам потрібен рівно тег.
                cell.SetValue(table.Columns[i].Cell);
                Style(cell, style);
                Border(cell);
            }
        }

        /// <summary>Рядок на всю ширину таблиці - заголовок, абзац, підпис.</summary>
        private static void WriteBanner(
            IXLWorksheet sheet, int row, int columnCount, string? text, ResolvedBlockStyle style)
        {
            var cell = sheet.Cell(row, 1);
            cell.SetValue(text ?? string.Empty);
            Style(cell, style);

            if (columnCount > 1)
                sheet.Range(sheet.Cell(row, 1), sheet.Cell(row, columnCount)).Merge();
        }

        private static void Style(IXLCell cell, ResolvedBlockStyle style)
        {
            // Незаданим шрифт і кегль лишаються Times New Roman 11 - рівно те, що
            // writer ставив завжди; у книзі, на відміну від Word, «нічого не
            // задано» вивело б Calibri, тож типове тут явне.
            cell.Style.Font.FontName = style.FontFamily is { Length: > 0 } font ? font : FontName;
            cell.Style.Font.FontSize = style.FontSize ?? 11;
            cell.Style.Font.Bold = style.Bold;
            cell.Style.Font.Italic = style.Italic;

            if (style.Color is { Length: > 0 } color)
                cell.Style.Font.FontColor = XLColor.FromHtml($"#{color}");

            cell.Style.Alignment.WrapText = true;
            cell.Style.Alignment.Horizontal = Horizontal(style.Alignment);
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        /// <summary>Смуга в книзі - це об'єднані клітинки, і «по ширині» в них
        /// виглядає зламано, тож Justify лягає ліворуч. Так writer поводився й до
        /// появи форматування: абзац і дата писалися Left.</summary>
        private static XLAlignmentHorizontalValues Horizontal(BlockAlignment alignment) => alignment switch
        {
            BlockAlignment.Center => XLAlignmentHorizontalValues.Center,
            BlockAlignment.Right => XLAlignmentHorizontalValues.Right,
            _ => XLAlignmentHorizontalValues.Left
        };

        private static void Border(IXLCell cell)
        {
            cell.Style.Border.TopBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.RightBorder = XLBorderStyleValues.Thin;
        }

        // Дзеркало TemplateBlockDocxWriter.RenderSignature - підпис у відомості
        // виглядає так само, як у документі.
        private static string RenderSignature(
            SignatureLine line, IReadOnlyDictionary<int, SignatoryInfo>? signatories)
        {
            SignatoryInfo? person = null;
            if (line.RecipientId is int id && signatories is not null)
                signatories.TryGetValue(id, out person);

            var parts = new[] { $"{line.Caption}:", person?.Rank ?? string.Empty, SignatureRule, person?.ShortName ?? string.Empty }
                .Where(p => !string.IsNullOrWhiteSpace(p));

            return string.Join(" ", parts);
        }

        private static IEnumerable<string> SplitLines(string? text)
        {
            if (string.IsNullOrEmpty(text)) return new[] { string.Empty };
            return text.Replace("\r\n", "\n").Split('\n');
        }
    }
}
