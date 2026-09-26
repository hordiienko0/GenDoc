using System.Text.RegularExpressions;
using ClosedXML.Excel;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;
using static GenDoc.Tests.Qa.QaKomplekt;

namespace GenDoc.Tests.Qa;

/// <summary>
/// QA 2026-09-26, блок А: комплекти з «шаблони/комплекти» завантажуються «Завантажити теку»
/// і проганяються пакетом на синтетичний набір (A1, A2, A5, A7, A8).
/// </summary>
public class KomplektPackageQaTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gqa-{Guid.NewGuid():N}"[..14]);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private const string Days = "04.08.2026-06.08.2026, 10.08.2026";

    // ---------------------------------------------------------------------
    // A1. Завантаження теки: пакет на теку, групові в пакеті, персональні лише завантажені
    // ---------------------------------------------------------------------

    public static IEnumerable<object[]> Sets() => new[]
    {
        //            тека                      групових DOCX  відомостей  «аркуш на дату»  персональних
        new object[] { "Стрільби",                 9,           3,          3,               0 },
        new object[] { "Котлове забезпечення",     2,           0,          0,               2 },
        new object[] { "Відрядження",              1,           0,          0,               0 },
        new object[] { "Відпустки та лікування",   0,           0,          0,               3 },
        new object[] { "Стройовий облік",          2,           1,          0,               0 },
        new object[] { "Інше",                     2,           0,          0,               0 }
    };

    [Theory]
    [MemberData(nameof(Sets))]
    public void A1_ImportFolder_MakesPackageOfGroupTemplates_PerRecipientOnlyUploaded(
        string set, int groupDocx, int ledgers, int perDate, int perRecipient)
    {
        using var db = new TestDb();

        var result = Importer(db).ImportFolder(SetFolder(set));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(result.Failures);
        var package = Assert.Single(result.Packages);
        Assert.Equal(groupDocx, package.TemplateCount);
        Assert.Equal(ledgers, package.ExportTemplateCount);
        Assert.Equal(perRecipient, package.PerRecipientCount);

        using var ctx = db.NewContext();
        Assert.Equal(groupDocx + perRecipient, ctx.Templates.Count());
        Assert.Equal(perRecipient, ctx.Templates.Count(t => t.Kind == TemplateKind.PerRecipient));
        Assert.Equal(ledgers, ctx.ExportTemplates.Count());
        Assert.Equal(perDate, ctx.ExportTemplates.Count(t => t.RepeatSheetPerDate));

        if (groupDocx + ledgers == 0)
        {
            // Лише персональні шаблони: пакет не створюється, шаблони видаються людині окремо.
            Assert.Empty(ctx.GenerationPackages);
            return;
        }

        var saved = Assert.Single(ctx.GenerationPackages);
        Assert.Equal(set, saved.Name);
        var inPackage = ctx.GenerationPackageTemplates.Where(pt => pt.GenerationPackageId == saved.Id)
            .Select(pt => pt.Template!.Kind).ToList();
        Assert.Equal(groupDocx, inPackage.Count);
        Assert.All(inPackage, k => Assert.Equal(TemplateKind.Group, k));
    }

    [Fact]
    public void A1_ImportSameFolderTwice_ReusesTemplates_AndNamesSecondPackageUniquely()
    {
        using var db = new TestDb();
        Importer(db).ImportFolder(SetFolder("Стрільби"));

        var second = Importer(db).ImportFolder(SetFolder("Стрільби"));

        Assert.True(second.Success, second.ErrorMessage);
        var package = Assert.Single(second.Packages);
        Assert.Equal("Стрільби (2)", package.Name);
        Assert.Equal(12, package.ReusedCount);

        using var ctx = db.NewContext();
        Assert.Equal(9, ctx.Templates.Count());
        Assert.Equal(3, ctx.ExportTemplates.Count());
    }

    // ---------------------------------------------------------------------
    // A2. Прогін кожного пакета на 4 особи
    // ---------------------------------------------------------------------

    public static IEnumerable<object[]> PackageSets() => Sets()
        .Where(s => (int)s[1] + (int)s[2] > 0)
        .Select(s => new[] { s[0] });

    [Theory]
    [MemberData(nameof(PackageSets))]
    public async Task A2_RunPackage_ForFourPeople_NoErrors_NoTags_EveryoneListed(string set)
    {
        using var db = new TestDb();
        var seeded = SeedPeople(db, _folder);
        var packageId = ImportSet(db, set);
        var generation = TestServices.Generation(db);

        var values = await FormValues(db, generation.GetManualTags(packageId), Days);
        var result = generation.RunPackage(packageId, _folder, values, regenerateExisting: true,
            Intake(seeded.IntakeId), NoProgress, courseOfficerId: CourseOfficerId);

        var problems = result.Issues!
            .Where(i => i.IsError || i.Message.Contains("не заповнено", StringComparison.Ordinal))
            .Select(i => $"{i.Phase} · {i.TemplateName} · {i.Message}").ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));

        using var ctx = db.NewContext();
        var package = ctx.GenerationPackages.Single(p => p.Id == packageId);
        var groupTemplates = ctx.GenerationPackageTemplates.Where(pt => pt.GenerationPackageId == packageId)
            .Select(pt => pt.Template!).ToList();
        Assert.Equal(groupTemplates.Count, result.DocxGroupGenerated);
        Assert.Equal(ctx.GenerationPackageExportTemplates.Count(l => l.GenerationPackageId == packageId), result.GroupGenerated);

        foreach (var file in Files(_folder, ".docx"))
            Assert.False(DocxText(file).Contains("{{", StringComparison.Ordinal), $"Лишились теги: {file}");

        foreach (var file in Files(_folder, ".xlsx"))
        {
            var cells = XlsxCells(file);
            Assert.DoesNotContain(cells, c => c.Contains("{{", StringComparison.Ordinal));
            foreach (var surname in Surnames)
                Assert.Contains(cells, c => c.Contains(surname, StringComparison.Ordinal));
        }

        // Групові DOCX з поіменним блоком перелічують усіх, у решти люди лише в {{кількість_осіб}}.
        foreach (var template in groupTemplates)
        {
            var listsPeople = ctx.TemplateFieldMappings.Any(m => m.TemplateId == template.Id && m.IsInsideRepeatingBlock);
            if (!listsPeople) continue;

            var text = DocxText(SingleFile(_folder, TemplateNaming.Clean(template.Name), ".docx"));
            foreach (var surname in Surnames)
                Assert.Contains(surname, text);
        }
    }

    [Fact]
    public async Task A2_PerRecipientTemplates_FromFolders_OneFilePerPerson_NoTags()
    {
        using var db = new TestDb();
        var seeded = SeedPeople(db, _folder);
        ImportSet(db, "Котлове забезпечення");
        Importer(db).ImportFolder(SetFolder("Відпустки та лікування"));

        List<int> templateIds;
        using (var ctx = db.NewContext())
            templateIds = ctx.Templates.Where(t => t.Kind == TemplateKind.PerRecipient).Select(t => t.Id).ToList();
        Assert.Equal(5, templateIds.Count);

        var generation = TestServices.Generation(db);
        var values = await FormValues(db, generation.GetManualTagsForTemplates(templateIds, Array.Empty<int>()), Days);
        var result = generation.GenerateTemplatesForRecipients(
            templateIds, Array.Empty<int>(), seeded.IdBySurname.Values.ToList(), _folder, values, NoProgress);

        Assert.Equal(0, result.Errors);
        Assert.Equal(5 * 4, result.Generated);
        Assert.DoesNotContain(result.Issues!, i => i.Message.Contains("не заповнено", StringComparison.Ordinal));
        var files = Files(_folder, ".docx");
        Assert.Equal(20, files.Length);
        Assert.All(files, f => Assert.DoesNotContain("{{", DocxText(f)));
    }

    // ---------------------------------------------------------------------
    // A5. Фільтр придатності
    // ---------------------------------------------------------------------

    [Fact]
    public async Task A5_LedgerFitnessFilter_RegularOnlyAndLimitedOnly_TakeOnlyTheirPeople()
    {
        using var db = new TestDb();
        var seeded = SeedPeople(db, _folder);
        var packageId = ImportSet(db, "Стрільби");
        var generation = TestServices.Generation(db);

        var links = generation.GetPackageExportTemplates(packageId);
        var dopusk = links.Single(l => l.Name.Contains("допуску", StringComparison.Ordinal));
        var zalik = links.Single(l => l.Name.Contains("заліку", StringComparison.Ordinal));
        generation.SaveExportTemplates(packageId, links.Select(l => ((int?)l.LinkId, l.ExportTemplateId, l.SortOrder,
            l.LinkId == dopusk.LinkId ? FitnessFilter.RegularOnly
            : l.LinkId == zalik.LinkId ? FitnessFilter.LimitedOnly
            : FitnessFilter.All)).ToList());

        var values = await FormValues(db, generation.GetManualTags(packageId), "04.08.2026");
        var result = generation.RunPackage(packageId, _folder, values, regenerateExisting: true,
            Intake(seeded.IntakeId), NoProgress, courseOfficerId: CourseOfficerId);
        Assert.Equal(0, result.GroupErrors);

        var dopuskCells = XlsxCells(SingleFile(_folder, TemplateNaming.Clean(dopusk.Name), ".xlsx"));
        foreach (var surname in Surnames.Where(s => s != LimitedSurname))
            Assert.Contains(dopuskCells, c => c.Contains(surname, StringComparison.Ordinal));
        Assert.DoesNotContain(dopuskCells, c => c.Contains(LimitedSurname, StringComparison.Ordinal));

        var zalikCells = XlsxCells(SingleFile(_folder, TemplateNaming.Clean(zalik.Name), ".xlsx"));
        Assert.Contains(zalikCells, c => c.Contains(LimitedSurname, StringComparison.Ordinal));
        foreach (var surname in Surnames.Where(s => s != LimitedSurname))
            Assert.DoesNotContain(zalikCells, c => c.Contains(surname, StringComparison.Ordinal));
    }

    private static int ShootersCount(string text)
    {
        var match = Regex.Match(text, @"стріляють:\s*(\d+)");
        Assert.True(match.Success, "У заявці не знайдено «Кількість осіб, що стріляють: N»:\n" + text);
        return int.Parse(match.Groups[1].Value);
    }

    [Fact]
    public async Task A5_ShootingPackage_FitAndLimitedVariants_CountOnlyTheirOwnPeople()
    {
        using var db = new TestDb();
        var seeded = SeedPeople(db, _folder);
        var packageId = ImportSet(db, "Стрільби");
        var generation = TestServices.Generation(db);

        var values = await FormValues(db, generation.GetManualTags(packageId), Days);
        var result = generation.RunPackage(packageId, _folder, values, regenerateExisting: true,
            Intake(seeded.IntakeId), NoProgress, courseOfficerId: CourseOfficerId);
        Assert.Equal(0, result.DocxGroupErrors);

        var fit = DocxText(SingleFile(_folder, "Заявка на боєприпаси (придатні)", ".docx"));
        var limited = DocxText(SingleFile(_folder, "Заявка на боєприпаси (обмежено придатні)", ".docx"));

        // Було (виправлено 2026-09-26): групові DOCX у пакеті не мають фільтра придатності - і «(придатні)», і «(обмежено придатні)»
        // рахують увесь набір (4 замість 3 і 1); так само накази «на N учасників стрільби».
        Assert.Equal(3, ShootersCount(fit));
        Assert.Equal(1, ShootersCount(limited));
    }

    [Fact]
    public async Task A5_RosterFitnessFilter_LimitedOnly_ReachesGroupDocxAndLedgers()
    {
        using var db = new TestDb();
        var seeded = SeedPeople(db, _folder);
        var packageId = ImportSet(db, "Стрільби");
        var generation = TestServices.Generation(db);

        var values = await FormValues(db, generation.GetManualTags(packageId), "04.08.2026");
        var result = generation.RunPackage(packageId, _folder, values, regenerateExisting: true,
            Intake(seeded.IntakeId, FitnessFilter.LimitedOnly), NoProgress, courseOfficerId: CourseOfficerId);

        Assert.Equal(0, result.GroupErrors + result.DocxGroupErrors);
        Assert.Equal(1, ShootersCount(DocxText(SingleFile(_folder, "Заявка на боєприпаси (обмежено придатні)", ".docx"))));
        foreach (var file in Files(_folder, ".xlsx"))
        {
            var cells = XlsxCells(file);
            Assert.Contains(cells, c => c.Contains(LimitedSurname, StringComparison.Ordinal));
            Assert.DoesNotContain(cells, c => c.Contains("ШЕВЧЕНКО", StringComparison.Ordinal));
        }
    }

    // ---------------------------------------------------------------------
    // A7. Повторна генерація і застарілість
    // ---------------------------------------------------------------------

    private async Task<(TestDb Db, int PackageId, int IntakeId)> ShootingRun(string days, DateTime documentDate)
    {
        var db = new TestDb();
        var seeded = SeedPeople(db, _folder);
        var packageId = ImportSet(db, "Стрільби");
        await Rerun(db, packageId, seeded.IntakeId, days, documentDate, regenerate: true);
        return (db, packageId, seeded.IntakeId);
    }

    private async Task<RunResult> Rerun(TestDb db, int packageId, int intakeId, string days, DateTime documentDate, bool regenerate = false)
    {
        var generation = TestServices.Generation(db);
        var values = await FormValues(db, generation.GetManualTags(packageId), days, documentDate);
        return generation.RunPackage(packageId, _folder, values, regenerateExisting: regenerate,
            Intake(intakeId), NoProgress, courseOfficerId: CourseOfficerId);
    }

    [Fact]
    public async Task A7_SameValues_SecondRunSkipsEverything()
    {
        var (db, packageId, intakeId) = await ShootingRun(Days, new DateTime(2026, 8, 3));
        using var _ = db;

        var second = await Rerun(db, packageId, intakeId, Days, new DateTime(2026, 8, 3));

        Assert.Equal(0, second.GroupGenerated + second.DocxGroupGenerated);
        Assert.Equal(3, second.GroupSkipped);
        Assert.Equal(9, second.DocxGroupSkipped);
    }

    [Fact]
    public async Task A7_ChangedShootingDays_NewVersionOfLedgersAndOrders_OthersSkipped()
    {
        var (db, packageId, intakeId) = await ShootingRun(Days, new DateTime(2026, 8, 3));
        using var _ = db;

        var second = await Rerun(db, packageId, intakeId, "11.08.2026-12.08.2026", new DateTime(2026, 8, 3));

        Assert.Equal(0, second.GroupErrors + second.DocxGroupErrors);
        Assert.Equal(3, second.GroupGenerated);

        using var ctx = db.NewContext();
        var currentLedgers = ctx.GeneratedGroupDocuments.Where(g => g.ExportTemplateId != null && g.IsCurrent).ToList();
        Assert.Equal(3, currentLedgers.Count);
        Assert.All(currentLedgers, g => Assert.Equal(2, g.Version));
        foreach (var ledger in currentLedgers)
        {
            using var workbook = new XLWorkbook(Path.Combine(_folder, ledger.FileName));
            Assert.Equal(new[] { "11.08.2026", "12.08.2026" }, workbook.Worksheets.Select(w => w.Name).ToArray());
        }

        // Документи з {{дати_стрільб}} - нова версія; «Стройова записка» ({{дата}}) дні не бачить - пропуск.
        var names = ctx.GeneratedGroupDocuments.Where(g => g.TemplateId != null && g.Version == 2)
            .Select(g => g.Template!.Name).ToList();
        Assert.Contains(names, n => n.Contains("Заявка на боєприпаси (придатні)", StringComparison.Ordinal));
        Assert.Contains(names, n => n.Contains("Витяг з наказу на стрільби (придатні)", StringComparison.Ordinal));
        Assert.DoesNotContain(names, n => n.Contains("Стройова записка", StringComparison.Ordinal));

        var order = ctx.GeneratedGroupDocuments.Single(g => g.IsCurrent && g.Template!.Name.Contains("Витяг з наказу на стрільби (придатні)"));
        Assert.Contains("11, 12 серпня 2026 року", DocxText(Path.Combine(_folder, order.FileName)));
    }

    [Fact]
    public async Task A7_OnlyDocumentDateChanged_SheetPerDateLedgerIsNotRegenerated()
    {
        var (db, packageId, intakeId) = await ShootingRun(Days, new DateTime(2026, 8, 3));
        using var _ = db;

        var second = await Rerun(db, packageId, intakeId, Days, new DateTime(2026, 8, 4));

        using var ctx = db.NewContext();
        var dopusk = ctx.GeneratedGroupDocuments
            .Where(g => g.ExportTemplate!.Name.Contains("допуску"))
            .OrderByDescending(g => g.Version).First();

        // Було (виправлено 2026-09-26): у відомостях «аркуш на кожну дату» {{дата_аркуша}} береться з дня аркуша, але відбиток
        // (SheetManualFingerprint) рахує його значення з «Дати документа» - нова дата документа дає
        // нову версію з тим самим вмістом, а стара позначається застарілою.
        Assert.Equal(1, dopusk.Version);
        Assert.True(second.GroupSkipped >= 1, "Відомість допуску мала бути пропущена як актуальна");
    }

    // ---------------------------------------------------------------------
    // A8. Підписант у документі
    // ---------------------------------------------------------------------

    [Fact]
    public async Task A8_DefaultSignerIsHeadOfCourses_ChosenCourseOfficerReplacesHim()
    {
        using var db = new TestDb();
        var seeded = SeedPeople(db, _folder);
        var packageId = ImportSet(db, "Відрядження");
        var generation = TestServices.Generation(db);
        var tags = generation.GetManualTags(packageId);

        var byDefault = await FormValues(db, tags, Days);
        Assert.Equal("полковник", byDefault["{{звання_підписанта}}"]);
        Assert.Equal("Іван ПЕТРЕНКО", byDefault["{{піб_підписанта}}"]);

        generation.RunPackage(packageId, _folder, byDefault, regenerateExisting: true, Intake(seeded.IntakeId), NoProgress);
        var headText = DocxText(Assert.Single(Files(_folder, ".docx")));
        Assert.Contains("полковник", headText);
        Assert.Contains("Іван ПЕТРЕНКО", headText);

        var chosen = await FormValues(db, tags, Days, adjust: form =>
            form.Signer!.Selected = form.Signer.Options.Single(o => o.RecipientId == CourseOfficerId));
        Assert.Equal("капітан", chosen["{{звання_підписанта}}"]);
        Assert.Equal("Василь КОВАЛЬЧУК", chosen["{{піб_підписанта}}"]);

        var second = generation.RunPackage(packageId, _folder, chosen, regenerateExisting: false, Intake(seeded.IntakeId), NoProgress);
        Assert.Equal(1, second.DocxGroupGenerated);

        using var ctx = db.NewContext();
        var current = ctx.GeneratedGroupDocuments.Single(g => g.IsCurrent);
        var officerText = DocxText(Path.Combine(_folder, current.FileName));
        Assert.Contains("Василь КОВАЛЬЧУК", officerText);
        Assert.DoesNotContain("Іван ПЕТРЕНКО", officerText);
    }

    [Fact]
    public async Task A8_CourseOfficerSignatureInLedger_UsesTheChosenOfficer()
    {
        using var db = new TestDb();
        var seeded = SeedPeople(db, _folder);
        var packageId = ImportSet(db, "Стрільби");
        var generation = TestServices.Generation(db);

        var values = await FormValues(db, generation.GetManualTags(packageId), "04.08.2026");
        generation.RunPackage(packageId, _folder, values, regenerateExisting: true,
            Intake(seeded.IntakeId), NoProgress, courseOfficerId: CourseOfficerId);

        var cells = XlsxCells(SingleFile(_folder, "Відомість складання заліку", ".xlsx"));
        Assert.Contains(cells, c => c.Contains("КОВАЛЬЧУК", StringComparison.Ordinal));
        // Підписант відомості - начальник курсів за замовчуванням.
        Assert.Contains(cells, c => c.Contains("Іван ПЕТРЕНКО", StringComparison.Ordinal));
    }
}
