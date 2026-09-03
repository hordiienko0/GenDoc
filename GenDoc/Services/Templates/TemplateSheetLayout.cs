using GenDoc.Models.TemplateBuilder;

namespace GenDoc.Services.Templates
{
    public record SheetBlockPlacement(int BlockIndex, int FirstRow, int LastRow);

    public record SheetLayout(
        IReadOnlyList<SheetBlockPlacement> Placements,
        int ColumnCount,
        int HeaderRowIndex,
        int TemplateRowIndex,
        int LastRow);

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
                var first = row;

                switch (block.Kind)
                {
                    case TemplateBlockKind.Title:
                        row++;
                        break;

                    case TemplateBlockKind.Header:
                    case TemplateBlockKind.Paragraph:
                    case TemplateBlockKind.DateAndCity:
                        row += LineCount(block.Text);
                        break;

                    case TemplateBlockKind.Signatures:
                        row += Math.Max(block.Signatures?.Count ?? 0, 0);
                        break;

                    case TemplateBlockKind.Table when block.Table is not null:
                        headerRowIndex = row;
                        templateRowIndex = row + 1;
                        row += 2;
                        break;
                }

                placements.Add(new SheetBlockPlacement(i, first, row - 1));
            }

            return new SheetLayout(placements, columnCount, headerRowIndex, templateRowIndex, row - 1);
        }

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

        public static string Range(SheetLayout layout)
            => layout.LastRow < 1
                ? string.Empty
                : $"A1:{ColumnLetter(layout.ColumnCount)}{layout.LastRow}";

        public static int LineCount(string? text)
        {
            if (string.IsNullOrEmpty(text)) return 1;
            return text.Replace("\r\n", "\n").Split('\n').Length;
        }
    }
}
