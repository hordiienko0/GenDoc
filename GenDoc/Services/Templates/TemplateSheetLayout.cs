using GenDoc.Models.TemplateBuilder;

namespace GenDoc.Services.Templates
{
    public record SheetBlockPlacement(int BlockIndex, int FirstRow, int LastRow, int FirstColumn = 1, int LastColumn = 1)
    {
        public bool Overlaps(SheetBlockPlacement other)
            => LastRow >= FirstRow && other.LastRow >= other.FirstRow
               && FirstRow <= other.LastRow && other.FirstRow <= LastRow
               && FirstColumn <= other.LastColumn && other.FirstColumn <= LastColumn;

        public bool CoversRow(int row) => row >= FirstRow && row <= LastRow;
    }

    public record SheetLayoutConflict(int BlockIndex, int OtherBlockIndex, string Cell, int FirstRow, int LastRow);

    public record SheetLayout(
        IReadOnlyList<SheetBlockPlacement> Placements,
        int ColumnCount,
        int HeaderRowIndex,
        int TemplateRowIndex,
        int LastRow,
        int LastColumn,
        IReadOnlyList<SheetLayoutConflict> Conflicts)
    {
        public bool RowConflicts(int blockIndex, int row)
            => Conflicts.Any(c => (c.BlockIndex == blockIndex || c.OtherBlockIndex == blockIndex)
                                  && row >= c.FirstRow && row <= c.LastRow);
    }

    public static class TemplateSheetLayout
    {
        public static SheetLayout Compute(IReadOnlyList<TemplateBlock> blocks)
        {
            var table = blocks.FirstOrDefault(b => b.Kind == TemplateBlockKind.Table)?.Table;
            var columnCount = Math.Max(table?.Columns.Count ?? 0, 1);

            var placements = new List<SheetBlockPlacement>();
            var row = 1;
            var headerRowIndex = 0;
            var templateRowIndex = 0;

            for (var i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                var first = Math.Max(block.AnchorRow ?? row, 1);
                var firstColumn = Math.Max(block.AnchorColumn ?? 1, 1);
                var last = first + RowCount(block) - 1;
                var lastColumn = firstColumn + Math.Max(ColumnSpan(block, columnCount), 1) - 1;

                if (block.Kind == TemplateBlockKind.Table && block.Table is not null && headerRowIndex == 0)
                {
                    headerRowIndex = first;
                    templateRowIndex = first + 1;
                }

                placements.Add(new SheetBlockPlacement(i, first, last, firstColumn, lastColumn));
                row = last + 1;
            }

            var conflicts = new List<SheetLayoutConflict>();
            for (var later = 1; later < placements.Count; later++)
            {
                for (var earlier = 0; earlier < later; earlier++)
                {
                    var a = placements[later];
                    var b = placements[earlier];
                    if (!a.Overlaps(b)) continue;

                    var top = Math.Max(a.FirstRow, b.FirstRow);
                    var left = Math.Max(a.FirstColumn, b.FirstColumn);
                    var bottom = Math.Min(a.LastRow, b.LastRow);

                    conflicts.Add(new SheetLayoutConflict(later, earlier, $"{ColumnLetter(left)}{top}", top, bottom));
                }
            }

            var sheetLastRow = placements.Count == 0 ? 0 : placements.Max(p => p.LastRow);
            var sheetLastColumn = placements.Count == 0 ? columnCount : Math.Max(placements.Max(p => p.LastColumn), 1);

            return new SheetLayout(
                placements, columnCount, headerRowIndex, templateRowIndex, sheetLastRow, sheetLastColumn, conflicts);
        }

        private static int RowCount(TemplateBlock block) => block.Kind switch
        {
            TemplateBlockKind.Title => 1,
            TemplateBlockKind.Header or TemplateBlockKind.Paragraph or TemplateBlockKind.DateAndCity => LineCount(block.Text),
            TemplateBlockKind.Signatures => Math.Max(block.Signatures?.Count ?? 0, 0),
            TemplateBlockKind.Table when block.Table is not null => 2,
            _ => 0
        };

        private static int ColumnSpan(TemplateBlock block, int columnCount)
            => block.Kind == TemplateBlockKind.Table && block.Table is not null
                ? block.Table.Columns.Count
                : block.SpanColumns ?? columnCount;

        public static string ColumnLetter(int index)
        {
            if (index < 1) return string.Empty;

            var letters = string.Empty;
            while (index > 0)
            {
                var remainder = (index - 1) % 26;
                letters = (char)('A' + remainder) + letters;
                index = (index - 1) / 26;
            }

            return letters;
        }

        public static int ColumnIndex(string letters)
        {
            var index = 0;
            foreach (var c in letters.ToUpperInvariant())
            {
                if (c < 'A' || c > 'Z') return 0;
                index = index * 26 + (c - 'A' + 1);
            }

            return index;
        }

        public const int MaxColumn = 16384;
        public const int MaxRow = 1048576;

        private const string CyrillicLookalikes = "АВСЕНКМОРТХІ";
        private const string LatinLookalikes = "ABCEHKMOPTXI";

        public static bool TryParseCellAddress(string? text, out int? row, out int? column)
        {
            row = null;
            column = null;

            var cleaned = (text ?? string.Empty).Trim().ToUpperInvariant();
            if (cleaned.Length == 0) return true;

            var letters = 0;
            while (letters < cleaned.Length && !char.IsDigit(cleaned[letters])) letters++;

            var head = new string(cleaned[..letters]
                .Select(c => CyrillicLookalikes.IndexOf(c) is var i and >= 0 ? LatinLookalikes[i] : c)
                .ToArray());
            var tail = cleaned[letters..];

            if (head.Length > 3 || head.Any(c => c < 'A' || c > 'Z')) return false;
            if (tail.Length > 0 && (tail.Length > 7 || !tail.All(char.IsDigit))) return false;

            if (head.Length > 0)
            {
                var index = ColumnIndex(head);
                if (index < 1 || index > MaxColumn) return false;
                column = index;
            }

            if (tail.Length > 0)
            {
                var number = int.Parse(tail);
                if (number < 1 || number > MaxRow) return false;
                row = number;
            }

            return true;
        }

        public static string FormatCellAddress(int? row, int? column)
            => (column is int c ? ColumnLetter(c) : string.Empty) + (row is int r ? r.ToString() : string.Empty);

        public static string Range(SheetLayout layout)
        {
            if (layout.LastRow < 1 || layout.Placements.Count == 0) return string.Empty;

            var firstRow = layout.Placements.Min(p => p.FirstRow);
            var firstColumn = layout.Placements.Min(p => p.FirstColumn);

            return $"{ColumnLetter(firstColumn)}{firstRow}:{ColumnLetter(layout.LastColumn)}{layout.LastRow}";
        }

        public static int LineCount(string? text)
        {
            if (string.IsNullOrEmpty(text)) return 1;
            return text.Replace("\r\n", "\n").Split('\n').Length;
        }
    }
}
