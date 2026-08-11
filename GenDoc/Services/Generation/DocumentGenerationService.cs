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

                    // Перевірка йде ДО будь-якої підстановки: GenerateOne не вміє
                    // розгортати повторювані блоки, тож {{#…}}/{{/…}} тут — ознака,
                    // що шаблон насправді груповий (одна довжина списку — один
                    // документ), а не персональний. Раніше ReplaceInParagraph мовчки
                    // стирав такий маркер як звичайний незаповнений тег, і людина
                    // отримувала документ без списку і без жодного попередження.
                    // Той самий детектор (BlockStructure.EmbeddedMarkerRegex), яким
                    // на груповому шляху користується GuardResidualMarkers.
                    var residualMarker = FindResidualMarkerInDocument(mainPart);
                    if (residualMarker is not null)
                        return new GenerationItemResult(
                            false, BuildStrayBlockMarkerMessage(template.Name, residualMarker), new List<string>());

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

            // Маркер, якого структурний обхід вище не розгорнув, лишається в
            // тексті буквально — і ReplaceInElement усередині ProcessSiblings
            // (рядок, що сам не є маркером), і ReplaceInElement у ExpandBlock
            // (підстановка в клонах) викликаються з preserveMarkers: true,
            // тож не стирають його як звичайний незаповнений тег. Перевірка
            // йде саме тут: усі маркери, які рушій УМІВ розгорнути, вже
            // прибрані разом з абзацом/рядком; усе, що лишилось — маркер там,
            // куди обхід не сягає, або в абзаці, який маркером-елементом не є.
            // Обов'язково до підмітання нижче: воно теж не стирає маркери
            // (той самий preserveMarkers: true), але порядок тримаємо явним,
            // щоб це не залежало від деталі реалізації заміни.
            GuardResidualMarkers(container, templateName);

            // Повне підмітання: дістає теги на будь-якій глибині, куди
            // структурний обхід (блокові діти контейнера, рядки однієї
            // таблиці) не заходить — усередині елемента керування вмістом
            // Word, у таблиці, вкладеній в комірку. ReplaceInParagraph рано
            // виходить, якщо в тексті нема "{{", тож уже підставлені абзаци
            // він не чіпає; unfilled.Distinct() у GenerateGroup прибирає
            // можливе подвійне повідомлення. preserveMarkers: true — це
            // груповий шлях, у нього є GuardResidualMarkers вище; GenerateOne
            // такого вартового не має, тож там маркери за замовчуванням і
            // надалі стираються, як до цієї гілки.
            unfilled.AddRange(ReplaceInContainer(container, sharedWithCount, preserveMarkers: true));

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

                ReplaceInElement(element, sharedWithCount, unfilled, preserveMarkers: true);
            }

            if (openName is not null)
            {
                var closedInsideTable = body.OfType<Table>()
                    .SelectMany(BlockStructure.Rows)
                    .Any(row => BlockStructure.CloseName(BlockStructure.MarkerText(row)) == openName);

                if (closedInsideTable)
                    throw new InvalidOperationException(
                        $"Шаблон «{templateName}»: блок «{{{{#{openName}}}}}» відкрито абзацом, "
                        + "а закрито рядком таблиці. Маркери мають бути на одному рівні — "
                        + "або обидва абзацами, або обидва рядками однієї таблиці.");

                if (insideTable)
                    throw new InvalidOperationException(
                        $"Шаблон «{templateName}»: блок «{{{{#{openName}}}}}» відкрито рядком таблиці "
                        + $"й не закрито в ній же — додайте рядок «{{{{/{openName}}}}}» у ту саму таблицю.");

                throw new InvalidOperationException(
                    $"Шаблон «{templateName}»: блок «{{{{#{openName}}}}}» не закрито тегом «{{{{/{openName}}}}}».");
            }
        }

        // Обхід не заходить у таблицю, поки відкрито блок рівня документа, тож
        // маркерний рядок усередині такого блоку інакше лишився б літеральним
        // текстом у кожній копії.
        private static void GuardNestedRowBlocks(
            List<OpenXmlElement> body, string templateName, string blockName)
        {
            var nested = body.OfType<Table>()
                .SelectMany(BlockStructure.Rows)
                .Select(row => BlockStructure.OpenName(BlockStructure.MarkerText(row)))
                .FirstOrDefault(name => name is not null);

            if (nested is not null)
                throw new InvalidOperationException(
                    $"Шаблон «{templateName}»: вкладені блоки не підтримуються — «{{{{#{nested}}}}}» "
                    + $"у рядку таблиці всередині блоку «{{{{#{blockName}}}}}».");
        }

        // Клонування рядка з вертикальним об'єднанням дало б N продовжень merge
        // і зіпсовану таблицю. Відмовляємо, а не знімаємо об'єднання тихо:
        // документ, який виглядає готовим, гірший за явну відмову.
        //
        // Область перевірки — лише комірки рядків, що САМІ є тілом блоку (body
        // складається з TableRow, коли блок відкрито рядком). Якщо тілом блоку
        // є ціла таблиця (блок рівня документа навколо таблиці) або рядок
        // містить вкладену таблицю зі своїм merge, це об'єднання не зазнає
        // жодного псування від клонування — таблиця чи вкладена таблиця
        // копіюється цілком і лишається самодостатньою в кожній копії.
        // Ширша перевірка (по всіх Descendants) відмовляла і в цих випадках
        // теж — а обʼєднана шапка таблиці майже завжди є в списках особового
        // складу, тож це виявилось наддумкою.
        private static void GuardVerticalMerge(
            List<OpenXmlElement> body, string templateName, string blockName)
        {
            var hasVerticalMerge = body.OfType<TableRow>()
                .SelectMany(row => row.Elements<TableCell>())
                .Any(cell => cell.TableCellProperties?.VerticalMerge is not null);

            if (hasVerticalMerge)
                throw new InvalidOperationException(
                    $"Шаблон «{templateName}»: у тілі блоку «{{{{#{blockName}}}}}» є вертикально "
                    + "об'єднані комірки. Повторення такого рядка зіпсує таблицю — приберіть "
                    + "об'єднання по вертикалі в рядках, що повторюються.");
        }

        // Маркер, який структурний обхід не розгорнув: або тому що не сягнув
        // туди (усередині елемента керування вмістом Word, у таблиці,
        // вкладеній у комірку), або тому що маркер ділить абзац з іншим
        // текстом («Список: {{#список}}» — це теж не «елемент-маркер», з
        // яким уміє працювати ProcessSiblings). Шукаємо неприв'язаним
        // регексом (BlockStructure.EmbeddedMarkerRegex) саме тому, що друга
        // причина не дає анкорованим OpenName/CloseName спрацювати. Це
        // безпечно лише разом з preserveMarkers=true в підмітанні вище: без
        // нього маркер, який ділить абзац з текстом, ReplaceInParagraph уже
        // стер би раніше, ніж ця перевірка його побачить.
        private static void GuardResidualMarkers(OpenXmlCompositeElement container, string templateName)
        {
            var marker = FindEmbeddedMarker(container);
            if (marker is null) return;

            throw new InvalidOperationException(
                $"Шаблон «{templateName}»: маркер «{marker}» не стоїть там, де рушій може "
                + "його розгорнути. Маркер має бути окремим абзацом серед абзаців контейнера "
                + "або окремим рядком серед рядків однієї таблиці — не частиною абзацу з іншим "
                + "текстом і не вкладеним глибше.");
        }

        // Спільний детектор для GuardResidualMarkers (груповий шлях) і перевірки
        // в GenerateOne: перший {{#…}}/{{/…}}, знайдений будь-де в тексті абзаців
        // контейнера, неприв'язаним BlockStructure.EmbeddedMarkerRegex (див.
        // коментар при його оголошенні — на відміну від анкорованих
        // OpenRegex/CloseRegex, він ловить і маркер, що ділить абзац з іншим
        // текстом).
        private static string? FindEmbeddedMarker(OpenXmlCompositeElement? container)
        {
            if (container is null) return null;

            foreach (var paragraph in container.Descendants<Paragraph>())
            {
                var text = BlockStructure.MarkerText(paragraph);
                var match = BlockStructure.EmbeddedMarkerRegex.Match(text);
                if (match.Success) return match.Value;
            }

            return null;
        }

        // Той самий детектор, застосований до всього документа GenerateOne
        // (тіло + всі колонтитули) — стільки ж контейнерів, скільки нижче
        // проходить заміна, щоб перевірка не пропустила маркер там, куди
        // підстановка таки дійде.
        private static string? FindResidualMarkerInDocument(MainDocumentPart? mainPart)
        {
            if (mainPart is null) return null;

            var bodyMarker = FindEmbeddedMarker(mainPart.Document?.Body);
            if (bodyMarker is not null) return bodyMarker;

            foreach (var header in mainPart.HeaderParts)
            {
                var marker = FindEmbeddedMarker(header.Header);
                if (marker is not null) return marker;
            }

            foreach (var footer in mainPart.FooterParts)
            {
                var marker = FindEmbeddedMarker(footer.Footer);
                if (marker is not null) return marker;
            }

            return null;
        }

        // Повідомлення для GenerateOne — на відміну від GuardResidualMarkers (де
        // йдеться про технічне розташування маркера в групового рушія), тут
        // причина інша й простіша для користувача: цей шаблон узагалі не для
        // одноосібної генерації.
        private static string BuildStrayBlockMarkerMessage(string templateName, string marker)
            => $"Шаблон «{templateName}» містить маркер повторюваного блоку «{marker}». "
               + "Такий шаблон формує один документ на весь список людей, а не окремий "
               + "документ для кожної людини — сформувати з нього персональний документ "
               + "не можна. Перевірте налаштування цього шаблону: він має генеруватися як груповий.";

        private static void ExpandBlock(
            OpenXmlElement openElement, List<OpenXmlElement> body, OpenXmlElement closeElement,
            IReadOnlyList<IDictionary<string, string>> perRecipientValues,
            IDictionary<string, string> sharedWithCount,
            List<string> unfilled,
            string templateName,
            string blockName)
        {
            GuardNestedRowBlocks(body, templateName, blockName);
            GuardVerticalMerge(body, templateName, blockName);

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
                    ReplaceInElement(clone, merged, unfilled, preserveMarkers: true);
                }
            }

            foreach (var original in body)
                original.Remove();

            openElement.Remove();
            closeElement.Remove();
        }

        // Заміна в усіх абзацах елемента: для абзацу це він сам, для рядка чи
        // таблиці — абзаци всіх його комірок.
        //
        // preserveMarkers за замовчуванням false: цей прапорець вмикає лише
        // груповий шлях (ProcessSiblings/ExpandBlock), де є GuardResidualMarkers,
        // що ловить лишений маркер і відмовляє. GenerateOne такого вартового
        // не має, тож там маркер, який опинився в звичайному тексті, і надалі
        // стирається як незаповнений тег — так само, як до цієї гілки.
        private static void ReplaceInElement(
            OpenXmlElement element, IDictionary<string, string> values, List<string> unfilled,
            bool preserveMarkers = false)
        {
            if (element is Paragraph paragraph)
            {
                ReplaceInParagraph(paragraph, values, unfilled, preserveMarkers);
                return;
            }

            foreach (var inner in element.Descendants<Paragraph>())
                ReplaceInParagraph(inner, values, unfilled, preserveMarkers);
        }

        private static List<string> ReplaceInContainer(
            OpenXmlCompositeElement? container, IDictionary<string, string> values,
            bool preserveMarkers = false)
        {
            var unfilled = new List<string>();
            if (container is null) return unfilled;

            foreach (var paragraph in container.Descendants<Paragraph>())
                ReplaceInParagraph(paragraph, values, unfilled, preserveMarkers);

            return unfilled;
        }

        // Заміна зі збереженням позиції нетекстових вузлів (w:tab, w:br, w:drawing…):
        // рахуємо зміщення кожного текстового вузла в межах параграфа, знаходимо збіги
        // у зчепленому тексті, і пишемо результат назад лише у ті самі текстові вузли,
        // а не в один "плаский" рядок першого рану.
        internal static void ReplaceInParagraph(
            Paragraph paragraph, IDictionary<string, string> values, List<string> unfilled,
            bool preserveMarkers = false)
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
                        if (preserveMarkers && BlockStructure.IsMarkerTag(match.Value))
                        {
                            // Маркер блоку ({{#…}}/{{/…}}), що дійшов сюди буквально —
                            // структурний обхід його не розпізнав і не прибрав. Це не
                            // поле: не вгадуємо значення і не стираємо як незаповнений
                            // тег, а лишаємо текст як є. GuardResidualMarkers у
                            // ProcessContainer саме такий залишок і шукає — стерши його
                            // тут, ми б зробили цю перевірку сліпою. Лише коли
                            // preserveMarkers=true (груповий шлях, де вартовий є) —
                            // GenerateOne цей прапорець не піднімає й далі стирає
                            // маркер як звичайний незаповнений тег.
                            sb.Append(match.Value);
                        }
                        // Цей вузол — вузол, де збіг починається: сюди йде все значення заміни.
                        else if (values.TryGetValue(match.Value, out var value) && !string.IsNullOrEmpty(value))
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
