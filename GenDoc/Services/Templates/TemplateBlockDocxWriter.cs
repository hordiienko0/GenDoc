using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GenDoc.Models.TemplateBuilder;

namespace GenDoc.Services.Templates
{
    public record SignatoryInfo(string Rank, string ShortName);

    public static class TemplateBlockDocxWriter
    {
        private const string SignatureRule = "_______________";

        public const string RepeatBlockName = "особи";

        public static byte[] Write(
            TemplateBuilderDocument document,
            IReadOnlyDictionary<int, SignatoryInfo>? signatories = null)
        {
            using var stream = new MemoryStream();

            using (var word = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
            {
                var main = word.AddMainDocumentPart();
                var body = new Body();

                foreach (var block in document.Blocks)
                {
                    if (block.Kind == TemplateBlockKind.Table)
                    {
                        if (block.Table is { } spec)
                            body.AppendChild(BuildTable(spec, BlockStyleDefaults.Resolve(block.Kind, block.Style)));

                        body.AppendChild(Text(string.Empty, BlockStyleDefaults.Marker));
                        continue;
                    }

                    foreach (var paragraph in Render(block, signatories))
                        body.AppendChild(paragraph);
                }

                main.Document = new Document(body);
                main.Document.Save();
            }

            return stream.ToArray();
        }

        private static IEnumerable<Paragraph> Render(
            TemplateBlock block,
            IReadOnlyDictionary<int, SignatoryInfo>? signatories)
        {
            var style = BlockStyleDefaults.Resolve(block.Kind, block.Style);

            switch (block.Kind)
            {
                case TemplateBlockKind.Header:
                    foreach (var line in SplitLines(block.Text))
                        yield return Text(line, style);
                    break;

                case TemplateBlockKind.Title:
                    yield return Text(block.Text ?? string.Empty, style);
                    break;

                case TemplateBlockKind.DateAndCity:
                    foreach (var line in SplitLines(block.Text))
                        yield return Text(line, style);
                    break;

                case TemplateBlockKind.Paragraph:
                    foreach (var line in SplitLines(block.Text))
                        yield return Text(line, style);
                    break;

                case TemplateBlockKind.Signatures:
                    foreach (var line in block.Signatures ?? Array.Empty<SignatureLine>())
                        yield return Text(RenderSignature(line, signatories), style);
                    break;

                case TemplateBlockKind.Table:
                    break;
            }
        }

        private static string RenderSignature(
            SignatureLine line,
            IReadOnlyDictionary<int, SignatoryInfo>? signatories)
        {
            SignatoryInfo? person = null;
            if (line.RecipientId is int id && signatories is not null)
                signatories.TryGetValue(id, out person);

            var rank = person?.Rank ?? string.Empty;
            var name = person?.ShortName ?? string.Empty;

            var parts = new[] { $"{line.Caption}:", rank, SignatureRule, name }
                .Where(p => !string.IsNullOrWhiteSpace(p));

            return string.Join(" ", parts);
        }

        private static Table BuildTable(TableSpec spec, ResolvedBlockStyle style)
        {
            var table = new Table(
                new TableProperties(
                    new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                    Borders()));

            var headerStyle = BlockStyleDefaults.ForTableHeader(style, BlockAlignment.Left);

            table.AppendChild(Row(spec.Columns.Select(c => c.Title), headerStyle));

            if (spec.RepeatPerPerson)
                table.AppendChild(MarkerRow($"{{{{#{RepeatBlockName}}}}}", spec.Columns.Count));

            table.AppendChild(Row(spec.Columns.Select(c => c.Cell), style));

            if (spec.RepeatPerPerson)
                table.AppendChild(MarkerRow($"{{{{/{RepeatBlockName}}}}}", spec.Columns.Count));

            return table;
        }

        private static TableRow Row(IEnumerable<string> texts, ResolvedBlockStyle style)
        {
            var row = new TableRow();

            foreach (var text in texts)
                row.AppendChild(Cell(text, style));

            return row;
        }

        private static TableRow MarkerRow(string marker, int columnCount)
        {
            var row = new TableRow();
            row.AppendChild(Cell(marker, BlockStyleDefaults.Marker));

            for (var i = 1; i < columnCount; i++)
                row.AppendChild(Cell(string.Empty, BlockStyleDefaults.Marker));

            return row;
        }

        private static TableCell Cell(string text, ResolvedBlockStyle style)
            => new(
                new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Auto }),
                Text(text, style));

        private static TableBorders Borders() => new(
            new TopBorder { Val = BorderValues.Single, Size = 4 },
            new BottomBorder { Val = BorderValues.Single, Size = 4 },
            new LeftBorder { Val = BorderValues.Single, Size = 4 },
            new RightBorder { Val = BorderValues.Single, Size = 4 },
            new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
            new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 });

        private static IEnumerable<string> SplitLines(string? text)
        {
            if (string.IsNullOrEmpty(text)) return new[] { string.Empty };
            return text.Replace("\r\n", "\n").Split('\n');
        }

        private static Paragraph Text(string text, ResolvedBlockStyle style)
        {
            var runProperties = new RunProperties();

            if (style.FontFamily is { Length: > 0 } font)
                runProperties.AppendChild(new RunFonts { Ascii = font, HighAnsi = font, ComplexScript = font });

            if (style.FontSize is { } size)
            {
                var halfPoints = ((int)Math.Round(size * 2)).ToString();
                runProperties.AppendChild(new FontSize { Val = halfPoints });
                runProperties.AppendChild(new FontSizeComplexScript { Val = halfPoints });
            }

            if (style.Bold) runProperties.AppendChild(new Bold());
            if (style.Italic) runProperties.AppendChild(new Italic());

            if (style.Color is { Length: > 0 } color)
                runProperties.AppendChild(new Color { Val = color });

            var run = new Run(runProperties);
            run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });

            var paragraphProperties = new ParagraphProperties(
                new Justification { Val = Justify(style.Alignment) });

            return new Paragraph(paragraphProperties, run);
        }

        private static JustificationValues Justify(BlockAlignment alignment) => alignment switch
        {
            BlockAlignment.Center => JustificationValues.Center,
            BlockAlignment.Right => JustificationValues.Right,
            BlockAlignment.Justify => JustificationValues.Both,
            _ => JustificationValues.Left
        };
    }
}
