using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Tests.Generation;

// 2.2: документ на 1-3 людей з будь-якого шаблону - без пакета. Той самий конвеєр,
// що й RunPackage (розкладка по теках, версії, архів), але запуск без GenerationPackageId.
public class GenerateTemplatesForRecipientsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-adhoc-{Guid.NewGuid():N}");
    public void Dispose() { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }
    private static readonly IProgress<string> NoProgress = new Progress<string>(_ => { });

    private static (int TemplateId, List<int> PeopleIds, int IntakeId) Seed(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
        ctx.OrganizationSettings.Add(new OrganizationSettings
        {
            UnitNumber = "А1234", City = "Львів", CommanderRank = "полковник", CommanderFullName = "І. ПЕТРЕНКО",
            CommanderPosition = "начальник", HrOfficerFullName = "К. КАДРОВ", UnitFullName = "Коледж"
        });
        var intake = new Intake { Number = 15, DisplayNumber = "Набір №15" };
        ctx.Intakes.Add(intake);
        var template = new Template
        {
            Name = "Шаблон_Рапорт_котлове_ІНДИВІДУАЛЬНИЙ", OriginalFileName = "rapport.docx",
            Content = TemplateFixtures.Bytes(TemplateFixtures.RaportIndividualDocx),
            UploadedAt = DateTime.Now, Kind = TemplateKind.PerRecipient
        };
        ctx.Templates.Add(template);
        var people = TemplateFixtures.Roster(3);
        ctx.Recipients.AddRange(people);
        ctx.SaveChanges();
        foreach (var p in people) p.IntakeId = intake.Id;
        foreach (var tag in new[] { "{{звання_зв}}", "{{піб_зв}}", "{{прибув}}", "{{таким}}" })
        {
            var (sourceType, fieldName) = Services.Templates.PlaceholderTagMaps.Classify(tag);
            ctx.TemplateFieldMappings.Add(new TemplateFieldMapping
            { TemplateId = template.Id, PlaceholderTag = tag, SourceType = sourceType, FieldName = fieldName, IsInsideRepeatingBlock = false });
        }
        ctx.SaveChanges();
        return (template.Id, people.Select(p => p.Id).ToList(), intake.Id);
    }

    [Fact]
    public void GeneratesOneFilePerPersonAndRecordsRunWithoutPackage()
    {
        using var db = new TestDb();
        var (templateId, peopleIds, intakeId) = Seed(db);

        var result = TestServices.Generation(db).GenerateTemplatesForRecipients(
            new[] { templateId }, Array.Empty<int>(), peopleIds.Take(2).ToList(), _folder, new Dictionary<string, string>(), NoProgress);

        Assert.Equal(2, result.Generated);
        Assert.Equal(0, result.Errors);
        Assert.Equal(2, Directory.GetFiles(_folder, "*.docx", SearchOption.AllDirectories).Length);

        using var check = db.Factory.CreateDbContext();
        var run = check.GenerationPackageRuns.Single();
        Assert.Null(run.GenerationPackageId);
        Assert.Equal(intakeId, run.IntakeId);
        Assert.Equal(run.Id, result.RunId);
        Assert.Equal(2, check.GeneratedDocuments.Count(g => g.RunId == run.Id));
    }

    [Fact]
    public async Task AdHocRun_IsVisibleInArchiveRunsAsSelective()
    {
        using var db = new TestDb();
        var (templateId, peopleIds, intakeId) = Seed(db);
        TestServices.Generation(db).GenerateTemplatesForRecipients(
            new[] { templateId }, Array.Empty<int>(), peopleIds.Take(1).ToList(), _folder, new Dictionary<string, string>(), NoProgress);

        var runs = await TestServices.Archive(db).GetRunsAsync(intakeId, null);

        var run = Assert.Single(runs);
        Assert.Equal("Вибірково", run.PackageName);
    }

    [Fact]
    public void SecondCall_BumpsVersion()
    {
        using var db = new TestDb();
        var (templateId, peopleIds, _) = Seed(db);
        var svc = TestServices.Generation(db);
        svc.GenerateTemplatesForRecipients(new[] { templateId }, Array.Empty<int>(), peopleIds.Take(1).ToList(), _folder, new(), NoProgress);
        svc.GenerateTemplatesForRecipients(new[] { templateId }, Array.Empty<int>(), peopleIds.Take(1).ToList(), _folder, new(), NoProgress);

        using var check = db.Factory.CreateDbContext();
        Assert.Equal(2, check.GeneratedDocuments.IgnoreQueryFilters().Count());
        Assert.Equal(2, check.GeneratedDocuments.Single(g => g.IsCurrent).Version);
    }

    // Excel-відомість для курсового - такий самий шаблон: на обраних людей формується
    // один аркуш (рядок на особу), без пакета, з тим самим записом прогону.
    [Fact]
    public void ExcelSheet_ForSelectedPeople_GeneratesOneWorkbookWithRowPerPerson()
    {
        using var db = new TestDb();
        var (_, peopleIds, _) = Seed(db);
        int exportTemplateId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var (row, mappings) = XlsxTemplateScan.ForGeneration(TemplateFixtures.RozdavalnaXlsx);
            var export = new ExportTemplate
            {
                Name = "Роздавальна відомість", OriginalFileName = "rozd.xlsx",
                Content = TemplateFixtures.Bytes(TemplateFixtures.RozdavalnaXlsx),
                UploadedAt = DateTime.Now, TemplateRowIndex = row, UsesPlaceholders = true
            };
            foreach (var m in mappings) export.ColumnMappings.Add(m);
            ctx.ExportTemplates.Add(export);
            ctx.SaveChanges();
            exportTemplateId = export.Id;
        }

        var result = TestServices.Generation(db).GenerateTemplatesForRecipients(
            Array.Empty<int>(), new[] { exportTemplateId }, peopleIds.Take(2).ToList(), _folder,
            new Dictionary<string, string> { ["{{дата_аркуша}}"] = "19.08.2026" }, NoProgress);

        Assert.Equal(1, result.GroupGenerated);
        Assert.Equal(0, result.GroupErrors);
        var file = Assert.Single(Directory.GetFiles(_folder, "*.xlsx", SearchOption.AllDirectories));
        using var workbook = new ClosedXML.Excel.XLWorkbook(file);
        var text = string.Join(" ", workbook.Worksheets.First().RangeUsed()!.CellsUsed().Select(c => c.GetString()));
        Assert.Contains("ПРІЗВИЩЕ01", text);
        Assert.Contains("ПРІЗВИЩЕ02", text);
        Assert.DoesNotContain("ПРІЗВИЩЕ03", text);

        using var check = db.Factory.CreateDbContext();
        Assert.Equal(1, check.GeneratedGroupDocuments.Count(g => g.RunId == result.RunId));
        // v25: склад відомості записано - рівно ті двоє, кого обрали.
        var participants = check.GeneratedGroupDocumentRecipients.Select(p => p.RecipientId).OrderBy(i => i).ToList();
        Assert.Equal(peopleIds.Take(2).OrderBy(i => i), participants);
    }
}
