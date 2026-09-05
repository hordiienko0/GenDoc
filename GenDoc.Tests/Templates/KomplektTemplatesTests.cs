using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Services.Templates;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Generation;
using TemplateKind = GenDoc.Models.Enums.TemplateKind;

namespace GenDoc.Tests.Templates;

/// <summary>
/// Дзеркальні шаблони з теки «шаблони/комплекти»: кожен завантажується через сервіси застосунку,
/// скан знаходить рівно очікувані теги (жодного випадкового Manual через одруківку),
/// а справжній прогін пакета для трьох людей не лишає «{{» у жодному файлі.
/// </summary>
public class KomplektTemplatesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gk-{Guid.NewGuid():N}"[..12]);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static readonly IProgress<string> NoProgress = new Progress<string>(_ => { });

    private const string Signer = "звання_підписанта";
    private const string SignerName = "піб_підписанта";

    // ---------------------------------------------------------------------
    // Каталог: файл → тип, автоматичні теги (людина/організація), ручні теги
    // ---------------------------------------------------------------------

    public sealed record DocxSpec(string Set, string File, TemplateKind Kind, string[] Auto, string[] Manual)
    {
        public override string ToString() => File;
    }

    public sealed record XlsxSpec(string Set, string File, int TemplateRow, string[] Auto, string[] Manual)
    {
        public override string ToString() => File;
    }

    private static string[] Range(string prefix, int from, int to, string suffix = "")
        => Enumerable.Range(from, to - from + 1).Select(i => $"{prefix}{i}{suffix}").ToArray();

    private static string[] OrderManual(bool limited, bool extract)
    {
        var tags = new List<string>();
        if (extract) tags.AddRange(new[] { "дата_наказу", "номер_наказу" });

        tags.AddRange(new[] { "керівник_стрільби_дв", "резерв_керівника", "дати_стрільб", "назва_набору" });

        var shootings = limited ? 6 : 7;
        tags.AddRange(Range("вправи_стрільби_", 1, shootings));
        tags.AddRange(Range("дата_стрільби_", 1, shootings));
        if (limited) tags.Add("дата_метання_гранат");
        tags.Add("керівник_стрільби_зв");

        var pbp = limited ? 5 : 4;
        tags.AddRange(Range("дні_пбп_", 1, pbp));
        tags.AddRange(Range("начальник_пбп_", 1, pbp, "_зв"));

        tags.AddRange(Range("ділянка_", 1, shootings));
        if (limited) tags.Add("ділянка_метання_гранат");

        tags.AddRange(Range("набої_вправа_", 1, limited ? 16 : 18));
        tags.Add("гранати_ргд");
        if (!limited) tags.AddRange(new[] { "гранати_навчальні_1", "гранати_навчальні_2" });

        tags.AddRange(new[]
        {
            "начальник_варти_зв", "зброя_начальника_варти", "вартовий_зв", "зброя_вартового",
            "кількість_акм_вксс", "отримувач_боєприпасів_дв", "кількість_акм_61",
            "начальник_курсів_дв", "кількість_рдг", "старший_машини_зв"
        });

        if (extract) tags.AddRange(new[] { "звання_засвідчувача", "піб_засвідчувача" });
        return tags.ToArray();
    }

    private static readonly string[] Commander = { "звання_командира", "піб_командира" };

    private static readonly string[] Person = { "звання", "піб" };

    public static readonly DocxSpec[] DocxCatalog =
    {
        new("Стрільби", "Шаблон_Заявка на боєприпаси (придатні).docx", TemplateKind.Group,
            Array.Empty<string>(),
            new[]
            {
                "дати_стрільб", "дата_розпорядження_гш", "кількість_набоїв_1", "кількість_набоїв_2", "кількість_ург",
                Signer, SignerName, "начальник_служби_засобів_ураження", "комірник_складу"
            }),
        new("Стрільби", "Шаблон_Заявка на боєприпаси (обмежено придатні).docx", TemplateKind.Group,
            Array.Empty<string>(),
            new[]
            {
                "дати_стрільб", "кількість_набоїв_1",
                Signer, SignerName, "начальник_служби_засобів_ураження", "комірник_складу"
            }),
        new("Стрільби", "Шаблон_Витяг з наказу на стрільби (придатні).docx", TemplateKind.Group,
            Commander, OrderManual(limited: false, extract: true)),
        new("Стрільби", "Шаблон_Витяг з наказу на стрільби (обмежено придатні).docx", TemplateKind.Group,
            Commander, OrderManual(limited: true, extract: true)),
        new("Стрільби", "Шаблон_Проєкт наказу на стрільби (придатні).docx", TemplateKind.Group,
            Commander, OrderManual(limited: false, extract: false)),
        new("Стрільби", "Шаблон_Проєкт наказу на стрільби (обмежено придатні).docx", TemplateKind.Group,
            Commander, OrderManual(limited: true, extract: false)),
        new("Стрільби", "Шаблон_Роздавально-здавальна відомість (форма 9а).docx", TemplateKind.Group,
            new[] { "піб_ініціали", "номер_вч" },
            new[] { "номер_відомості", "дата", "дата_аркуша", "калібр", "кількість_патронів", "начальник_пбп", Signer, SignerName, "керівник_стрільб" }),
        new("Стрільби", "Шаблон_Стройова записка на стрільби (Додаток 3).docx", TemplateKind.Group,
            Array.Empty<string>(), new[] { "дата" }),
        new("Стрільби", "Шаблон_Заявка на використання автомобільної техніки.docx", TemplateKind.Group,
            Array.Empty<string>(),
            new[] { "дата_перевезення", "номер_наказу", "дата_наказу", "старший_машини", Signer, SignerName, "дата" }),

        new("Котлове забезпечення", "Шаблон_Рапорт про зарахування на котлове забезпечення (груповий).docx", TemplateKind.Group,
            Person, new[] { "дата_прибуття", "дата_зарахування", Signer, SignerName, "дата_рапорту" }),
        new("Котлове забезпечення", "Шаблон_Рапорт про зарахування на котлове забезпечення (індивідуальний).docx", TemplateKind.PerRecipient,
            new[] { "звання_зв", "піб_зв", "таким", "прибув", "дата_посвідчення", "номер_посвідчення", "прод_атестат" },
            new[] { "дата_прибуття", "дата_зарахування", Signer, SignerName, "дата_рапорту" }),
        new("Котлове забезпечення", "Шаблон_Рапорт про зняття з котлового забезпечення (індивідуальний).docx", TemplateKind.PerRecipient,
            new[] { "звання_зв", "піб_зв", "таким", "дата_посвідчення", "номер_посвідчення" },
            new[] { "дата_вибуття", "дата_зняття", Signer, SignerName, "дата_рапорту" }),
        new("Котлове забезпечення", "Шаблон_Рапорт про зняття з котлового забезпечення (груповий).docx", TemplateKind.Group,
            Person, new[] { "дата_вибуття", "дата_зняття", Signer, SignerName, "дата_рапорту" }),

        new("Відрядження", "Шаблон_Рапорт про направлення у відрядження (груповий).docx", TemplateKind.Group,
            Person, new[] { "вос_курсу", "місце_відрядження", "дата_вибуття", "підстава", Signer, SignerName, "дата_рапорту" }),

        new("Стройовий облік", "Шаблон_Стройова записка курсів.docx", TemplateKind.Group,
            Array.Empty<string>(),
            new[]
            {
                "час", "дата",
                "за_списком_кппк", "за_списком_бзвп", "за_списком_загалом",
                "у_строю_кппк", "у_строю_бзвп", "у_строю_загалом",
                "шпиталь_кппк", "шпиталь_бзвп", "шпиталь_загалом",
                "відрядження_кппк", "відрядження_бзвп", "відрядження_загалом",
                Signer, SignerName, "відрядження", "шпиталь", "звільнення"
            }),
        new("Стройовий облік", "Шаблон_Титульний аркуш іменного списку вечірньої перевірки.docx", TemplateKind.Group,
            Array.Empty<string>(), new[] { "населений_пункт", "рік" }),

        new("Інше", "Шаблон_Звернення на отримання благодійної допомоги.docx", TemplateKind.Group,
            Array.Empty<string>(), new[] { "адресат", "піб_адресата", "перелік_матеріалів", Signer, SignerName }),
        new("Інше", "Шаблон_Пояснювальна записка про незібрані гільзи.docx", TemplateKind.Group,
            Array.Empty<string>(),
            new[] { "дати_стрільб", "номер_наказу", "дата_наказу", "кількість_набоїв", "причина", Signer, SignerName, "дата" })
    };

    public static readonly XlsxSpec[] XlsxCatalog =
    {
        new("Стрільби", "Шаблон_Відомість допуску до стрільб (Додаток 5).xlsx", 7,
            new[] { "№", "звання", "піб", "оцінка_1", "оцінка_2", "оцінка_3", "оцінка_4", "оцінка_загальна", "номер_вч" },
            new[] { "номери_вправ", Signer, SignerName, "дата_аркуша" }),
        new("Стрільби", "Шаблон_Відомість складання заліку з вимог безпеки (Додаток 8).xlsx", 10,
            new[] { "№", "звання", "піб", "курсовий_офіцер", "номер_вч" },
            new[] { "опис_підрозділу", "дата_аркуша", "причина_інструктажу", Signer, SignerName }),
        new("Стрільби", "Шаблон_Роздавально-здавальна відомість.xlsx", 9,
            new[] { "піб_ініціали", "курсовий_офіцер", "номер_вч" },
            new[] { "номер_відомості", "дата_аркуша", "калібр", "кількість_патронів", Signer, SignerName, "керівник_стрільб" }),
        new("Стройовий облік", "Шаблон_Список вечірньої перевірки.xlsx", 4,
            new[] { "№", "звання", "піб" },
            new[] { Signer, SignerName })
    };

    public static IEnumerable<object[]> DocxCases() => DocxCatalog.Select(s => new object[] { s });
    public static IEnumerable<object[]> XlsxCases() => XlsxCatalog.Select(s => new object[] { s });

    // ---------------------------------------------------------------------
    // Допоміжне
    // ---------------------------------------------------------------------

    private static string KomplektRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "шаблони", "комплекти")))
            dir = dir.Parent;
        return dir is null
            ? throw new DirectoryNotFoundException("Не знайдено теку «шаблони/комплекти»")
            : Path.Combine(dir.FullName, "шаблони", "комплекти");
    }

    private static string PathOf(string set, string file)
    {
        var path = Path.Combine(KomplektRoot(), set, file);
        Assert.True(File.Exists(path), $"Немає файлу шаблону: {path}");
        return path;
    }

    private static string Braced(string tag) => "{{" + tag + "}}";

    private static TemplateService Templates(TestDb db) => new(db.Factory, new FakeAuditLog(), new FakeCurrentUser());

    private static ExportTemplateService ExportTemplates(TestDb db) => new(db.Factory, new FakeAuditLog(), new FakeCurrentUser());

    private static void AssertSameTags(string what, IEnumerable<string> expectedInner, IEnumerable<string> actualBraced)
    {
        var expected = expectedInner.Select(Braced).OrderBy(t => t, StringComparer.Ordinal).ToList();
        var actual = actualBraced.Distinct().OrderBy(t => t, StringComparer.Ordinal).ToList();

        var missing = expected.Except(actual, StringComparer.Ordinal).ToList();
        var extra = actual.Except(expected, StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0 && extra.Count == 0,
            $"{what}: очікувані теги не збігаються зі сканом. "
            + $"Бракує: [{string.Join(", ", missing)}]. Зайві: [{string.Join(", ", extra)}]");
    }

    private static string DocxText(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var main = doc.MainDocumentPart!;
        var parts = new List<string> { string.Concat(main.Document!.Body!.Descendants<Text>().Select(t => t.Text)) };
        parts.AddRange(main.HeaderParts.Select(h => string.Concat(h.Header!.Descendants<Text>().Select(t => t.Text))));
        parts.AddRange(main.FooterParts.Select(f => string.Concat(f.Footer!.Descendants<Text>().Select(t => t.Text))));
        return string.Join("\n", parts);
    }

    private static List<string> XlsxCells(string path)
    {
        using var workbook = new XLWorkbook(path);
        return workbook.Worksheets
            .SelectMany(ws => ws.RangeUsed()?.CellsUsed().Select(c => c.GetString()) ?? Enumerable.Empty<string>())
            .ToList();
    }

    // ---------------------------------------------------------------------
    // 1. DOCX: завантаження через TemplateService і класифікація тегів
    // ---------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(DocxCases))]
    public void Docx_Upload_ScansExactlyTheExpectedTags(DocxSpec spec)
    {
        using var db = new TestDb();

        var result = Templates(db).Upload(PathOf(spec.Set, spec.File));
        Assert.True(result.Success, result.ErrorMessage);

        using var ctx = db.NewContext();
        var template = Assert.Single(ctx.Templates);
        var mappings = ctx.TemplateFieldMappings.Where(m => m.TemplateId == template.Id).ToList();

        Assert.Equal(spec.Kind, template.Kind);
        Assert.StartsWith("Шаблон_", spec.File, StringComparison.Ordinal);
        Assert.False(template.Name.Contains("{{", StringComparison.Ordinal));

        AssertSameTags("ручні", spec.Manual,
            mappings.Where(m => m.SourceType == MappingSourceType.Manual).Select(m => m.PlaceholderTag));
        AssertSameTags("автоматичні", spec.Auto,
            mappings.Where(m => m.SourceType != MappingSourceType.Manual).Select(m => m.PlaceholderTag));

        Assert.All(mappings.Where(m => m.SourceType != MappingSourceType.Manual),
            m => Assert.False(string.IsNullOrEmpty(m.FieldName), $"{m.PlaceholderTag}: немає поля"));

        foreach (var reserved in new[] { "{{роздільник}}", "{{номер}}", "{{кількість_осіб}}" })
            Assert.DoesNotContain(mappings, m => m.PlaceholderTag == reserved);

        if (spec.Kind == TemplateKind.Group)
        {
            var inBlock = mappings.Where(m => m.IsInsideRepeatingBlock).Select(m => m.PlaceholderTag).ToList();
            var perPerson = spec.Auto.Where(t => PlaceholderTagMaps.Classify(Braced(t)).SourceType == MappingSourceType.Recipient)
                .Select(Braced).ToList();
            foreach (var tag in perPerson)
                Assert.Contains(tag, inBlock);
        }
    }

    [Fact]
    public void Docx_ManualTags_AreSnakeCaseUkrainian_AndSharedNamesMeanTheSameThing()
    {
        var allManual = DocxCatalog.SelectMany(s => s.Manual).Concat(XlsxCatalog.SelectMany(s => s.Manual)).Distinct().ToList();

        foreach (var tag in allManual)
        {
            Assert.Matches(@"^[а-яіїєґ0-9_]+$", tag);
            Assert.DoesNotContain("__", tag);
            Assert.Equal(MappingSourceType.Manual, PlaceholderTagMaps.Classify(Braced(tag)).SourceType);
        }

        foreach (var tag in new[] { "дата", "дата_рапорту", "дата_аркуша" })
            Assert.True(ManualTagClassifier.IsDocumentDate(tag));

        Assert.True(ManualTagClassifier.HasSignerPair(new[] { Braced(Signer), Braced(SignerName) }));
    }

    // ---------------------------------------------------------------------
    // 2. XLSX: завантаження через ExportTemplateService, рядок-шаблон і теги
    // ---------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(XlsxCases))]
    public void Xlsx_Upload_FindsTemplateRow_AndScansExactlyTheExpectedTags(XlsxSpec spec)
    {
        using var db = new TestDb();

        var result = ExportTemplates(db).UploadTemplate(PathOf(spec.Set, spec.File));
        Assert.True(result.Success, result.ErrorMessage);

        using var ctx = db.NewContext();
        var template = Assert.Single(ctx.ExportTemplates);
        var mappings = ctx.ExportTemplateColumnMappings.Where(m => m.ExportTemplateId == template.Id).ToList();

        Assert.True(template.UsesPlaceholders, "Шаблон має працювати через теги, а не через заголовки колонок");
        Assert.Equal(spec.TemplateRow, template.TemplateRowIndex);

        AssertSameTags("ручні", spec.Manual,
            mappings.Where(m => m.SourceType == MappingSourceType.Manual).Select(m => m.PlaceholderTag));
        AssertSameTags("автоматичні", spec.Auto,
            mappings.Where(m => m.SourceType != MappingSourceType.Manual).Select(m => m.PlaceholderTag));

        var rowTags = mappings.Where(m => m.ColumnIndex > 0).Select(m => m.PlaceholderTag).ToList();
        Assert.Contains(rowTags, t => t is "{{піб}}" or "{{піб_ініціали}}");

        var allowedManualInRow = new[] { "{{дата_аркуша}}", "{{калібр}}", "{{кількість_патронів}}", "{{причина_інструктажу}}" };
        var strayManualInRow = mappings
            .Where(m => m.ColumnIndex > 0 && m.SourceType == MappingSourceType.Manual && !allowedManualInRow.Contains(m.PlaceholderTag))
            .Select(m => m.PlaceholderTag)
            .ToList();
        Assert.True(strayManualInRow.Count == 0, "У рядку-шаблоні несподівані ручні теги: " + string.Join(", ", strayManualInRow));

        using var workbook = new XLWorkbook(PathOf(spec.Set, spec.File));
        Assert.Single(workbook.Worksheets);
    }

    // ---------------------------------------------------------------------
    // 3. Наскрізний прогін: усі шаблони одним пакетом для трьох людей
    // ---------------------------------------------------------------------

    private static Dictionary<string, string> ManualValuesForEverything()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var all = DocxCatalog.SelectMany(s => s.Manual).Concat(XlsxCatalog.SelectMany(s => s.Manual)).Distinct();

        foreach (var tag in all)
        {
            values[Braced(tag)] = ManualTagClassifier.Classify(tag) switch
            {
                ManualTagKind.Date when ManualTagClassifier.IsDocumentDate(tag) => "07.08.2026",
                ManualTagKind.Date => "01 серпня 2026 року",
                _ => $"[{tag}]"
            };
        }

        return values;
    }

    private sealed record Seeded(int PackageId, int IntakeId, int CourseOfficerId);

    private Seeded SeedEverything(TestDb db)
    {
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Users.Add(new UserProfile { FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now });
            ctx.AppSettings.Add(new AppSettings { DefaultOutputFolder = _folder });
            ctx.OrganizationSettings.Add(new OrganizationSettings
            {
                UnitNumber = "А0000", City = "Харків",
                CommanderRank = "бригадний генерал", CommanderFullName = "Іван КОМАНДИР",
                CommanderPosition = "Начальник університету", HrOfficerFullName = "Петро КАДРОВИК",
                UnitFullName = "Харківський національний університет Повітряних Сил"
            });

            var intake = new Intake
            {
                Number = 29, DisplayNumber = "Набір №29", Status = IntakeStatus.Active,
                DateStart = new DateOnly(2026, 7, 15), DateEnd = new DateOnly(2026, 8, 30)
            };
            ctx.Intakes.Add(intake);

            var room = new Room { Building = "1", Number = "305", Capacity = 6 };
            ctx.Rooms.Add(room);
            ctx.SaveChanges();

            var ranks = new[] { ("майор", "майора"), ("капітан", "капітана"), ("старший лейтенант", "старшого лейтенанта") };
            for (var i = 0; i < 3; i++)
            {
                var person = TemplateFixtures.Person(i + 1, $"ПРІЗВИЩЕ{i + 1:00}", $"Ім'я{i + 1:00}", ranks[i].Item1);
                person.IntakeId = intake.Id;
                person.RoomId = room.Id;
                person.FitnessCategory = i == 2 ? "обмежено придатний" : "придатний";
                person.Gender = Gender.Male;
                person.RankAccusative = ranks[i].Item2;
                person.FullNameAccusative = $"ПРІЗВИЩЕ{i + 1:00} Ім'я{i + 1:00} Петровича";
                person.TravelCertificateNumber = $"№ {100 + i}";
                person.TravelCertificateDate = new DateOnly(2026, 7, 13);
                person.FoodCertificate = $"ПА-{10 + i}";
                person.Weapons.Add(new Weapon { Name = "АКМ", SerialNumber = $"АА {1000 + i}" });
                ctx.Recipients.Add(person);
            }

            ctx.Recipients.Add(new Recipient
            {
                Id = 100, LastName = "КОВАЛЬЧУК", FirstName = "Василь", MiddleName = "Богданович",
                Rank = "капітан", Position = "курсовий офіцер", IsCourseOfficer = true, IntakeId = null
            });
            ctx.SaveChanges();
        }

        var templates = Templates(db);
        foreach (var spec in DocxCatalog)
        {
            var upload = templates.Upload(PathOf(spec.Set, spec.File));
            Assert.True(upload.Success, $"{spec.File}: {upload.ErrorMessage}");
        }

        var exportTemplates = ExportTemplates(db);
        foreach (var spec in XlsxCatalog)
        {
            var upload = exportTemplates.UploadTemplate(PathOf(spec.Set, spec.File));
            Assert.True(upload.Success, $"{spec.File}: {upload.ErrorMessage}");
        }

        using var seeded = db.NewContext();
        var templateIds = seeded.Templates.Select(t => t.Id).ToList();
        var exportIds = seeded.ExportTemplates.Select(t => t.Id).ToList()
            .Select(id => (id, FitnessFilter.All)).ToList();

        var packageId = TestServices.Generation(db).CreatePackage("Комплекти", null, templateIds, exportIds);
        return new Seeded(packageId, seeded.Intakes.Single().Id, 100);
    }

    [Fact]
    public void RealRun_AllKomplektTemplates_ForThreePeople_LeaveNoPlaceholders()
    {
        using var db = new TestDb();
        var seeded = SeedEverything(db);

        var selection = new RosterSelection(
            true, Array.Empty<int>(), FitnessFilter.All, false,
            Array.Empty<RankCategory>(), Array.Empty<string>(), IntakeId: seeded.IntakeId);

        var result = TestServices.Generation(db).RunPackage(
            seeded.PackageId, _folder, ManualValuesForEverything(), regenerateExisting: true,
            selection, NoProgress, courseOfficerId: seeded.CourseOfficerId);

        var problems = result.Issues!
            .Where(i => i.IsError || i.Message.Contains("не заповнено", StringComparison.Ordinal))
            .Select(i => $"{i.Phase} · {i.TemplateName} · {i.Message}")
            .ToList();
        Assert.True(problems.Count == 0, "Прогін із помилками або незаповненими тегами:\n" + string.Join("\n", problems));

        var perRecipient = DocxCatalog.Count(s => s.Kind == TemplateKind.PerRecipient);
        var group = DocxCatalog.Count(s => s.Kind == TemplateKind.Group);

        Assert.Equal(perRecipient * 3, result.Generated);
        Assert.Equal(group, result.DocxGroupGenerated);
        Assert.Equal(XlsxCatalog.Length, result.GroupGenerated);
        Assert.Equal(0, result.Errors + result.GroupErrors + result.DocxGroupErrors);

        var docxFiles = Directory.GetFiles(_folder, "*.docx", SearchOption.AllDirectories);
        var xlsxFiles = Directory.GetFiles(_folder, "*.xlsx", SearchOption.AllDirectories);
        Assert.Equal(perRecipient * 3 + group, docxFiles.Length);
        Assert.Equal(XlsxCatalog.Length, xlsxFiles.Length);

        foreach (var file in docxFiles)
            Assert.False(DocxText(file).Contains("{{", StringComparison.Ordinal), $"Лишились теги у {file}");

        foreach (var file in xlsxFiles)
        {
            var cells = XlsxCells(file);
            Assert.DoesNotContain(cells, c => c.Contains("{{", StringComparison.Ordinal));
            foreach (var lastName in new[] { "ПРІЗВИЩЕ01", "ПРІЗВИЩЕ02", "ПРІЗВИЩЕ03" })
                Assert.Contains(cells, c => c.Contains(lastName, StringComparison.Ordinal));
        }

        var groupDocx = docxFiles.Where(f => f.Contains("груповий", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.All(groupDocx, f =>
        {
            var text = DocxText(f);
            foreach (var lastName in new[] { "ПРІЗВИЩЕ01", "ПРІЗВИЩЕ02", "ПРІЗВИЩЕ03" })
                Assert.Contains(lastName, text);
        });

        var zayavka = Assert.Single(docxFiles, f => f.Contains("Заявка на боєприпаси (придатні)", StringComparison.Ordinal));
        Assert.Contains("Кількість осіб, що стріляють: 3.", DocxText(zayavka));

        var order = Assert.Single(docxFiles, f => f.Contains("Витяг з наказу на стрільби (придатні)", StringComparison.Ordinal));
        var orderText = DocxText(order);
        Assert.Contains("на 3 учасників стрільби", orderText);
        Assert.Contains("бригадний генерал", orderText);
        Assert.Contains("Іван КОМАНДИР", orderText);

        var forma9a = Assert.Single(docxFiles, f => f.Contains("форма 9а", StringComparison.Ordinal));
        var forma9aText = DocxText(forma9a);
        Assert.Contains("ПРІЗВИЩЕ01 І. П.", forma9aText);
        Assert.Contains("А0000", forma9aText);

        var individual = docxFiles.Where(f => f.Contains("індивідуальний", StringComparison.Ordinal)).ToList();
        Assert.Equal(perRecipient * 3, individual.Count);
        Assert.Contains(individual, f => DocxText(f).Contains("майора ПРІЗВИЩЕ01"));
    }
}
