using System.IO;
using ClosedXML.Excel;
using GenDoc.Models.TemplateBuilder;

namespace GenDoc.Services.Templates
{
    public record XlsxBuildResult(byte[] Content, int TemplateRowIndex, int TemplateSheetIndex = 0);

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
            var templateSheetIndex = 0;

            for (var sheetIndex = 0; sheetIndex < sheetNames.Count; sheetIndex++)
            {
                var blocks = document.Blocks.Where(b => b.SheetIndex == sheetIndex).ToList();
                var sheet = workbook.Worksheets.Add(SafeSheetName(sheetNames[sheetIndex], sheetIndex));
                var layout = TemplateSheetLayout.Compute(blocks);

                WriteSheet(sheet, blocks, layout, signatories);

                if (templateRowIndex == 0 && layout.TemplateRowIndex > 0)
                {
                    templateRowIndex = layout.TemplateRowIndex;
                    templateSheetIndex = sheetIndex;
                }
            }

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            return new XlsxBuildResult(stream.ToArray(), templateRowIndex, templateSheetIndex);
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
            var headerStyle = BlockStyleDefaults.ForTableHeader(style, BlockAlignment.Center);

            for (var i = 0; i < table.Columns.Count; i++)
            {
                var cell = sheet.Cell(row, i + 1);
                cell.Value = table.Columns[i].Title;
                Style(cell, headerStyle);
                Border(cell);

                sheet.Column(i + 1).Width = ColumnWidthChars(table.Columns[i].Title);
            }

            sheet.Row(row).Height = 30;
        }

        internal static double ColumnWidthChars(string title)
            => Math.Clamp(title.Length + 4, MinColumnWidth, MaxColumnWidth);

        private static void WriteTemplateRow(IXLWorksheet sheet, int row, TableSpec table, ResolvedBlockStyle style)
        {
            for (var i = 0; i < table.Columns.Count; i++)
            {
                var cell = sheet.Cell(row, i + 1);
                cell.SetValue(table.Columns[i].Cell);
                Style(cell, style);
                Border(cell);
            }
        }

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
