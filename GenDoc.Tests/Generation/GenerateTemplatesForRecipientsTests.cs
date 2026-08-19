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
            new[] { templateId }, peopleIds.Take(2).ToList(), _folder, new Dictionary<string, string>(), NoProgress);

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
            new[] { templateId }, peopleIds.Take(1).ToList(), _folder, new Dictionary<string, string>(), NoProgress);

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
        svc.GenerateTemplatesForRecipients(new[] { templateId }, peopleIds.Take(1).ToList(), _folder, new(), NoProgress);
        svc.GenerateTemplatesForRecipients(new[] { templateId }, peopleIds.Take(1).ToList(), _folder, new(), NoProgress);

        using var check = db.Factory.CreateDbContext();
        Assert.Equal(2, check.GeneratedDocuments.IgnoreQueryFilters().Count());
        Assert.Equal(2, check.GeneratedDocuments.Single(g => g.IsCurrent).Version);
    }
}
