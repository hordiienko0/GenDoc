using System.Text.RegularExpressions;
using GenDoc.Models.Enums;
using GenDoc.Models.TemplateBuilder;

namespace GenDoc.Services.Templates
{
    public enum PreviewRunKind
    {
        Text,
        DbValue,
        ManualValue
    }

    public record PreviewRun(string Text, PreviewRunKind Kind);

    public abstract record PreviewElement;

    public record PreviewLine(IReadOnlyList<PreviewRun> Runs, ResolvedBlockStyle Style) : PreviewElement;

    public record PreviewTable(
        IReadOnlyList<string> Headers,
        IReadOnlyList<IReadOnlyList<PreviewRun>> Cells,
        ResolvedBlockStyle Style) : PreviewElement;

    public record SheetPreviewRow(
        int Number,
        bool IsMerged,
        ResolvedBlockStyle Style,
        IReadOnlyList<IReadOnlyList<PreviewRun>> Cells,
        bool IsTableHeader = false,
        bool IsTemplateRow = false);

    public record SheetPreview(
        IReadOnlyList<string> ColumnLetters,
        IReadOnlyList<SheetPreviewRow> Rows);

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

        private static IEnumerable<PreviewLine> BannerLines(
            TemplateBlock block,
            IReadOnlyDictionary<string, string> values,
            IReadOnlyDictionary<int, SignatoryInfo>? signatories)
            => Render(block, values, signatories).OfType<PreviewLine>();

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
