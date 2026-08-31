using System.Text.RegularExpressions;
using GenDoc.Models.Enums;
using GenDoc.Models.TemplateBuilder;

namespace GenDoc.Services.Templates
{
    public enum PreviewRunKind
    {
        Text,
        /// <summary>Значення підставлене з бази - акцентна заливка за легендою макета.</summary>
        DbValue,
        /// <summary>Тег, який оператор заповнить на генерації - застережлива заливка.</summary>
        ManualValue
    }

    public record PreviewRun(string Text, PreviewRunKind Kind);

    /// <summary>Або рядок тексту, або таблиця: у відомості прев'ю мусить показати
    /// сітку, у документі - суцільний текст, а порядок блоків спільний.</summary>
    public abstract record PreviewElement;

    /// <summary>Style - той самий розв'язаний стиль, який поклав би writer;
    /// окремого enum вирівнювання прев'ю більше не тримає, інакше він розійшовся
    /// б із моделлю.</summary>
    public record PreviewLine(IReadOnlyList<PreviewRun> Runs, ResolvedBlockStyle Style) : PreviewElement;

    public record PreviewTable(
        IReadOnlyList<string> Headers,
        IReadOnlyList<IReadOnlyList<PreviewRun>> Cells,
        ResolvedBlockStyle Style) : PreviewElement;

    /// <summary>Рядок аркуша у попередньому перегляді відомості. IsMerged - смуга
    /// на всю ширину (заголовок, абзац, підпис): у книзі це об'єднані клітинки,
    /// тож і на екрані вона одна.</summary>
    public record SheetPreviewRow(
        int Number,
        bool IsMerged,
        ResolvedBlockStyle Style,
        IReadOnlyList<IReadOnlyList<PreviewRun>> Cells,
        bool IsTableHeader = false,
        bool IsTemplateRow = false);

    /// <summary>Аркуш як його побачить оператор в Excel: літери колонок, номери
    /// рядків і вміст клітинок.</summary>
    public record SheetPreview(
        IReadOnlyList<string> ColumnLetters,
        IReadOnlyList<SheetPreviewRow> Rows);

    /// <summary>
    /// Прев'ю рендериться з тієї самої моделі блоків, що й .docx, а не з готових байтів:
    /// інакше воно неминуче відставало б від документа. Розкладка рядків тут мусить
    /// повторювати TemplateBlockDocxWriter - це його дзеркало на екрані.
    /// </summary>
    public static class TemplateBlockPreview
    {
        public const string ManualPlaceholder = "‹вводиться при генерації›";

        private const string SignatureRule = "_______________";

        private static readonly Regex PlaceholderRegex = new(@"\{\{[^{}]+\}\}", RegexOptions.Compiled);

        public static IReadOnlyList<PreviewElement> Build(
            TemplateBuilderDocument document,
            IReadOnlyDictionary<string, string> values,
            IReadOnlyDictionary<int, SignatoryInfo>? signatories = null)
        {
            var elements = new List<PreviewElement>();

            foreach (var block in document.Blocks)
                elements.AddRange(Render(block, values, signatories));

            return elements;
        }

        /// <summary>
        /// Прев'ю відомості як аркуша: рядки нумеруються тією самою розкладкою
        /// (TemplateSheetLayout), за якою TemplateBlockXlsxWriter кладе клітинки,
        /// тож номер рядка на екрані дорівнює номеру рядка у відкритій книзі.
        /// </summary>
        public static SheetPreview BuildSheet(
            IReadOnlyList<TemplateBlock> blocks,
            IReadOnlyDictionary<string, string> values,
            IReadOnlyDictionary<int, SignatoryInfo>? signatories = null)
        {
            var layout = TemplateSheetLayout.Compute(blocks);
            var rows = new List<SheetPreviewRow>();

            foreach (var placement in layout.Placements)
            {
                var block = blocks[placement.BlockIndex];

                var style = BlockStyleDefaults.Resolve(block.Kind, block.Style);

                if (block.Kind == TemplateBlockKind.Table && block.Table is { } table)
                {
                    // Ті самі два стилі, які TemplateBlockXlsxWriter кладе в шапку
                    // й у рядок-шаблон.
                    rows.Add(new SheetPreviewRow(
                        placement.FirstRow, IsMerged: false,
                        BlockStyleDefaults.ForTableHeader(style, BlockAlignment.Center),
                        table.Columns.Select(c => (IReadOnlyList<PreviewRun>)new[] { new PreviewRun(c.Title, PreviewRunKind.Text) }).ToList(),
                        IsTableHeader: true));

                    rows.Add(new SheetPreviewRow(
                        placement.FirstRow + 1, IsMerged: false, style,
                        table.Columns.Select(c => Substitute(c.Cell, values)).ToList(),
                        IsTemplateRow: true));

                    continue;
                }

                var row = placement.FirstRow;
                foreach (var line in BannerLines(block, values, signatories))
                {
                    rows.Add(new SheetPreviewRow(
                        row++, IsMerged: true, line.Style, new[] { line.Runs }));
                }
            }

            var letters = Enumerable.Range(1, layout.ColumnCount)
                .Select(TemplateSheetLayout.ColumnLetter)
                .ToList();

            return new SheetPreview(letters, rows);
        }

        /// <summary>Смуги на всю ширину - заголовок, гриф, абзац, підписи.</summary>
        private static IEnumerable<PreviewLine> BannerLines(
            TemplateBlock block,
            IReadOnlyDictionary<string, string> values,
            IReadOnlyDictionary<int, SignatoryInfo>? signatories)
            => Render(block, values, signatories).OfType<PreviewLine>();

        /// <summary>Усі теги документа - з них будується перелік мапінгів для підстановки.</summary>
        public static IReadOnlyList<string> CollectTags(TemplateBuilderDocument document)
        {
            var tags = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var block in document.Blocks)
            {
                foreach (var text in TextsOf(block))
                {
                    foreach (Match match in PlaceholderRegex.Matches(text))
                        if (seen.Add(match.Value)) tags.Add(match.Value);
                }
            }

            return tags;
        }

        private static IEnumerable<string> TextsOf(TemplateBlock block)
        {
            if (!string.IsNullOrEmpty(block.Text)) yield return block.Text;

            foreach (var line in block.Signatures ?? Array.Empty<SignatureLine>())
                if (!string.IsNullOrEmpty(line.Caption)) yield return line.Caption;

            foreach (var column in block.Table?.Columns ?? Array.Empty<TableColumn>())
            {
                yield return column.Title;
                yield return column.Cell;
            }
        }

        private static IEnumerable<PreviewElement> Render(
            TemplateBlock block,
            IReadOnlyDictionary<string, string> values,
            IReadOnlyDictionary<int, SignatoryInfo>? signatories)
        {
            // Вирівнювання і жирність беруться з того самого розв'язувача, що
            // годує writer'ів - прев'ю не має власної копії правил.
            var style = BlockStyleDefaults.Resolve(block.Kind, block.Style);

            switch (block.Kind)
            {
                case TemplateBlockKind.Header:
                    foreach (var line in SplitLines(block.Text))
                        yield return new PreviewLine(Substitute(line, values), style);
                    break;

                case TemplateBlockKind.Title:
                    yield return new PreviewLine(Substitute(block.Text ?? string.Empty, values), style);
                    break;

                // Розбивається на рядки, як Header і Paragraph: TemplateSheetLayout
                // резервує під блок LineCount(Text) рядків, і xlsx-writer стільки
                // й пише. Один рядок із переносом усередині означав, що на екрані
                // блок займає рядок, а в книзі - два (аудит 2026-08-28).
                case TemplateBlockKind.DateAndCity:
                    foreach (var line in SplitLines(block.Text))
                        yield return new PreviewLine(Substitute(line, values), style);
                    break;

                case TemplateBlockKind.Paragraph:
                    foreach (var line in SplitLines(block.Text))
                        yield return new PreviewLine(Substitute(line, values), style);
                    break;

                case TemplateBlockKind.Signatures:
                    foreach (var line in block.Signatures ?? Array.Empty<SignatureLine>())
                        yield return new PreviewLine(RenderSignature(line, values, signatories), style);
                    break;

                case TemplateBlockKind.Table when block.Table is { } table:
                    // Один рядок даних: у відомості рядок-шаблон клонується по
                    // одному на людину, тож прев'ю показує його на тестовій особі.
                    yield return new PreviewTable(
                        table.Columns.Select(c => c.Title).ToList(),
                        table.Columns.Select(c => Substitute(c.Cell, values)).ToList(),
                        style);
                    break;
            }
        }

        private static IReadOnlyList<PreviewRun> RenderSignature(
            SignatureLine line,
            IReadOnlyDictionary<string, string> values,
            IReadOnlyDictionary<int, SignatoryInfo>? signatories)
        {
            SignatoryInfo? person = null;
            if (line.RecipientId is int id && signatories is not null)
                signatories.TryGetValue(id, out person);

            var runs = new List<PreviewRun>();

            // Склеювання - дзеркало writer'ів: порожні частини викидаються, між
            // рештою рівно один пробіл. Прев'ю додавало пробіли безумовно й
            // показувало «Начальник курсу:  ______ » - подвійний після двокрапки
            // й хвостовий, - коли підписанта не обрано (аудит 2026-08-28).
            void Space()
            {
                if (runs.Count > 0) runs.Add(new PreviewRun(" ", PreviewRunKind.Text));
            }

            runs.AddRange(Substitute($"{line.Caption}:", values));

            if (!string.IsNullOrWhiteSpace(person?.Rank))
            {
                Space();
                runs.Add(new PreviewRun(person!.Rank, PreviewRunKind.DbValue));
            }

            // Порожні місця під ручний підпис - рівно те, що зробить і writer,
            // коли підписанта не обрано.
            Space();
            runs.Add(new PreviewRun(SignatureRule, PreviewRunKind.Text));

            if (!string.IsNullOrWhiteSpace(person?.ShortName))
            {
                Space();
                runs.Add(new PreviewRun(person!.ShortName, PreviewRunKind.DbValue));
            }

            return runs;
        }

        private static IReadOnlyList<PreviewRun> Substitute(
            string text, IReadOnlyDictionary<string, string> values)
        {
            var runs = new List<PreviewRun>();
            var position = 0;

            foreach (Match match in PlaceholderRegex.Matches(text))
            {
                if (match.Index > position)
                    runs.Add(new PreviewRun(text[position..match.Index], PreviewRunKind.Text));

                runs.Add(Resolve(match.Value, values));
                position = match.Index + match.Length;
            }

            if (position < text.Length)
                runs.Add(new PreviewRun(text[position..], PreviewRunKind.Text));

            return runs;
        }

        private static PreviewRun Resolve(string tag, IReadOnlyDictionary<string, string> values)
        {
            var (sourceType, _) = PlaceholderTagMaps.Classify(tag);
            if (sourceType == MappingSourceType.Manual)
                return new PreviewRun(ManualPlaceholder, PreviewRunKind.ManualValue);

            // Поле є в базі, але в тестової особи порожнє - показуємо сам тег, а не
            // порожнечу: інакше слово просто зникає, і причину не видно.
            var value = values.TryGetValue(tag, out var resolved) && !string.IsNullOrWhiteSpace(resolved)
                ? resolved
                : tag;

            return new PreviewRun(value, PreviewRunKind.DbValue);
        }

        private static IEnumerable<string> SplitLines(string? text)
        {
            if (string.IsNullOrEmpty(text)) return new[] { string.Empty };
            return text.Replace("\r\n", "\n").Split('\n');
        }
    }
}
