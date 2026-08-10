# Повторювані блоки в таблицях DOCX — план реалізації

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Навчити рушій генерації повторювати рядки таблиці для кожної людини зі складу, як він уже вміє повторювати абзаци.

**Architecture:** Обхід перестає бути плоским `Descendants<Paragraph>()` і йде по блокових дітях контейнера — абзац або таблиця. Кінцевий автомат блоку виноситься в один метод над послідовністю сусідів одного батька і застосовується двічі: до блокових дітей контейнера і до рядків таблиці. Розпізнавання маркерів переїжджає в спільний `BlockStructure`, яким користуються і сканер, і рушій.

**Tech Stack:** .NET 8 (`net8.0-windows`), DocumentFormat.OpenXml, xUnit 2.5.3.

## Global Constraints

- Цільовий фреймворк `net8.0-windows`; нових NuGet-пакетів не додавати.
- Коментарі й повідомлення для користувача — українською; назви типів, методів і тестів — англійською.
- Змін схеми БД немає. `DatabaseSchemaInitializer`, `AppDbContext.OnConfiguring`, `ShutdownMode` і пароль бази не чіпати.
- Збірка мусить давати **нуль попереджень** — сьогодні їх нуль.
- Прогін набору: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj -v q --nologo`
- Базовий стан до початку: **270 passed, 0 skipped, 0 failed**.
- Наявні тести мусять лишатися зеленими. Єдиний виняток названо явно в Task 1 — тест `GroupBlockInsideTable_FailsWithMessageNamingTemplateAndBlock` переписується, бо обмеження, яке він фіксував, зникає.
- Абзацний шлях не змінює поведінки: `шаблони/Шаблон_Рапорт_котлове_ГРУПОВИЙ (3).docx` формується точно як раніше.

## Довідка: поточний код

`GenDoc/Services/Generation/DocumentGenerationService.cs`:
- `ProcessContainer(OpenXmlCompositeElement container, IReadOnlyList<IDictionary<string,string>> perRecipientValues, IDictionary<string,string> sharedValues, string templateName)` — бере `container.Descendants<Paragraph>()` плоско, крутить автомат, зве `ExpandBlock`.
- `ExpandBlock(Paragraph openParagraph, List<Paragraph> bodyParagraphs, Paragraph closeParagraph, …, string templateName, string blockName)` — перша дія: перевірка `!ReferenceEquals(p.Parent, openParagraph.Parent)` і відмова «тіло блоку лежить усередині таблиці».
- Приватні: `GetParagraphText(Paragraph)`, `ReplaceInParagraph(Paragraph, IDictionary<string,string>, List<string>)` (internal), `ReplaceInContainer(OpenXmlCompositeElement?, IDictionary<string,string>)`.
- Константи: `CountTag = "{{кількість_осіб}}"`, `IndexTag = "{{номер}}"`, `SeparatorTag = "{{роздільник}}"`.
- Регекси `BlockOpenRegex` / `BlockCloseRegex`: `^\{\{#([^{}]+)\}\}$` і `^\{\{/([^{}]+)\}\}$`.

`GenDoc/Services/Templates/TemplateService.cs`:
- Має **власні копії** тих самих двох регексів із коментарем «тримати в синхроні».
- `ScanPlaceholders(WordprocessingDocument doc)` → `internal sealed record ScanResult(List<(string Tag, bool IsInsideBlock)> Tags, bool HasBlock)`; локальна функція `ScanContainer` іде `container.Descendants<Paragraph>()` плоско.
- `ReservedBlockTags` — `{{роздільник}}`, `{{номер}}`, `{{кількість_осіб}}`; у мапінг не потрапляють.
- `Upload` ставить `Kind = scan.HasBlock ? TemplateKind.Group : TemplateKind.PerRecipient`.

`GenDoc.Tests/Generation/DocxRealTemplateTests.cs` має хелпери `TemplateStub(string name)`, `OutputPath(string name)`, `ReadAllText(string path)`, `ParagraphTexts(string path)` і клас створює тимчасову теку в конструкторі, прибирає в `Dispose`.

## Структура файлів

**Створюються (продакшн):**
- `GenDoc/Services/Templates/BlockStructure.cs` — обхід і розпізнавання маркерів, спільні для сканера й рушія.

**Змінюються (продакшн):**
- `GenDoc/Services/Generation/DocumentGenerationService.cs` — Task 1, Task 2.
- `GenDoc/Services/Templates/TemplateService.cs` — Task 3.

**Створюються (тести):**
- `GenDoc.Tests/Generation/TableBlockTests.cs` — Task 1.
- `GenDoc.Tests/Generation/TableBlockErrorTests.cs` — Task 2.
- `GenDoc.Tests/Templates/TableBlockScanTests.cs` — Task 3.

**Змінюються (тести):**
- `GenDoc.Tests/Generation/DocxRealTemplateTests.cs` — Task 1, переписаний тест Task 17.

---

### Task 1: Табличні блоки в рушії

**Files:**
- Create: `GenDoc/Services/Templates/BlockStructure.cs`
- Modify: `GenDoc/Services/Generation/DocumentGenerationService.cs`
- Create: `GenDoc.Tests/Generation/TableBlockTests.cs`
- Modify: `GenDoc.Tests/Generation/DocxRealTemplateTests.cs`

**Interfaces:**
- Consumes: `DocumentGenerationService.GenerateGroup(Template, byte[], IReadOnlyList<IDictionary<string,string>>, IDictionary<string,string>, string)` → `GenerationItemResult(bool Success, string? ErrorMessage, List<string> UnfilledTags)`.
- Produces:
  - `public static class BlockStructure` у просторі імен `GenDoc.Services.Templates`, з членами `OpenRegex`, `CloseRegex`, `BlockChildren(OpenXmlCompositeElement)`, `Rows(Table)`, `MarkerText(OpenXmlElement)`, `OpenName(string)`, `CloseName(string)`, `IsMarkerTag(string)` — Task 3 користується всіма.
  - У `DocumentGenerationService` приватні `ProcessSiblings(...)`, `ExpandBlock(OpenXmlElement, List<OpenXmlElement>, OpenXmlElement, …)` і `ReplaceInElement(OpenXmlElement, IDictionary<string,string>, List<string>)` — Task 2 додає до них охоронців.

- [ ] **Step 1: Написати падаючі тести**

Create `GenDoc.Tests/Generation/TableBlockTests.cs`:

```csharp
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GenDoc.Models;
using GenDoc.Services.Generation;

namespace GenDoc.Tests.Generation;

// Списки людей у військових паперах майже завжди табличні, тож повторення
// рядка — не окрема фіча, а той самий блок {{#…}}/{{/…}}, лише одиниця
// повторення інша: рядок замість абзацу.
public class TableBlockTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-tblock-{Guid.NewGuid():N}");

    public TableBlockTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static Template TemplateStub(string name) => new()
    {
        Id = 1, Name = name, OriginalFileName = $"{name}.docx",
        Content = Array.Empty<byte>(), UploadedAt = DateTime.Now
    };

    private static TableRow Row(params string[] cells)
        => new(cells.Select(c => new TableCell(new Paragraph(new Run(new Text(c))))));

    // Шапка, маркерний рядок, один рядок тіла, закриваючий маркер, підсумок.
    // {{кількість_осіб}} свідомо поза блоком — воно спільне для документа.
    private static byte[] BuildTableBlockDocx()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            doc.AddMainDocumentPart().Document = new Document(new Body());
            var body = doc.MainDocumentPart!.Document!.Body!;

            body.AppendChild(new Paragraph(new Run(new Text("Список особового складу"))));
            body.AppendChild(new Table(
                Row("№", "ПІБ", "Звання"),
                Row("{{#список}}"),
                Row("{{номер}}", "{{піб}}{{роздільник}}", "{{звання}}"),
                Row("{{/список}}"),
                Row("Усього", "{{кількість_осіб}}", "")));

            doc.MainDocumentPart.Document.Save();
        }
        return stream.ToArray();
    }

    private static List<IDictionary<string, string>> People(params (string Pib, string Rank)[] people)
        => people.Select(p => (IDictionary<string, string>)new Dictionary<string, string>
        {
            ["{{піб}}"] = p.Pib,
            ["{{звання}}"] = p.Rank
        }).ToList();

    private static List<List<string>> TableCells(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var table = doc.MainDocumentPart!.Document.Body!.Elements<Table>().Single();
        return table.Elements<TableRow>()
            .Select(r => r.Elements<TableCell>()
                .Select(c => string.Concat(c.Descendants<Text>().Select(t => t.Text)).Trim())
                .ToList())
            .ToList();
    }

    private string Generate(byte[] template, List<IDictionary<string, string>> people, string fileName)
    {
        var path = Path.Combine(_folder, fileName);
        var result = new DocumentGenerationService().GenerateGroup(
            TemplateStub("Список"), template, people, new Dictionary<string, string>(), path);

        Assert.True(result.Success, result.ErrorMessage);
        return path;
    }

    [Fact]
    public void TableBlock_ExpandsOneRowPerPerson_AndRemovesMarkerRows()
    {
        var path = Generate(BuildTableBlockDocx(),
            People(("ШЕВЧЕНКО Т.", "полковник"), ("ФРАНКО І.", "майор"), ("ЛЕСЯ У.", "капітан")),
            "expand.docx");

        var rows = TableCells(path);

        // Шапка + 3 людини + підсумок. Маркерних рядків уже нема.
        Assert.Equal(5, rows.Count);
        Assert.Equal("№", rows[0][0]);
        Assert.Equal("ШЕВЧЕНКО Т.;", rows[1][1]);
        Assert.Equal("ФРАНКО І.;", rows[2][1]);
        Assert.Equal("ЛЕСЯ У..", rows[3][1]);
        Assert.Equal("Усього", rows[4][0]);
        Assert.DoesNotContain(rows, r => r.Any(c => c.Contains("{{#") || c.Contains("{{/")));
    }

    [Fact]
    public void TableBlock_NumbersRowsFromOne()
    {
        var path = Generate(BuildTableBlockDocx(),
            People(("ПЕРШИЙ", "солдат"), ("ДРУГИЙ", "солдат")),
            "numbers.docx");

        var rows = TableCells(path);
        Assert.Equal("1", rows[1][0]);
        Assert.Equal("2", rows[2][0]);
    }

    [Fact]
    public void TableBlock_FillsSharedTagsOutsideTheBlock()
    {
        var path = Generate(BuildTableBlockDocx(),
            People(("ПЕРШИЙ", "солдат"), ("ДРУГИЙ", "солдат"), ("ТРЕТІЙ", "солдат")),
            "count.docx");

        var rows = TableCells(path);
        Assert.Equal("3", rows[^1][1]);
    }

    [Fact]
    public void TableBlock_EmptyRoster_LeavesHeaderAndFooterOnly()
    {
        var path = Generate(BuildTableBlockDocx(), new List<IDictionary<string, string>>(), "empty.docx");

        var rows = TableCells(path);
        Assert.Equal(2, rows.Count);
        Assert.Equal("№", rows[0][0]);
        Assert.Equal("Усього", rows[1][0]);
    }

    // Горизонтальне об'єднання живе у властивостях комірки, тож має пережити
    // клонування рядка без окремого коду.
    [Fact]
    public void TableBlock_KeepsHorizontalMergeOnClonedRows()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            doc.AddMainDocumentPart().Document = new Document(new Body());
            var body = doc.MainDocumentPart!.Document!.Body!;

            var wide = new TableCell(
                new TableCellProperties(new GridSpan { Val = 2 }),
                new Paragraph(new Run(new Text("{{піб}}"))));

            body.AppendChild(new Table(
                Row("{{#список}}"),
                new TableRow(wide),
                Row("{{/список}}")));

            doc.MainDocumentPart.Document.Save();
        }

        var path = Generate(stream.ToArray(),
            People(("ПЕРШИЙ", "солдат"), ("ДРУГИЙ", "солдат")),
            "gridspan.docx");

        using var produced = WordprocessingDocument.Open(path, false);
        var spans = produced.MainDocumentPart!.Document.Body!
            .Elements<Table>().Single()
            .Elements<TableRow>()
            .SelectMany(r => r.Elements<TableCell>())
            .Select(c => c.TableCellProperties?.GridSpan?.Val?.Value)
            .Where(v => v is not null)
            .ToList();

        Assert.Equal(new int?[] { 2, 2 }, spans);
    }
}
```

- [ ] **Step 2: Прогнати — усі мають впасти**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj --filter "FullyQualifiedName~TableBlockTests" -v q --nologo`
Expected: FAIL, 5 failed. Повідомлення — «тіло блоку … лежить усередині таблиці», бо старий охоронець у `ExpandBlock` спрацьовує на рядках таблиці.

- [ ] **Step 3: Створити спільний `BlockStructure`**

Create `GenDoc/Services/Templates/BlockStructure.cs`:

```csharp
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

        // Блокові діти контейнера в порядку документа. Маркером або тілом блоку
        // може бути лише абзац чи таблиця; решта (закладки, структуровані теги
        // вмісту, розриви секцій) проходить наскрізь незміненою — вона не бере
        // участі в блоках, але й зникати з документа не має.
        public static IEnumerable<OpenXmlElement> BlockChildren(OpenXmlCompositeElement container)
            => container.ChildElements.Where(e => e is Paragraph or Table);

        public static IEnumerable<TableRow> Rows(Table table) => table.Elements<TableRow>();

        // Маркерний текст: зчеплений і обрізаний текст усіх абзаців елемента.
        // Для абзацу це його власний текст; для рядка — текст усіх комірок
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
```

- [ ] **Step 4: Перебудувати обхід у рушії**

У `GenDoc/Services/Generation/DocumentGenerationService.cs` додати `using GenDoc.Services.Templates;` до списку `using`-ів.

Замінити тіло `ProcessContainer` (метод лишає ту саму сигнатуру):

```csharp
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
```

Замінити `ExpandBlock` цілком — старий охоронець «однакового батька» видаляється разом із повідомленням про таблиці:

```csharp
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
```

`GetParagraphText` більше не використовується — видалити метод, інакше з'явиться попередження про невикористаний приватний член.

- [ ] **Step 5: Прогнати нові тести**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj --filter "FullyQualifiedName~TableBlockTests" -v q --nologo`
Expected: PASS, 5 passed.

- [ ] **Step 6: Переписати тест Task 17**

Обмеження, яке він фіксував, зникло: блок рівня документа, у тілі якого таблиця, тепер клонує таблицю цілком.

У `GenDoc.Tests/Generation/DocxRealTemplateTests.cs` замінити метод `GroupBlockInsideTable_FailsWithMessageNamingTemplateAndBlock` на:

```csharp
    // Побічний наслідок переходу на обхід блокових дітей: якщо в тілі блоку
    // лежить ціла таблиця, вона клонується на кожну людину — окрема таблиця на
    // особу. Раніше це була відмова (Task 17), бо обхід був плоским і абзаци
    // комірок мали інший батько, ніж маркери.
    [Fact]
    public void GroupBlockAroundTable_ClonesTheWholeTablePerPerson()
    {
        var bytes = BuildGroupDocxWithBlockInsideTable();

        var path = OutputPath("table-block.docx");
        var result = new DocumentGenerationService().GenerateGroup(
            TemplateStub("Відомість у таблиці"), bytes,
            new List<IDictionary<string, string>>
            {
                new Dictionary<string, string> { ["{{піб}}"] = "ПЕРШИЙ" },
                new Dictionary<string, string> { ["{{піб}}"] = "ДРУГИЙ" }
            },
            new Dictionary<string, string>(), path);

        Assert.True(result.Success, result.ErrorMessage);

        using var produced = WordprocessingDocument.Open(path, false);
        var tables = produced.MainDocumentPart!.Document.Body!.Elements<Table>().ToList();

        Assert.Equal(2, tables.Count);
        Assert.Equal("ПЕРШИЙ", string.Concat(tables[0].Descendants<Text>().Select(t => t.Text)).Trim());
        Assert.Equal("ДРУГИЙ", string.Concat(tables[1].Descendants<Text>().Select(t => t.Text)).Trim());
        Assert.DoesNotContain("{{", ReadAllText(path));
    }
```

Хелпер `BuildGroupDocxWithBlockInsideTable` лишається без змін.

- [ ] **Step 7: Прогнати весь набір**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj -v q --nologo`
Expected: PASS, **275 passed, 0 skipped**, нуль попереджень збірки.

Особливо звірити, що зелені: `DocxRealTemplateTests.GroupRaport_ExpandsBlockOncePerPerson_WithCorrectSeparators` і `GroupRaport_EmptyRoster_RemovesBlockEntirely` — це сторожі абзацного шляху на справжньому шаблоні.

- [ ] **Step 8: Коміт**

```bash
git add GenDoc/Services/Templates/BlockStructure.cs GenDoc/Services/Generation/DocumentGenerationService.cs GenDoc.Tests/Generation/TableBlockTests.cs GenDoc.Tests/Generation/DocxRealTemplateTests.cs
git commit -m "feat: repeat table rows per person in group docx templates"
```

---

### Task 2: Охоронці й повідомлення про помилки

**Files:**
- Modify: `GenDoc/Services/Generation/DocumentGenerationService.cs`
- Create: `GenDoc.Tests/Generation/TableBlockErrorTests.cs`

**Interfaces:**
- Consumes: `ProcessSiblings`, `ExpandBlock`, `BlockStructure` з Task 1.
- Produces: приватні `GuardVerticalMerge(List<OpenXmlElement>, string, string)` і `GuardNestedRowBlocks(List<OpenXmlElement>, string, string)`.

Три випадки лишаються тихо неправильними після Task 1, і кожен дає користувачеві зіпсований документ без жодного слова:

1. **Вертикальне об'єднання в повторюваному рядку.** Клон дасть N продовжень merge і поламану таблицю.
2. **Маркерний рядок усередині блоку рівня документа.** Обхід туди не заходить, тож `{{#…}}` лишиться літеральним текстом у кожній копії.
3. **Блок, відкритий абзацом і закритий рядком таблиці.** Дає повідомлення «не закрито», яке не пояснює справжньої причини.

- [ ] **Step 1: Написати падаючі тести**

Create `GenDoc.Tests/Generation/TableBlockErrorTests.cs`:

```csharp
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GenDoc.Models;
using GenDoc.Services.Generation;

namespace GenDoc.Tests.Generation;

// Кожен випадок тут дав би зіпсований документ мовчки. Відмова з названим
// шаблоном і блоком краща за файл, який виглядає готовим.
public class TableBlockErrorTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-tblockerr-{Guid.NewGuid():N}");

    public TableBlockErrorTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static Template TemplateStub(string name) => new()
    {
        Id = 1, Name = name, OriginalFileName = $"{name}.docx",
        Content = Array.Empty<byte>(), UploadedAt = DateTime.Now
    };

    private static TableRow Row(params string[] cells)
        => new(cells.Select(c => new TableCell(new Paragraph(new Run(new Text(c))))));

    private static byte[] Build(Action<Body> fill)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            doc.AddMainDocumentPart().Document = new Document(new Body());
            fill(doc.MainDocumentPart!.Document!.Body!);
            doc.MainDocumentPart.Document.Save();
        }
        return stream.ToArray();
    }

    private GenerationItemResult Generate(byte[] template, string fileName)
        => new DocumentGenerationService().GenerateGroup(
            TemplateStub("Проба"), template,
            new List<IDictionary<string, string>>
            {
                new Dictionary<string, string> { ["{{піб}}"] = "ПЕРШИЙ" },
                new Dictionary<string, string> { ["{{піб}}"] = "ДРУГИЙ" }
            },
            new Dictionary<string, string>(),
            Path.Combine(_folder, fileName));

    [Fact]
    public void VerticalMergeInRepeatedRow_FailsWithReadableMessage()
    {
        var merged = new TableCell(
            new TableCellProperties(new VerticalMerge { Val = MergedCellValues.Restart }),
            new Paragraph(new Run(new Text("{{піб}}"))));

        var bytes = Build(body => body.AppendChild(new Table(
            Row("{{#список}}"),
            new TableRow(merged),
            Row("{{/список}}"))));

        var result = Generate(bytes, "vmerge.docx");

        Assert.False(result.Success);
        Assert.Contains("Проба", result.ErrorMessage);
        Assert.Contains("список", result.ErrorMessage);
        Assert.Contains("вертикально", result.ErrorMessage);
    }

    [Fact]
    public void RowMarkerInsideParagraphBlock_FailsAsNestedBlock()
    {
        var bytes = Build(body =>
        {
            body.AppendChild(new Paragraph(new Run(new Text("{{#зовнішній}}"))));
            body.AppendChild(new Table(
                Row("{{#внутрішній}}"),
                Row("{{піб}}"),
                Row("{{/внутрішній}}")));
            body.AppendChild(new Paragraph(new Run(new Text("{{/зовнішній}}"))));
        });

        var result = Generate(bytes, "nested.docx");

        Assert.False(result.Success);
        Assert.Contains("Проба", result.ErrorMessage);
        Assert.Contains("внутрішній", result.ErrorMessage);
        Assert.Contains("вкладені", result.ErrorMessage);
    }

    [Fact]
    public void BlockOpenedByParagraphAndClosedByRow_FailsWithCrossingMessage()
    {
        var bytes = Build(body =>
        {
            body.AppendChild(new Paragraph(new Run(new Text("{{#список}}"))));
            body.AppendChild(new Table(
                Row("{{піб}}"),
                Row("{{/список}}")));
        });

        var result = Generate(bytes, "crossing.docx");

        Assert.False(result.Success);
        Assert.Contains("Проба", result.ErrorMessage);
        Assert.Contains("список", result.ErrorMessage);
        Assert.Contains("на одному рівні", result.ErrorMessage);
    }

    [Fact]
    public void BlockOpenedInTableAndNotClosedThere_FailsNamingTheTable()
    {
        var bytes = Build(body => body.AppendChild(new Table(
            Row("{{#список}}"),
            Row("{{піб}}"))));

        var result = Generate(bytes, "unclosed-row.docx");

        Assert.False(result.Success);
        Assert.Contains("Проба", result.ErrorMessage);
        Assert.Contains("список", result.ErrorMessage);
        Assert.Contains("таблиц", result.ErrorMessage);
    }
}
```

- [ ] **Step 2: Прогнати — усі чотири мають впасти**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj --filter "FullyQualifiedName~TableBlockErrorTests" -v q --nologo`
Expected: FAIL, 4 failed. Перші два падають на `Assert.False(result.Success)` — генерація мовчки «вдається»; третій і четвертий падають на змісті повідомлення.

- [ ] **Step 3: Додати охоронців**

У `GenDoc/Services/Generation/DocumentGenerationService.cs` на початку `ExpandBlock`, перед циклом:

```csharp
            GuardNestedRowBlocks(body, templateName, blockName);
            GuardVerticalMerge(body, templateName, blockName);
```

Додати обидва методи поруч із `ExpandBlock`:

```csharp
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
        private static void GuardVerticalMerge(
            List<OpenXmlElement> body, string templateName, string blockName)
        {
            var hasVerticalMerge = body
                .SelectMany(element => element.Descendants<TableCellProperties>())
                .Any(properties => properties.VerticalMerge is not null);

            if (hasVerticalMerge)
                throw new InvalidOperationException(
                    $"Шаблон «{templateName}»: у тілі блоку «{{{{#{blockName}}}}}» є вертикально "
                    + "об'єднані комірки. Повторення такого рядка зіпсує таблицю — приберіть "
                    + "об'єднання по вертикалі в рядках, що повторюються.");
        }
```

- [ ] **Step 4: Уточнити повідомлення про незакритий блок**

У `ProcessSiblings` замінити фінальний `throw`:

```csharp
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
```

- [ ] **Step 5: Прогнати нові тести**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj --filter "FullyQualifiedName~TableBlockErrorTests" -v q --nologo`
Expected: PASS, 4 passed.

- [ ] **Step 6: Прогнати весь набір**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj -v q --nologo`
Expected: PASS, **279 passed, 0 skipped**, нуль попереджень.

Звірити, що зелений `DocumentGenerationServiceTests` — там є тести на незакритий блок і незбіжну назву в абзацному шляху, і їхні очікувані тексти не мали змінитися.

- [ ] **Step 7: Коміт**

```bash
git add GenDoc/Services/Generation/DocumentGenerationService.cs GenDoc.Tests/Generation/TableBlockErrorTests.cs
git commit -m "feat: guard vertical merge, nested row blocks and markers crossing a table boundary"
```

---

### Task 3: Сканер бачить табличні блоки

**Files:**
- Modify: `GenDoc/Services/Templates/TemplateService.cs`
- Create: `GenDoc.Tests/Templates/TableBlockScanTests.cs`

**Interfaces:**
- Consumes: `BlockStructure` з Task 1; `TemplateService.ScanPlaceholders(WordprocessingDocument)` → `ScanResult(List<(string Tag, bool IsInsideBlock)> Tags, bool HasBlock)`.
- Produces: нічого для наступних задач.

Без цієї задачі попередні дві працюють лише наполовину. Шаблон, чиє повторення живе тільки в таблиці, дістане `HasBlock = false`, `Upload` поставить `Kind = TemplateKind.PerRecipient`, і документ **ніколи не потрапить у групову фазу** — тихо, без помилки. Крім того, теги в рядках тіла мусять діставати `IsInsideRepeatingBlock = true`, інакше вони потраплять у форму ручних міток як одне значення на весь пакет замість значення на людину.

- [ ] **Step 1: Написати падаючі тести**

Create `GenDoc.Tests/Templates/TableBlockScanTests.cs`:

```csharp
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GenDoc.Services.Templates;

namespace GenDoc.Tests.Templates;

// Якщо сканер не бачить маркерних рядків, шаблон класифікується як документ на
// одну людину й ніколи не потрапляє в групову фазу — мовчки, без помилки.
public class TableBlockScanTests
{
    private static TableRow Row(params string[] cells)
        => new(cells.Select(c => new TableCell(new Paragraph(new Run(new Text(c))))));

    private static TemplateService.ScanResult Scan(Action<Body> fill)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            doc.AddMainDocumentPart().Document = new Document(new Body());
            fill(doc.MainDocumentPart!.Document!.Body!);
            doc.MainDocumentPart.Document.Save();
        }

        using var reopened = WordprocessingDocument.Open(new MemoryStream(stream.ToArray()), false);
        return TemplateService.ScanPlaceholders(reopened);
    }

    private static void FillTableTemplate(Body body)
    {
        body.AppendChild(new Paragraph(new Run(new Text("Затверджую {{піб_командира}}"))));
        body.AppendChild(new Table(
            Row("№", "ПІБ"),
            Row("{{#список}}"),
            Row("{{номер}}", "{{піб}} {{звання}}"),
            Row("{{/список}}"),
            Row("Усього", "{{кількість_осіб}}")));
    }

    // ── Характеризаційні: проходять і до змін ────────────────────────
    // Плоский обхід перелічує абзаци в порядку документа, включно з тими, що в
    // комірках, тож для простої таблиці він маркерний рядок таки бачить. Ці
    // тести не доводять виправлення — вони стережуть, щоб перебудова нічого не
    // зламала.

    [Fact]
    public void TableMarkerRows_MarkTemplateAsGroup()
    {
        var scan = Scan(FillTableTemplate);
        Assert.True(scan.HasBlock);
    }

    [Fact]
    public void TagsInsideRepeatedRow_AreFlaggedAsInsideBlock()
    {
        var tags = Scan(FillTableTemplate).Tags.ToDictionary(t => t.Tag, t => t.IsInsideBlock, StringComparer.Ordinal);

        Assert.True(tags["{{піб}}"]);
        Assert.True(tags["{{звання}}"]);
        Assert.False(tags["{{піб_командира}}"]);
    }

    // {{номер}} і {{кількість_осіб}} обчислює рушій — вони не поля й у мапінг
    // потрапляти не мають, інакше з'являться зайві рядки у формі ручних міток.
    [Fact]
    public void EngineTags_AreNotCollected()
    {
        var tags = Scan(FillTableTemplate).Tags.Select(t => t.Tag).ToList();

        Assert.DoesNotContain("{{номер}}", tags);
        Assert.DoesNotContain("{{кількість_осіб}}", tags);
    }

    // ── Розрізняльні: падають до змін ────────────────────────────────

    // Плоский обхід тримає стан блоку в одній змінній на весь контейнер, тож
    // незакритий маркер у таблиці позначає «всередині блоку» все, що йде далі
    // по документу. Структурований обхід тримає стан у межах тієї таблиці.
    [Fact]
    public void UnclosedMarkerRow_DoesNotLeakBlockStateOntoTheRestOfTheDocument()
    {
        var scan = Scan(body =>
        {
            body.AppendChild(new Table(
                Row("{{#список}}"),
                Row("{{піб}}")));
            body.AppendChild(new Paragraph(new Run(new Text("Підписав {{піб_командира}}"))));
        });

        var tags = scan.Tags.ToDictionary(t => t.Tag, t => t.IsInsideBlock, StringComparer.Ordinal);

        Assert.False(tags["{{піб_командира}}"]);
    }

    // Маркерний рядок усередині блоку рівня документа: плоский обхід приймає
    // його за справжній блок і на «{{/внутрішній}}» закриває облік, тож теги
    // після нього, але всередині зовнішнього блоку, лишаються непозначеними.
    [Fact]
    public void RowMarkerInsideParagraphBlock_DoesNotCloseTheOuterBlockEarly()
    {
        var scan = Scan(body =>
        {
            body.AppendChild(new Paragraph(new Run(new Text("{{#зовнішній}}"))));
            body.AppendChild(new Table(
                Row("{{#внутрішній}}"),
                Row("{{піб}}"),
                Row("{{/внутрішній}}")));
            body.AppendChild(new Paragraph(new Run(new Text("{{звання}}"))));
            body.AppendChild(new Paragraph(new Run(new Text("{{/зовнішній}}"))));
        });

        var tags = scan.Tags.ToDictionary(t => t.Tag, t => t.IsInsideBlock, StringComparer.Ordinal);

        Assert.True(tags["{{звання}}"]);
    }

    // Маркер збігається зі звичайним регексом плейсхолдера, тож без окремої
    // перевірки він потрапив би в мапінг як поле й з'явився б у формі ручних
    // міток. Генерація такий шаблон однаково відхилить (Task 2), але в мапінгу
    // сміття лишатись не має.
    [Fact]
    public void BlockMarkersAreNeverCollectedAsFields()
    {
        var scan = Scan(body =>
        {
            body.AppendChild(new Paragraph(new Run(new Text("{{#зовнішній}}"))));
            body.AppendChild(new Table(
                Row("{{#внутрішній}}"),
                Row("{{піб}}"),
                Row("{{/внутрішній}}")));
            body.AppendChild(new Paragraph(new Run(new Text("{{/зовнішній}}"))));
        });

        Assert.DoesNotContain(scan.Tags, t => t.Tag.StartsWith("{{#", StringComparison.Ordinal));
        Assert.DoesNotContain(scan.Tags, t => t.Tag.StartsWith("{{/", StringComparison.Ordinal));
    }

    [Fact]
    public void TemplateWithoutMarkers_IsNotAGroupTemplate()
    {
        var scan = Scan(body => body.AppendChild(new Table(
            Row("№", "ПІБ"),
            Row("1", "{{піб}}"))));

        Assert.False(scan.HasBlock);
        Assert.All(scan.Tags, t => Assert.False(t.IsInsideBlock));
    }
}
```

- [ ] **Step 2: Прогнати — три розрізняльні мають впасти**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj --filter "FullyQualifiedName~TableBlockScanTests" -v q --nologo`
Expected: FAIL, **3 failed, 4 passed**.

Падають саме розрізняльні: `UnclosedMarkerRow_DoesNotLeakBlockStateOntoTheRestOfTheDocument`, `RowMarkerInsideParagraphBlock_DoesNotCloseTheOuterBlockEarly`, `BlockMarkersAreNeverCollectedAsFields`. Чотири характеризаційні проходять — так і має бути, вони стережуть наявну поведінку.

Якщо падає інший набір — зупинитись і повідомити. Це означає, що плоский обхід поводиться не так, як описано, і Step 3 може виявитись зайвим або неповним. Записати фактичний перелік у звіт: саме він є доказом того, що задача не декоративна.

- [ ] **Step 3: Перебудувати сканер на `BlockStructure`**

У `GenDoc/Services/Templates/TemplateService.cs` видалити власні `BlockOpenRegex` і `BlockCloseRegex` разом із коментарем «тримати в синхроні» — вони переїхали в `BlockStructure`.

Замінити тіло `ScanPlaceholders`:

```csharp
        // Обхід повторює той самий порядок, що й рушій (BlockStructure): блокові
        // діти контейнера, а всередину таблиці по маркерні рядки — лише поки не
        // відкрито блок рівня документа.
        internal static ScanResult ScanPlaceholders(WordprocessingDocument doc)
        {
            var seen = new Dictionary<string, int>(StringComparer.Ordinal); // тег → індекс у tags
            var tags = new List<(string Tag, bool IsInsideBlock)>();
            var hasBlock = false;

            void CollectTags(OpenXmlElement element, bool insideBlock)
            {
                var paragraphs = element is Paragraph paragraph
                    ? new[] { paragraph }.AsEnumerable()
                    : element.Descendants<Paragraph>();

                foreach (var inner in paragraphs)
                {
                    var text = string.Concat(inner.Descendants<Text>().Select(t => t.Text)).Trim();

                    foreach (Match match in PlaceholderRegex.Matches(text))
                    {
                        if (ReservedBlockTags.Contains(match.Value)) continue;

                        // Маркер збігається зі звичайним регексом плейсхолдера.
                        // Сюди він доходить лише з елемента всередині вже
                        // відкритого блоку — генерація такий шаблон відхилить,
                        // але в мапінг «{{#…}}» потрапляти не має.
                        if (BlockStructure.IsMarkerTag(match.Value)) continue;

                        if (seen.TryGetValue(match.Value, out var index))
                        {
                            if (insideBlock && !tags[index].IsInsideBlock)
                                tags[index] = (match.Value, true);
                        }
                        else
                        {
                            seen[match.Value] = tags.Count;
                            tags.Add((match.Value, insideBlock));
                        }
                    }
                }
            }

            void ScanSiblings(List<OpenXmlElement> siblings, bool insideTable)
            {
                string? openName = null;

                foreach (var element in siblings)
                {
                    var markerText = BlockStructure.MarkerText(element);

                    if (BlockStructure.OpenName(markerText) is { } opened)
                    {
                        openName = opened;
                        hasBlock = true;
                        continue;
                    }

                    if (BlockStructure.CloseName(markerText) is not null)
                    {
                        openName = null;
                        continue;
                    }

                    if (openName is null && !insideTable && element is Table table)
                    {
                        ScanSiblings(BlockStructure.Rows(table).Cast<OpenXmlElement>().ToList(), insideTable: true);
                        continue;
                    }

                    CollectTags(element, insideBlock: openName is not null);
                }
            }

            void ScanContainer(OpenXmlCompositeElement? container)
            {
                if (container is null) return;
                ScanSiblings(BlockStructure.BlockChildren(container).ToList(), insideTable: false);
            }

            var mainPart = doc.MainDocumentPart;
            if (mainPart?.Document?.Body is not null)
                ScanContainer(mainPart.Document.Body);

            if (mainPart is not null)
            {
                foreach (var header in mainPart.HeaderParts)
                    ScanContainer(header.Header);

                foreach (var footer in mainPart.FooterParts)
                    ScanContainer(footer.Footer);
            }

            return new ScanResult(tags, hasBlock);
        }
```

Прибрати `using System.Text.RegularExpressions;`, якщо після видалення регексів він лишився без користувачів — `PlaceholderRegex` у цьому ж файлі його ще потребує, тож найімовірніше він лишається. Звірити збіркою: нуль попереджень.

- [ ] **Step 4: Прогнати нові тести**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj --filter "FullyQualifiedName~TableBlockScanTests" -v q --nologo`
Expected: PASS, 7 passed.

- [ ] **Step 5: Прогнати весь набір**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj -v q --nologo`
Expected: PASS, **286 passed, 0 skipped**, нуль попереджень.

Особливо звірити `GenDoc.Tests/TemplateServiceScanTests.cs` і `GenDoc.Tests/Templates/TemplateScanRealFilesTests.cs` — вони фіксують абзацний скан на справжніх рапортах, зокрема що `{{роздільник}}` у мапінг не потрапляє, а теги тіла блоку позначені.

- [ ] **Step 6: Коміт**

```bash
git add GenDoc/Services/Templates/TemplateService.cs GenDoc.Tests/Templates/TableBlockScanTests.cs
git commit -m "fix: recognise table marker rows when scanning a template"
```

---

## Підсумок

Після Task 3 набір має бути **286 passed, 0 skipped, 0 failed**, нуль попереджень.
Приріст: 270 базових → 275 (Task 1: +5 нових, тест Task 17 переписано, не додано) →
279 (Task 2: +4) → 286 (Task 3: +7).

| Вимога специфікації | Задача |
|---|---|
| Маркерні рядки відкривають і закривають блок у таблиці | Task 1 |
| Рядки тіла клонуються на кожну людину, маркери видаляються | Task 1 |
| `{{номер}}`, `{{роздільник}}`, `{{кількість_осіб}}` у рядках | Task 1 |
| Спільний `BlockStructure` для сканера й рушія | Task 1 (створення), Task 3 (друге використання) |
| Блокові елементи інших типів проходять наскрізь | Task 1 (`BlockChildren`) |
| Ціла таблиця в блоці клонується; тест Task 17 переписано | Task 1 |
| `gridSpan` переживає клонування | Task 1 |
| Відмова на `vMerge` | Task 2 |
| Вкладені блоки, перетин межі таблиці, незакритий блок у таблиці | Task 2 |
| `HasBlock` і `TemplateKind.Group` для табличних блоків | Task 3 |
| `IsInsideRepeatingBlock` для тегів рядків тіла | Task 3 |
| Стан блоку не протікає за межі таблиці | Task 3 |
| Маркери не потрапляють у мапінг як поля | Task 3 (`IsMarkerTag`) |
| Регресія абзацного шляху на справжньому шаблоні | Task 1 Step 7, Task 3 Step 5 |

## Знайдене під час роботи

Розділ для дефектів, які виявляться під час виконання і не входили до специфікації. Записувати сюди, а не виправляти на місці.

- (порожньо)
