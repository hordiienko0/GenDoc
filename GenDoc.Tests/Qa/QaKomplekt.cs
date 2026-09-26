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

namespace GenDoc.Tests.Qa;

/// <summary>
/// Спільне для QA-прогонів блоку А: синтетичний набір із чотирьох людей (двоє придатних чоловіків,
/// обмежено придатний чоловік і придатна жінка), курсовий офіцер, начальник курсів у налаштуваннях,
/// завантаження теки з «шаблони/комплекти» через <see cref="TemplateFolderImportService"/> і заповнення
/// ручних тегів тією самою формою, що й наживо.
/// </summary>
internal static class QaKomplekt
{
    public const int UserId = 1;
    public const int CourseOfficerId = 100;

    public static readonly string[] Surnames = { "ШЕВЧЕНКО", "ГОНЧАР", "БІЛИЙ", "ПЕТРОВА" };
    public const string LimitedSurname = "БІЛИЙ";
    public const string FemaleSurname = "ПЕТРОВА";

    public static readonly IProgress<string> NoProgress = new Progress<string>(_ => { });

    public static string KomplektRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "шаблони", "комплекти")))
            dir = dir.Parent;
        return dir is null
            ? throw new DirectoryNotFoundException("Не знайдено теку «шаблони/комплекти»")
            : Path.Combine(dir.FullName, "шаблони", "комплекти");
    }

    public static string SetFolder(string set) => Path.Combine(KomplektRoot(), set);

    public static string KomplektFile(string set, string file)
    {
        var path = Path.Combine(KomplektRoot(), set, file);
        Assert.True(File.Exists(path), $"Немає файлу шаблону: {path}");
        return path;
    }

    public static TemplateFolderImportService Importer(TestDb db) => new(
        db.Factory,
        new TemplateService(db.Factory, new FakeAuditLog(), new FakeCurrentUser()),
        new ExportTemplateService(db.Factory, new FakeAuditLog(), new FakeCurrentUser()),
        TestServices.Generation(db));

    public sealed record Seeded(int IntakeId, IReadOnlyDictionary<string, int> IdBySurname);

    /// <summary>Люди, курсовий офіцер, налаштування; начальник курсів - «полковник Іван ПЕТРЕНКО».</summary>
    public static Seeded SeedPeople(TestDb db, string outputFolder)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { Id = UserId, FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now });
        ctx.AppSettings.Add(new AppSettings { DefaultOutputFolder = outputFolder });
        ctx.OrganizationSettings.Add(new OrganizationSettings
        {
            UnitNumber = "А0000", City = "Харків",
            CommanderRank = "полковник", CommanderFullName = "Іван ПЕТРЕНКО",
            CommanderPosition = "Начальник курсів", HrOfficerFullName = "Петро КАДРОВИК",
            UnitFullName = "Харківський національний університет Повітряних Сил"
        });

        var intake = new Intake
        {
            Number = 30, DisplayNumber = "Набір №30", Status = IntakeStatus.Active,
            DateStart = new DateOnly(2026, 7, 15), DateEnd = new DateOnly(2026, 8, 30)
        };
        ctx.Intakes.Add(intake);
        var room = new Room { Building = "1", Number = "305", Capacity = 6 };
        ctx.Rooms.Add(room);
        ctx.SaveChanges();

        var people = new[]
        {
            (Last: "ШЕВЧЕНКО", First: "Андрій", Middle: "Петрович", Rank: "майор", Fit: "придатний", Gender: Gender.Male),
            (Last: "ГОНЧАР", First: "Юрій", Middle: "Іванович", Rank: "капітан", Fit: "придатний", Gender: Gender.Male),
            (Last: "БІЛИЙ", First: "Олег", Middle: "Миколайович", Rank: "старший лейтенант", Fit: "обмежено придатний", Gender: Gender.Male),
            (Last: "ПЕТРОВА", First: "Олена", Middle: "Іванівна", Rank: "лейтенант", Fit: "придатний", Gender: Gender.Female)
        };

        var ids = new Dictionary<string, int>();
        for (var i = 0; i < people.Length; i++)
        {
            var p = people[i];
            var person = TemplateFixtures.Person(i + 1, p.Last, p.First, p.Rank);
            person.MiddleName = p.Middle;
            person.IntakeId = intake.Id;
            person.RoomId = room.Id;
            person.FitnessCategory = p.Fit;
            person.Gender = p.Gender;
            person.TravelCertificateNumber = $"№ {100 + i}";
            person.TravelCertificateDate = new DateOnly(2026, 7, 13);
            person.FoodCertificate = $"ПА-{10 + i}";
            person.Weapons.Add(new Weapon { Name = "АКМ", SerialNumber = $"АА {1000 + i}" });
            ctx.Recipients.Add(person);
            ids[p.Last] = person.Id;
        }

        ctx.Recipients.Add(new Recipient
        {
            Id = CourseOfficerId, LastName = "КОВАЛЬЧУК", FirstName = "Василь", MiddleName = "Богданович",
            Rank = "капітан", Position = "курсовий офіцер", IsCourseOfficer = true, IntakeId = null
        });
        ctx.SaveChanges();

        return new Seeded(intake.Id, ids);
    }

    public static int ImportSet(TestDb db, string set)
    {
        var result = Importer(db).ImportFolder(SetFolder(set));
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(result.Failures);

        using var ctx = db.Factory.CreateDbContext();
        return ctx.GenerationPackages.Single(p => p.Name == set).Id;
    }

    public static ManualTagFormBuilder FormBuilder(TestDb db) => TestServices.ManualTagForm(
        db, TestServices.Staff(db, UserId), new FakeCurrentUser(), new FakeIntakeAccessor(), UserId);

    /// <summary>
    /// Значення ручних тегів так, як їх дає форма генерації: «Дата документа», «Дні стрільб», підписант
    /// за замовчуванням (начальник курсів); решта текстових полів - «[тег]».
    /// </summary>
    public static async Task<Dictionary<string, string>> FormValues(
        TestDb db, IReadOnlyList<string> tags, string shootingDays, DateTime? documentDate = null,
        Action<ManualTagFormViewModel>? adjust = null)
    {
        var form = await FormBuilder(db).BuildAsync(tags, "qa");

        foreach (var row in form.Rows)
        {
            if (row.Kind == ManualTagKind.Period) row.Value = shootingDays;
            else if (row.Kind == ManualTagKind.Text && string.IsNullOrWhiteSpace(row.Value))
                row.Value = "[" + ManualTagClassifier.Normalize(row.Tag) + "]";
        }

        form.SetDocumentDate(documentDate ?? new DateTime(2026, 8, 3));
        adjust?.Invoke(form);
        return form.GetValues();
    }

    public static RosterSelection Intake(int intakeId, FitnessFilter filter = FitnessFilter.All) => new(
        true, Array.Empty<int>(), filter, false,
        Array.Empty<RankCategory>(), Array.Empty<string>(), IntakeId: intakeId);

    public static string DocxText(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var main = doc.MainDocumentPart!;
        var parts = new List<string>();
        foreach (var paragraph in main.Document!.Body!.Descendants<Paragraph>())
            parts.Add(string.Concat(paragraph.Descendants<Text>().Select(t => t.Text)));
        parts.AddRange(main.HeaderParts.Select(h => string.Concat(h.Header!.Descendants<Text>().Select(t => t.Text))));
        parts.AddRange(main.FooterParts.Select(f => string.Concat(f.Footer!.Descendants<Text>().Select(t => t.Text))));
        return string.Join("\n", parts);
    }

    public static List<string> SheetCells(IXLWorksheet sheet)
        => sheet.RangeUsed()?.CellsUsed()
               .Select(c => c.DataType == XLDataType.DateTime ? c.GetDateTime().ToString("dd.MM.yyyy") : c.GetString())
               .ToList()
           ?? new List<string>();

    public static List<string> XlsxCells(string path)
    {
        using var workbook = new XLWorkbook(path);
        return workbook.Worksheets.SelectMany(SheetCells).ToList();
    }

    public static string SingleFile(string folder, string nameFragment, string extension)
        => Assert.Single(Directory.GetFiles(folder, "*" + extension, SearchOption.AllDirectories),
            f => f.Contains(nameFragment, StringComparison.Ordinal));

    public static string[] Files(string folder, string extension)
        => Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*" + extension, SearchOption.AllDirectories)
            : Array.Empty<string>();
}
