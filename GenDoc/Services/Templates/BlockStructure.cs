using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace GenDoc.Services.Templates
{
    // Спільне розпізнавання повторюваних блоків для сканера (TemplateService) і
    // рушія (DocumentGenerationService). Тримати це в одному місці обов'язково:
    // PlaceholderTagMaps існує рівно тому, що домовленість «не забути змінити в
    // обох» одного разу не втрималась, і колонка мовчки виходила порожня.
    public static class BlockStructure
    {
        public static readonly Regex OpenRegex = new(@"^\{\{#([^{}]+)\}\}$", RegexOptions.Compiled);
        public static readonly Regex CloseRegex = new(@"^\{\{/([^{}]+)\}\}$", RegexOptions.Compiled);

        // Той самий синтаксис маркера, але без прив'язки до країв рядка. Пара
        // вище відповідає на «чи цей елемент - маркер», для чого вся довжина
        // абзаца/рядка мусить дорівнювати маркеру. Цей регекс відповідає на
        // інше питання - «чи маркер ЗАЛІЗ у текст абзаца» (наприклад, «Список:
        // {{#список}}» чи «кінець {{/список}}»): такий текст ніколи не пройде
        // як елемент-маркер (обхід його не розпізнає й не розгорне), але й
        // лишати його неперевіреним не можна - це той самий недосяжний блок,
        // лише в іншому вбранні. GuardResidualMarkers шукає саме цим регексом.
        public static readonly Regex EmbeddedMarkerRegex = new(@"\{\{[#/][^{}]+\}\}", RegexOptions.Compiled);

        // Блокові діти контейнера в порядку документа. Маркером або тілом блоку
        // може бути лише абзац чи таблиця; решта (закладки, структуровані теги
        // вмісту, розриви секцій) проходить наскрізь незміненою - вона не бере
        // участі в блоках, але й зникати з документа не має.
        public static IEnumerable<OpenXmlElement> BlockChildren(OpenXmlCompositeElement container)
            => container.ChildElements.Where(e => e is Paragraph or Table);

        public static IEnumerable<TableRow> Rows(Table table) => table.Elements<TableRow>();

        // Маркерний текст: зчеплений і обрізаний текст усіх абзаців елемента.
        // Для абзацу це його власний текст; для рядка - текст усіх комірок
        // підряд, тож рядок із «{{#список}}» у першій комірці й порожніми
        // рештою розпізнається як маркер.
        public static string MarkerText(OpenXmlElement element)
            => string.Concat(element.Descendants<Text>().Select(t => t.Text)).Trim();

        public static string? OpenName(string markerText)
        {
            var match = OpenRegex.Match(markerText);
            return match.Success ? match.Groups[1].Value : null;
        }

        public static string? CloseName(string markerText)
        {
            var match = CloseRegex.Match(markerText);
            return match.Success ? match.Groups[1].Value : null;
        }

        // Чи є знайдений {{тег}} маркером блоку. Потрібно сканеру: маркер
        // збігається зі звичайним регексом плейсхолдера, тож без цієї перевірки
        // «{{#список}}» потрапив би в мапінг як поле.
        public static bool IsMarkerTag(string tagWithBraces)
            => OpenRegex.IsMatch(tagWithBraces) || CloseRegex.IsMatch(tagWithBraces);
    }
}
