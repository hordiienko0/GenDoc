using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GenDoc.Models;
using GenDoc.Services.Templates;

namespace GenDoc.Services.Generation
{
    public class DocumentGenerationService : IDocumentGenerationService
    {
        private static readonly Regex PlaceholderRegex = new(@"\{\{[^{}]+\}\}", RegexOptions.Compiled);

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

        // Розгортає повторювані блоки {{#name}}…{{/name}} у контейнері
        // (тіло/шапка/підвал) і замінює звичайні мітки поза блоками. Одиниця
        // повторення — абзац на рівні документа або рядок усередині таблиці.
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

            ProcessSiblings(
                BlockStructure.BlockChildren(container).ToList(),
                perRecipientValues, sharedWithCount, unfilled, templateName, insideTable: false);

            // Елементи, що не є ні абзацом, ні таблицею (елементи керування
            // вмістом Word, розриви секцій), у блоках участі не беруть — але їхні
            // абзаци мусять діставати підстановку спільних тегів так само, як до
            // переходу на обхід блокових дітей. Інакше груповий режим мовчки
            // лишав би там сирі {{теги}}, тоді як GenerateOne їх заповнює.
            foreach (var other in container.ChildElements.Where(e => e is not Paragraph && e is not Table))
                ReplaceInElement(other, sharedWithCount, unfilled);

            return unfilled;
        }

        // Кінцевий автомат блоку над послідовністю сусідів одного батька.
        // Викликається двічі: для блокових дітей контейнера і для рядків
        // таблиці. Тіло блоку за побудовою складається з сусідів маркерів, тож
        // окремої перевірки «однакового батька» більше не потрібно.
        //
        // Правило, що знімає двозначність: поки відкрито блок рівня документа,
        // таблиця — це вміст блоку і клонується цілком; усередину таблиці по
        // маркерні рядки заходимо лише тоді, коли блок не відкрито.
        private static void ProcessSiblings(
            List<OpenXmlElement> siblings,
            IReadOnlyList<IDictionary<string, string>> perRecipientValues,
            IDictionary<string, string> sharedWithCount,
            List<string> unfilled,
            string templateName,
            bool insideTable)
        {
            string? openName = null;
            OpenXmlElement? openElement = null;
            var body = new List<OpenXmlElement>();

            foreach (var element in siblings)
            {
                var text = BlockStructure.MarkerText(element);

                if (BlockStructure.OpenName(text) is { } opened)
                {
                    if (openName is not null)
                        throw new InvalidOperationException(
                            $"Шаблон «{templateName}»: вкладені блоки не підтримуються: «{{{{#{opened}}}}}» усередині «{{{{#{openName}}}}}».");

                    openName = opened;
                    openElement = element;
                    body = new List<OpenXmlElement>();
                    continue;
                }

                if (BlockStructure.CloseName(text) is { } closed)
                {
                    if (openName is null)
                        throw new InvalidOperationException(
                            $"Шаблон «{templateName}»: закриваючий тег «{{{{/{closed}}}}}» без відповідного «{{{{#{closed}}}}}».");

                    if (closed != openName)
                        throw new InvalidOperationException(
                            $"Шаблон «{templateName}»: незбіжна назва блоку: очікували «{{{{/{openName}}}}}», отримали «{{{{/{closed}}}}}».");

                    ExpandBlock(openElement!, body, element, perRecipientValues, sharedWithCount,
                        unfilled, templateName, openName);

                    openName = null;
                    openElement = null;
                    body = new List<OpenXmlElement>();
                    continue;
                }

                if (openName is not null)
                {
                    body.Add(element);
                    continue;
                }

                if (!insideTable && element is Table table)
                {
                    ProcessSiblings(
                        BlockStructure.Rows(table).Cast<OpenXmlElement>().ToList(),
                        perRecipientValues, sharedWithCount, unfilled, templateName, insideTable: true);
                    continue;
                }

                ReplaceInElement(element, sharedWithCount, unfilled);
            }

            if (openName is not null)
                throw new InvalidOperationException(
                    $"Шаблон «{templateName}»: блок «{{{{#{openName}}}}}» не закрито тегом «{{{{/{openName}}}}}».");
        }

        private static void ExpandBlock(
            OpenXmlElement openElement, List<OpenXmlElement> body, OpenXmlElement closeElement,
            IReadOnlyList<IDictionary<string, string>> perRecipientValues,
            IDictionary<string, string> sharedWithCount,
            List<string> unfilled,
            string templateName,
            string blockName)
        {
            var count = perRecipientValues.Count;
            for (var i = 0; i < count; i++)
            {
                var merged = new Dictionary<string, string>(sharedWithCount, StringComparer.Ordinal);
                foreach (var kv in perRecipientValues[i])
                    merged[kv.Key] = kv.Value;

                merged[IndexTag] = (i + 1).ToString(CultureInfo.InvariantCulture);
                merged[SeparatorTag] = i == count - 1 ? "." : ";";

                foreach (var original in body)
                {
                    var clone = original.CloneNode(true);
                    closeElement.InsertBeforeSelf(clone);
                    ReplaceInElement(clone, merged, unfilled);
                }
            }

            foreach (var original in body)
                original.Remove();

            openElement.Remove();
            closeElement.Remove();
        }

        // Заміна в усіх абзацах елемента: для абзацу це він сам, для рядка чи
        // таблиці — абзаци всіх його комірок.
        private static void ReplaceInElement(
            OpenXmlElement element, IDictionary<string, string> values, List<string> unfilled)
        {
            if (element is Paragraph paragraph)
            {
                ReplaceInParagraph(paragraph, values, unfilled);
                return;
            }

            foreach (var inner in element.Descendants<Paragraph>())
                ReplaceInParagraph(inner, values, unfilled);
        }

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
