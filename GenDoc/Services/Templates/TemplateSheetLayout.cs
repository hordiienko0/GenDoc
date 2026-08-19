using GenDoc.Models.TemplateBuilder;

namespace GenDoc.Services.Templates
{
    /// <summary>Куди на аркуші ліг блок: перший і останній зайняті рядки.</summary>
    public record SheetBlockPlacement(int BlockIndex, int FirstRow, int LastRow);

    /// <summary>Розкладка аркуша: скільки колонок, які рядки зайняв кожен блок,
    /// де шапка таблиці й де рядок-шаблон (0 - таблиці на аркуші немає).</summary>
    public record SheetLayout(
        IReadOnlyList<SheetBlockPlacement> Placements,
        int ColumnCount,
        int HeaderRowIndex,
        int TemplateRowIndex,
        int LastRow);

    /// <summary>
    /// Одна розкладка на двох споживачів: за нею TemplateBlockXlsxWriter кладе
    /// клітинки, і за нею ж конструктор підписує, який блок які рядки займе.
    /// Тримати це в одному місці обов'язково - інакше підпис на екрані й реальна
    /// книга розійдуться рівно так, як колись розійшлись мапи тегів.
    /// </summary>
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
                        // Шапка колонок і під нею рядок-шаблон - рівно два рядки.
                        headerRowIndex = row;
                        templateRowIndex = row + 1;
                        row += 2;
                        break;
                }

                placements.Add(new SheetBlockPlacement(i, first, row - 1));
            }

            return new SheetLayout(placements, columnCount, headerRowIndex, templateRowIndex, row - 1);
        }

        /// <summary>1 → A, 27 → AA. Потрібно, щоб оператор бачив колонку так само,
        /// як побачить її у відкритому Excel.</summary>
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

        /// <summary>Діапазон на кшталт «A1:C4» - те, що оператор побачить у Excel.</summary>
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
