using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GenDoc.Models;

namespace GenDoc.Services.Generation
{
    public class DocumentGenerationService : IDocumentGenerationService
    {
        private static readonly Regex PlaceholderRegex = new(@"\{\{[^{}]+\}\}", RegexOptions.Compiled);
        private static readonly Regex BlockOpenRegex = new(@"^\{\{#([^{}]+)\}\}$", RegexOptions.Compiled);
        private static readonly Regex BlockCloseRegex = new(@"^\{\{/([^{}]+)\}\}$", RegexOptions.Compiled);

        private const string CountTag = "{{кількість_осіб}}";
        private const string IndexTag = "{{номер}}";
        private const string SeparatorTag = "{{роздільник}}";

        public GenerationItemResult GenerateOne(Template template, byte[] content, IDictionary<string, string> values, string outputPath)
        {
            try
            {
                // Копія байтів — MemoryStream(byte[]) інакше пише напряму у переданий
                // масив (кеш шаблону), а його не можна мутувати між генераціями.
                using var stream = new MemoryStream();
                stream.Write(content, 0, content.Length);
                stream.Position = 0;

                var unfilled = new List<string>();

                using (var doc = WordprocessingDocument.Open(stream, true))
                {
                    var mainPart = doc.MainDocumentPart;

                    if (mainPart?.Document?.Body is not null)
                        unfilled.AddRange(ReplaceInContainer(mainPart.Document.Body, values));

                    if (mainPart is not null)
                    {
                        foreach (var header in mainPart.HeaderParts)
                            unfilled.AddRange(ReplaceInContainer(header.Header, values));

                        foreach (var footer in mainPart.FooterParts)
                            unfilled.AddRange(ReplaceInContainer(footer.Footer, values));
                    }

                    mainPart?.Document?.Save();
                }

                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                File.WriteAllBytes(outputPath, stream.ToArray());

                return new GenerationItemResult(true, null, unfilled.Distinct().ToList());
            }
            catch (Exception ex)
            {
                return new GenerationItemResult(false, ex.Message, new List<string>());
            }
        }

        public GenerationItemResult GenerateGroup(
            Template template, byte[] content,
            IReadOnlyList<IDictionary<string, string>> perRecipientValues,
            IDictionary<string, string> sharedValues,
            string outputPath)
        {
            try
            {
                using var stream = new MemoryStream();
                stream.Write(content, 0, content.Length);
                stream.Position = 0;

                var unfilled = new List<string>();

                using (var doc = WordprocessingDocument.Open(stream, true))
                {
                    var mainPart = doc.MainDocumentPart;

                    if (mainPart?.Document?.Body is not null)
                        unfilled.AddRange(ProcessContainer(mainPart.Document.Body, perRecipientValues, sharedValues, template.Name));

                    if (mainPart is not null)
                    {
                        foreach (var header in mainPart.HeaderParts)
                            unfilled.AddRange(ProcessContainer(header.Header!, perRecipientValues, sharedValues, template.Name));

                        foreach (var footer in mainPart.FooterParts)
                            unfilled.AddRange(ProcessContainer(footer.Footer!, perRecipientValues, sharedValues, template.Name));
                    }

                    mainPart?.Document?.Save();
                }

                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                File.WriteAllBytes(outputPath, stream.ToArray());

                return new GenerationItemResult(true, null, unfilled.Distinct().ToList());
            }
            catch (Exception ex)
            {
                return new GenerationItemResult(false, ex.Message, new List<string>());
            }
        }

        // Розгортає повторюваний блок {{#name}}…{{/name}} у контейнері (тіло/шапка/підвал)
        // і замінює звичайні мітки поза блоком. Абзаци тіла блоку клонуються по одному
        // на кожен елемент perRecipientValues, у тому ж порядку, і вставляються перед
        // закриваючим маркером; обидва маркерні абзаци після цього видаляються цілком.
        private static List<string> ProcessContainer(
            OpenXmlCompositeElement container,
            IReadOnlyList<IDictionary<string, string>> perRecipientValues,
            IDictionary<string, string> sharedValues,
            string templateName)
        {
            var unfilled = new List<string>();

            var sharedWithCount = new Dictionary<string, string>(sharedValues, StringComparer.Ordinal)
            {
                [CountTag] = perRecipientValues.Count.ToString(CultureInfo.InvariantCulture)
            };

            var paragraphs = container.Descendants<Paragraph>().ToList();

            string? openBlockName = null;
            Paragraph? openParagraph = null;
            var bodyParagraphs = new List<Paragraph>();

            foreach (var paragraph in paragraphs)
            {
                var text = GetParagraphText(paragraph).Trim();
                var openMatch = BlockOpenRegex.Match(text);
                var closeMatch = BlockCloseRegex.Match(text);

                if (openMatch.Success)
                {
                    if (openBlockName is not null)
                        throw new InvalidOperationException(
                            $"Шаблон «{templateName}»: вкладені блоки не підтримуються: «{{{{#{openMatch.Groups[1].Value}}}}}» усередині «{{{{#{openBlockName}}}}}».");

                    openBlockName = openMatch.Groups[1].Value;
                    openParagraph = paragraph;
                    bodyParagraphs = new List<Paragraph>();
                    continue;
                }

                if (closeMatch.Success)
                {
                    var closeName = closeMatch.Groups[1].Value;
                    if (openBlockName is null)
                        throw new InvalidOperationException($"Шаблон «{templateName}»: закриваючий тег «{{{{/{closeName}}}}}» без відповідного «{{{{#{closeName}}}}}».");

                    if (closeName != openBlockName)
                        throw new InvalidOperationException(
                            $"Шаблон «{templateName}»: незбіжна назва блоку: очікували «{{{{/{openBlockName}}}}}», отримали «{{{{/{closeName}}}}}».");

                    ExpandBlock(openParagraph!, bodyParagraphs, paragraph, perRecipientValues, sharedWithCount,
                        unfilled, templateName, openBlockName);

                    openBlockName = null;
                    openParagraph = null;
                    bodyParagraphs = new List<Paragraph>();
                    continue;
                }

                if (openBlockName is not null)
                    bodyParagraphs.Add(paragraph);
                else
                    ReplaceInParagraph(paragraph, sharedWithCount, unfilled);
            }

            if (openBlockName is not null)
                throw new InvalidOperationException($"Шаблон «{templateName}»: блок «{{{{#{openBlockName}}}}}» не закрито тегом «{{{{/{openBlockName}}}}}».");

            return unfilled;
        }

        private static void ExpandBlock(
            Paragraph openParagraph, List<Paragraph> bodyParagraphs, Paragraph closeParagraph,
            IReadOnlyList<IDictionary<string, string>> perRecipientValues,
            IDictionary<string, string> sharedWithCount,
            List<string> unfilled,
            string templateName,
            string blockName)
        {
            if (bodyParagraphs.Any(p => !ReferenceEquals(p.Parent, openParagraph.Parent)))
                throw new InvalidOperationException(
                    $"Шаблон «{templateName}»: тіло блоку «{{{{#{blockName}}}}}» лежить усередині таблиці. "
                    + "Повторювані блоки в таблицях поки не підтримуються — винесіть рядки блоку "
                    + "з таблиці на рівень маркерів {{#…}}/{{/…}}.");

            var count = perRecipientValues.Count;
            for (var i = 0; i < count; i++)
            {
                var merged = new Dictionary<string, string>(sharedWithCount, StringComparer.Ordinal);
                foreach (var kv in perRecipientValues[i])
                    merged[kv.Key] = kv.Value;

                merged[IndexTag] = (i + 1).ToString(CultureInfo.InvariantCulture);
                merged[SeparatorTag] = i == count - 1 ? "." : ";";

                foreach (var original in bodyParagraphs)
                {
                    var clone = (Paragraph)original.CloneNode(true);
                    closeParagraph.InsertBeforeSelf(clone);
                    ReplaceInParagraph(clone, merged, unfilled);
                }
            }

            foreach (var original in bodyParagraphs)
                original.Remove();

            openParagraph.Remove();
            closeParagraph.Remove();
        }

        private static string GetParagraphText(Paragraph paragraph)
            => string.Concat(paragraph.Descendants<Text>().Select(t => t.Text));

        // Заміна зі збереженням позиції нетекстових вузлів (w:tab, w:br, w:drawing…):
        // рахуємо зміщення кожного текстового вузла в межах параграфа, знаходимо збіги
        // у зчепленому тексті, і пишемо результат назад лише у ті самі текстові вузли,
        // а не в один "плаский" рядок першого рану.
        private static List<string> ReplaceInContainer(OpenXmlCompositeElement? container, IDictionary<string, string> values)
        {
            var unfilled = new List<string>();
            if (container is null) return unfilled;

            foreach (var paragraph in container.Descendants<Paragraph>())
                ReplaceInParagraph(paragraph, values, unfilled);

            return unfilled;
        }

        internal static void ReplaceInParagraph(Paragraph paragraph, IDictionary<string, string> values, List<string> unfilled)
        {
            var textNodes = paragraph.Descendants<Text>().ToList();
            if (textNodes.Count == 0) return;

            var offsets = new int[textNodes.Count];
            var fullTextBuilder = new StringBuilder();
            for (var i = 0; i < textNodes.Count; i++)
            {
                offsets[i] = fullTextBuilder.Length;
                fullTextBuilder.Append(textNodes[i].Text);
            }

            var fullText = fullTextBuilder.ToString();
            if (!fullText.Contains("{{")) return;

            var matches = PlaceholderRegex.Matches(fullText);
            if (matches.Count == 0) return;

            var matchQueue = new Queue<Match>(matches);

            for (var i = 0; i < textNodes.Count; i++)
            {
                if (matchQueue.Count == 0) break;

                var node = textNodes[i];
                var original = node.Text;
                var start = offsets[i];
                var end = start + original.Length;

                var sb = new StringBuilder();
                var localPos = 0;

                while (matchQueue.Count > 0)
                {
                    var match = matchQueue.Peek();
                    if (match.Index >= end) break; // цей збіг стосується наступних вузлів

                    var matchStartLocal = Math.Max(0, match.Index - start);
                    if (matchStartLocal > localPos)
                    {
                        sb.Append(original, localPos, matchStartLocal - localPos);
                        localPos = matchStartLocal;
                    }

                    if (match.Index >= start)
                    {
                        // Цей вузол — вузол, де збіг починається: сюди йде все значення заміни.
                        if (values.TryGetValue(match.Value, out var value) && !string.IsNullOrEmpty(value))
                            sb.Append(value);
                        else
                            unfilled.Add(match.Value);
                    }

                    var matchEndLocal = Math.Min(original.Length, match.Index + match.Length - start);
                    localPos = matchEndLocal;

                    if (match.Index + match.Length <= end)
                    {
                        matchQueue.Dequeue();
                        continue; // у цьому ж вузлі може починатись наступний збіг
                    }

                    break; // збіг триває у наступному вузлі — лишаємо його в черзі
                }

                if (localPos < original.Length)
                    sb.Append(original, localPos, original.Length - localPos);

                node.Text = sb.ToString();
                node.Space = SpaceProcessingModeValues.Preserve;
            }
        }
    }
}
