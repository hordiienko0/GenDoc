using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GenDoc.Models;
using GenDoc.Services.Generation;

namespace GenDoc.Tests;

public class DocumentGenerationServiceTests
{
    // Плейсхолдер розбитий на три текстові вузли (типова ситуація в реальному Word-файлі
    // після ручного редагування тексту всередині фігурних дужок).
    [Fact]
    public void ReplaceInParagraph_PlaceholderSplitAcrossThreeRuns_ReplacesWholeTag()
    {
        var paragraph = new Paragraph(
            new Run(new Text("{{ta")),
            new Run(new Text("g")),
            new Run(new Text("}} tail")));

        var values = new Dictionary<string, string> { ["{{tag}}"] = "VALUE" };
        var unfilled = new List<string>();

        DocumentGenerationService.ReplaceInParagraph(paragraph, values, unfilled);

        Assert.Equal("VALUE tail", ConcatText(paragraph));
        Assert.Empty(unfilled);
    }

    // Заміна не повинна зсувати w:tab — таб має лишитись між двома замінами на своєму місці.
    [Fact]
    public void ReplaceInParagraph_PlaceholderAdjacentToTab_KeepsTabInPlace()
    {
        var paragraph = new Paragraph(
            new Run(new Text("{{a}}")),
            new Run(new TabChar()),
            new Run(new Text("{{b}}")));

        var values = new Dictionary<string, string> { ["{{a}}"] = "X", ["{{b}}"] = "Y" };
        var unfilled = new List<string>();

        DocumentGenerationService.ReplaceInParagraph(paragraph, values, unfilled);

        var runs = paragraph.Elements<Run>().ToList();
        Assert.Equal("X", runs[0].GetFirstChild<Text>()!.Text);
        Assert.NotNull(runs[1].GetFirstChild<TabChar>());
        Assert.Equal("Y", runs[2].GetFirstChild<Text>()!.Text);
    }

    // Два незалежні плейсхолдери в одному параграфі не повинні заважати один одному.
    [Fact]
    public void ReplaceInParagraph_TwoPlaceholdersInOneParagraph_BothReplacedIndependently()
    {
        var paragraph = new Paragraph(
            new Run(new Text("{{first}}")),
            new Run(new Text(" і ")),
            new Run(new Text("{{second}}")));

        var values = new Dictionary<string, string> { ["{{first}}"] = "ОДИН", ["{{second}}"] = "ДВА" };
        var unfilled = new List<string>();

        DocumentGenerationService.ReplaceInParagraph(paragraph, values, unfilled);

        Assert.Equal("ОДИН і ДВА", ConcatText(paragraph));
    }

    // Значення, якого немає в словнику, потрапляє у список unfilled, а плейсхолдер
    // замінюється на порожній рядок (а не лишається як текст {{тег}}).
    [Fact]
    public void ReplaceInParagraph_MissingValue_AddsToUnfilledAndClearsTag()
    {
        var paragraph = new Paragraph(new Run(new Text("до {{невідомий}} після")));
        var values = new Dictionary<string, string>();
        var unfilled = new List<string>();

        DocumentGenerationService.ReplaceInParagraph(paragraph, values, unfilled);

        Assert.Equal("до  після", ConcatText(paragraph));
        Assert.Equal(new[] { "{{невідомий}}" }, unfilled);
    }

    // Наскрізний прогін через GenerateOne: плейсхолдер у тілі документа, в комірці таблиці,
    // у колонтитулах — усі мають замінитись.
    [Fact]
    public void GenerateOne_ReplacesPlaceholdersInBodyTableAndHeaderFooter()
    {
        var bytes = BuildDocxWithBodyTableHeaderFooter();

        var template = new Template { Name = "Т", Content = bytes };
        var values = new Dictionary<string, string>
        {
            ["{{body_tag}}"] = "ТІЛО",
            ["{{cell_tag}}"] = "КОМІРКА",
            ["{{header_tag}}"] = "ШАПКА",
            ["{{footer_tag}}"] = "ПІДВАЛ"
        };

        var outputPath = Path.Combine(Path.GetTempPath(), $"gendoc_test_{Guid.NewGuid():N}.docx");
        try
        {
            var service = new DocumentGenerationService();
            var result = service.GenerateOne(template, bytes, values, outputPath);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Empty(result.UnfilledTags);

            using var doc = WordprocessingDocument.Open(outputPath, false);
            var mainPart = doc.MainDocumentPart!;

            Assert.Contains("ТІЛО", ConcatText(mainPart.Document!.Body!));
            Assert.Contains("КОМІРКА", ConcatText(mainPart.Document.Body!));
            Assert.Contains("ШАПКА", ConcatText(mainPart.HeaderParts.First().Header!));
            Assert.Contains("ПІДВАЛ", ConcatText(mainPart.FooterParts.First().Footer!));
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    // Шаблон без жодного плейсхолдера — текст не повинен змінитись взагалі.
    [Fact]
    public void GenerateOne_NoPlaceholders_LeavesTextUnchanged()
    {
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            using (var wordDoc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
            {
                var mainPart = wordDoc.AddMainDocumentPart();
                mainPart.Document = new Document(new Body(new Paragraph(new Run(new Text("Звичайний текст без міток.")))));
                mainPart.Document.Save();
            }
            bytes = ms.ToArray();
        }

        var template = new Template { Name = "Т", Content = bytes };
        var outputPath = Path.Combine(Path.GetTempPath(), $"gendoc_test_{Guid.NewGuid():N}.docx");
        try
        {
            var service = new DocumentGenerationService();
            var result = service.GenerateOne(template, bytes, new Dictionary<string, string>(), outputPath);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Empty(result.UnfilledTags);

            using var doc = WordprocessingDocument.Open(outputPath, false);
            Assert.Equal("Звичайний текст без міток.", ConcatText(doc.MainDocumentPart!.Document!.Body!));
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    // Груповий DOCX: повторюваний блок {{#список}}…{{/список}} клонується по одному
    // на кожного одержувача, {{роздільник}} — ";" для всіх, крім останнього.
    [Fact]
    public void GenerateGroup_ClonesBlockPerRecipient_WithSeparatorAndTabPreserved()
    {
        var bytes = BuildDocxWithGroupBlock();
        var template = new Template { Name = "Т", Content = bytes };

        var perRecipient = new List<IDictionary<string, string>>
        {
            new Dictionary<string, string> { ["{{звання}}"] = "майор", ["{{піб}}"] = "ІВАНЕНКО Іван" },
            new Dictionary<string, string> { ["{{звання}}"] = "капітан", ["{{піб}}"] = "ПЕТРЕНКО Петро" },
            new Dictionary<string, string> { ["{{звання}}"] = "лейтенант", ["{{піб}}"] = "СИДОРЕНКО Сидір" },
        };
        var shared = new Dictionary<string, string> { ["{{адресат}}"] = "Командиру частини" };

        var outputPath = Path.Combine(Path.GetTempPath(), $"gendoc_group_{Guid.NewGuid():N}.docx");
        try
        {
            var service = new DocumentGenerationService();
            var result = service.GenerateGroup(template, bytes, perRecipient, shared, outputPath);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Empty(result.UnfilledTags);

            using var doc = WordprocessingDocument.Open(outputPath, false);
            var body = doc.MainDocumentPart!.Document!.Body!;
            var paragraphs = body.Elements<Paragraph>().ToList();

            // Маркерні абзаци прибрані, лишились: адресат, 3 клони, підсумок.
            Assert.Equal(5, paragraphs.Count);
            Assert.Equal("Командиру частини", ConcatText(paragraphs[0]));

            Assert.Equal("майор", ConcatText(paragraphs[1].Elements<Run>().First()));
            Assert.NotNull(paragraphs[1].Descendants<TabChar>().FirstOrDefault());
            Assert.Equal("майорІВАНЕНКО Іван;", ConcatText(paragraphs[1]));

            Assert.Equal("капітанПЕТРЕНКО Петро;", ConcatText(paragraphs[2]));
            Assert.Equal("лейтенантСИДОРЕНКО Сидір.", ConcatText(paragraphs[3])); // останній — крапка

            Assert.Equal("Кількість: 3", ConcatText(paragraphs[4]));
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    // Три позначені одержувачі (підмножина) — стільки рядків і з'являється, у тому
    // порядку, у якому передані, незалежно від загального розміру складу.
    [Fact]
    public void GenerateGroup_SubsetOfThree_ProducesExactlyThreeInGivenOrder()
    {
        var bytes = BuildDocxWithGroupBlock();
        var template = new Template { Name = "Т", Content = bytes };

        var perRecipient = new List<IDictionary<string, string>>
        {
            new Dictionary<string, string> { ["{{звання}}"] = "лейтенант", ["{{піб}}"] = "А А" },
            new Dictionary<string, string> { ["{{звання}}"] = "капітан", ["{{піб}}"] = "Б Б" },
        };
        var shared = new Dictionary<string, string> { ["{{адресат}}"] = "X" };

        var outputPath = Path.Combine(Path.GetTempPath(), $"gendoc_group_{Guid.NewGuid():N}.docx");
        try
        {
            var service = new DocumentGenerationService();
            var result = service.GenerateGroup(template, bytes, perRecipient, shared, outputPath);

            Assert.True(result.Success, result.ErrorMessage);

            using var doc = WordprocessingDocument.Open(outputPath, false);
            var body = doc.MainDocumentPart!.Document!.Body!;
            var paragraphs = body.Elements<Paragraph>().ToList();

            Assert.Equal(4, paragraphs.Count); // адресат + 2 клони + підсумок
            Assert.Equal("лейтенантА А;", ConcatText(paragraphs[1]));
            Assert.Equal("капітанБ Б.", ConcatText(paragraphs[2]));
            Assert.Equal("Кількість: 2", ConcatText(paragraphs[3]));
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    // Незакритий блок — явна помилка генерації, а не тихий частковий документ.
    [Fact]
    public void GenerateGroup_UnclosedBlock_ReturnsFailure()
    {
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            using (var wordDoc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
            {
                var mainPart = wordDoc.AddMainDocumentPart();
                mainPart.Document = new Document(new Body(
                    new Paragraph(new Run(new Text("{{#список}}"))),
                    new Paragraph(new Run(new Text("{{піб}}")))));
                mainPart.Document.Save();
            }
            bytes = ms.ToArray();
        }

        var template = new Template { Name = "Т", Content = bytes };
        var perRecipient = new List<IDictionary<string, string>> { new Dictionary<string, string> { ["{{піб}}"] = "X" } };

        var outputPath = Path.Combine(Path.GetTempPath(), $"gendoc_group_{Guid.NewGuid():N}.docx");
        var service = new DocumentGenerationService();
        var result = service.GenerateGroup(template, bytes, perRecipient, new Dictionary<string, string>(), outputPath);

        Assert.False(result.Success);
        Assert.False(File.Exists(outputPath));
    }

    // Вкладені блоки не підтримуються — явна помилка.
    [Fact]
    public void GenerateGroup_NestedBlocks_ReturnsFailure()
    {
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            using (var wordDoc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
            {
                var mainPart = wordDoc.AddMainDocumentPart();
                mainPart.Document = new Document(new Body(
                    new Paragraph(new Run(new Text("{{#зовнішній}}"))),
                    new Paragraph(new Run(new Text("{{#внутрішній}}"))),
                    new Paragraph(new Run(new Text("{{піб}}"))),
                    new Paragraph(new Run(new Text("{{/внутрішній}}"))),
                    new Paragraph(new Run(new Text("{{/зовнішній}}")))));
                mainPart.Document.Save();
            }
            bytes = ms.ToArray();
        }

        var template = new Template { Name = "Т", Content = bytes };
        var perRecipient = new List<IDictionary<string, string>> { new Dictionary<string, string>() };

        var outputPath = Path.Combine(Path.GetTempPath(), $"gendoc_group_{Guid.NewGuid():N}.docx");
        var service = new DocumentGenerationService();
        var result = service.GenerateGroup(template, bytes, perRecipient, new Dictionary<string, string>(), outputPath);

        Assert.False(result.Success);
    }

    // Навмисна асиметрія (другий огляд перед злиттям гілки): GenerateOne не
    // розуміє блоків і не має вартового (GuardResidualMarkers), і не мусить
    // його отримати — це рушій без структурного обходу, для нього маркер
    // просто ще один {{тег}}, якого нема в мапінгу. preserveMarkers у
    // ReplaceInParagraph за замовчуванням false, і саме GenerateOne — той
    // єдиний шлях, що ніколи не передає true, тож маркер, який опинився в
    // звичайному тексті, стирається як незаповнений тег так само, як це
    // було до всієї гілки з табличними блоками (master). Цей тест закріплює
    // саме цю різницю з груповим шляхом: тут документ ГЕНЕРУЄТЬСЯ, а не
    // відмовляє.
    [Fact]
    public void GenerateOne_DocumentWithMarker_SucceedsAndErasesMarker_MatchingMaster()
    {
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            using (var wordDoc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
            {
                var mainPart = wordDoc.AddMainDocumentPart();
                mainPart.Document = new Document(new Body(
                    new Paragraph(new Run(new Text("Список: {{#список}}"))),
                    new Paragraph(new Run(new Text("{{піб}}"))),
                    new Paragraph(new Run(new Text("кінець {{/список}}")))));
                mainPart.Document.Save();
            }
            bytes = ms.ToArray();
        }

        var template = new Template { Name = "Т", Content = bytes };
        var values = new Dictionary<string, string> { ["{{піб}}"] = "ОДИН" };
        var outputPath = Path.Combine(Path.GetTempPath(), $"gendoc_test_{Guid.NewGuid():N}.docx");
        try
        {
            var service = new DocumentGenerationService();
            var result = service.GenerateOne(template, bytes, values, outputPath);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Contains("{{#список}}", result.UnfilledTags);
            Assert.Contains("{{/список}}", result.UnfilledTags);

            using var doc = WordprocessingDocument.Open(outputPath, false);
            var text = ConcatText(doc.MainDocumentPart!.Document!.Body!);
            Assert.DoesNotContain("{{", text);
            Assert.Contains("ОДИН", text);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    private static byte[] BuildDocxWithGroupBlock()
    {
        using var ms = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body(
                new Paragraph(new Run(new Text("{{адресат}}"))),
                new Paragraph(new Run(new Text("{{#список}}"))),
                new Paragraph(
                    new Run(new Text("{{звання}}")),
                    new Run(new TabChar()),
                    new Run(new Text("{{піб}}")),
                    new Run(new Text("{{роздільник}}"))),
                new Paragraph(new Run(new Text("{{/список}}"))),
                new Paragraph(new Run(new Text("Кількість: {{кількість_осіб}}")))));
            mainPart.Document.Save();
        }

        return ms.ToArray();
    }

    private static string ConcatText(DocumentFormat.OpenXml.OpenXmlElement container)
        => string.Concat(container.Descendants<Text>().Select(t => t.Text));

    private static byte[] BuildDocxWithBodyTableHeaderFooter()
    {
        using var ms = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());

            var headerPart = mainPart.AddNewPart<HeaderPart>();
            headerPart.Header = new Header(new Paragraph(new Run(new Text("{{header_tag}}"))));
            headerPart.Header.Save();
            var headerRelId = mainPart.GetIdOfPart(headerPart);

            var footerPart = mainPart.AddNewPart<FooterPart>();
            footerPart.Footer = new Footer(new Paragraph(new Run(new Text("{{footer_tag}}"))));
            footerPart.Footer.Save();
            var footerRelId = mainPart.GetIdOfPart(footerPart);

            var table = new Table(
                new TableRow(
                    new TableCell(
                        new Paragraph(new Run(new Text("{{cell_tag}}"))))));

            var sectionProps = new SectionProperties(
                new HeaderReference { Type = HeaderFooterValues.Default, Id = headerRelId },
                new FooterReference { Type = HeaderFooterValues.Default, Id = footerRelId });

            var body = mainPart.Document.Body!;
            body.Append(table);
            body.Append(new Paragraph(new Run(new Text("{{body_tag}}"))));
            body.Append(sectionProps);

            mainPart.Document.Save();
        }

        return ms.ToArray();
    }
}
