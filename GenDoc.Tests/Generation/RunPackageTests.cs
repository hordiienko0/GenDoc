using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Tests.Generation;

public class RunPackageTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-run-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static readonly IProgress<string> NoProgress = new Progress<string>(_ => { });

    private static (int PackageId, List<int> RecipientIds) SeedPackage(TestDb db, List<Recipient> people)
    {
        using var ctx = db.Factory.CreateDbContext();

        ctx.Users.Add(new UserProfile { FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now });
        ctx.OrganizationSettings.Add(new OrganizationSettings
        {
            UnitNumber = "А1234", City = "Львів",
            CommanderRank = "полковник", CommanderFullName = "І. ПЕТРЕНКО",
            CommanderPosition = "начальник", HrOfficerFullName = "К. КАДРОВ",
            UnitFullName = "Військовий коледж"
        });

        var template = new Template
        {
            Name = "Шаблон_Рапорт_котлове_ІНДИВІДУАЛЬНИЙ",
            OriginalFileName = "rapport.docx",
            Content = TemplateFixtures.Bytes(TemplateFixtures.RaportIndividualDocx),
            UploadedAt = DateTime.Now,
            Kind = TemplateKind.PerRecipient
        };
        ctx.Templates.Add(template);
        ctx.Recipients.AddRange(people);
        ctx.SaveChanges();

        foreach (var tag in new[]
                 {
                     "{{звання_зв}}", "{{піб_зв}}", "{{прибув}}", "{{таким}}",
                     "{{номер_посвідчення}}", "{{прод_атестат}}", "{{дата_посвідчення}}"
                 })
        {
            var (sourceType, fieldName) = GenDoc.Services.Templates.PlaceholderTagMaps.Classify(tag);
            ctx.TemplateFieldMappings.Add(new TemplateFieldMapping
            {
                TemplateId = template.Id,
                PlaceholderTag = tag,
                SourceType = sourceType,
                FieldName = fieldName,
                IsInsideRepeatingBlock = false
            });
        }

        var package = new GenerationPackage { Name = "Тестовий пакет" };
        package.Templates.Add(new GenerationPackageTemplate { TemplateId = template.Id, SortOrder = 0 });
        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();

        return (package.Id, people.Select(p => p.Id).ToList());
    }

    private RunResult Run(TestDb db, int packageId, bool regenerate = false, RosterSelection? selection = null)
        => TestServices.Generation(db).RunPackage(
            packageId, _folder, new Dictionary<string, string>(), regenerate,
            selection ?? RosterSelection.Everyone, NoProgress);

    [Fact]
    public void RunPackage_GeneratesOneFilePerPerson()
    {
        using var db = new TestDb();
        var (packageId, _) = SeedPackage(db, TemplateFixtures.Roster(3));

        var result = Run(db, packageId);

        Assert.Equal(3, result.Generated);
        Assert.Equal(0, result.Errors);
        Assert.Equal(3, Directory.GetFiles(_folder, "*.docx", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void RunPackage_FileNameDropsTemplatePrefixAndUnderscores()
    {
        using var db = new TestDb();
        var (packageId, _) = SeedPackage(db, new List<Recipient>
        {
            TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас")
        });

        Run(db, packageId);

        var file = Directory.GetFiles(_folder, "*.docx", SearchOption.AllDirectories).Single();
        var templateFolder = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(file)));

        Assert.Equal("Рапорт котлове ІНДИВІДУАЛЬНИЙ", templateFolder);
        Assert.StartsWith("ШЕВЧЕНКО Тарас", Path.GetFileName(file));
    }

    [Fact]
    public void RunPackage_PutsDocumentsIntoIntakeAndTemplateFolders()
    {
        using var db = new TestDb();
        var (packageId, _) = SeedPackage(db, new List<Recipient>
        {
            TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас")
        });

        Run(db, packageId);

        var file = Directory.GetFiles(_folder, "*.docx", SearchOption.AllDirectories).Single();
        var relative = Path.GetRelativePath(_folder, file);
        var parts = relative.Split(Path.DirectorySeparatorChar);

        Assert.Equal(4, parts.Length);
        Assert.Equal("Рапорт котлове ІНДИВІДУАЛЬНИЙ", parts[1]);
        Assert.StartsWith(DateTime.Now.ToString("yyyy-MM-dd"), parts[2]);
        Assert.EndsWith(".docx", parts[3]);
    }

    [Fact]
    public void RunPackage_TwoPeopleWithIdenticalNames_GetDistinctFileNames()
    {
        using var db = new TestDb();
        var (packageId, _) = SeedPackage(db, new List<Recipient>
        {
            TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас"),
            TemplateFixtures.Person(2, "ШЕВЧЕНКО", "Тарас")
        });

        var result = Run(db, packageId);

        Assert.Equal(2, result.Generated);
        Assert.Equal(2, Directory.GetFiles(_folder, "*.docx", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void RunPackage_SecondRun_SkipsWhenFilesStillPresent()
    {
        using var db = new TestDb();
        var (packageId, _) = SeedPackage(db, TemplateFixtures.Roster(2));

        Run(db, packageId);
        var second = Run(db, packageId);

        Assert.Equal(0, second.Generated);
        Assert.Equal(2, second.Skipped);
    }

    [Fact]
    public void RunPackage_SecondRun_RegeneratesWhenFilesWereRemovedFromFolder()
    {
        using var db = new TestDb();
        var (packageId, _) = SeedPackage(db, TemplateFixtures.Roster(2));

        Run(db, packageId);
        foreach (var file in Directory.GetFiles(_folder, "*.docx", SearchOption.AllDirectories)) File.Delete(file);

        var second = Run(db, packageId);

        Assert.Equal(2, second.Generated);
        Assert.Equal(0, second.Skipped);
    }

    [Fact]
    public void RunPackage_RegenerateExisting_BumpsVersionAndKeepsOneCurrent()
    {
        using var db = new TestDb();
        var (packageId, recipientIds) = SeedPackage(db, TemplateFixtures.Roster(1));

        Run(db, packageId);
        Run(db, packageId, regenerate: true);

        using var ctx = db.Factory.CreateDbContext();
        var docs = ctx.GeneratedDocuments
            .Where(g => g.RecipientId == recipientIds[0])
            .OrderBy(g => g.Version).ToList();

        Assert.Equal(new[] { 1, 2 }, docs.Select(d => d.Version).ToArray());
        Assert.Single(docs.Where(d => d.IsCurrent));
        Assert.Equal(2, docs.Single(d => d.IsCurrent).Version);
    }

    [Fact]
    public void RunPackage_SelectedRecipientsOnly_IgnoresTheRest()
    {
        using var db = new TestDb();
        var (packageId, recipientIds) = SeedPackage(db, TemplateFixtures.Roster(4));

        var result = Run(db, packageId, selection: new RosterSelection(
            AllRecipients: false,
            RecipientIds: new[] { recipientIds[0], recipientIds[2] },
            FitnessFilter: FitnessFilter.All,
            PermanentStaffOnly: false,
            RankCategories: Array.Empty<RankCategory>(),
            Ranks: Array.Empty<string>()));

        Assert.Equal(2, result.Generated);
    }

    [Fact]
    public void RunPackage_RankFilter_NarrowsRosterByExactRank()
    {
        using var db = new TestDb();
        var (packageId, _) = SeedPackage(db, new List<Recipient>
        {
            TemplateFixtures.Person(1, "ПЕРШИЙ", "Іван", "майор"),
            TemplateFixtures.Person(2, "ДРУГИЙ", "Петро", "капітан"),
            TemplateFixtures.Person(3, "ТРЕТІЙ", "Сидір", "майор")
        });

        var result = Run(db, packageId, selection: new RosterSelection(
            AllRecipients: true,
            RecipientIds: Array.Empty<int>(),
            FitnessFilter: FitnessFilter.All,
            PermanentStaffOnly: false,
            RankCategories: Array.Empty<RankCategory>(),
            Ranks: new[] { "майор" }));

        Assert.Equal(2, result.Generated);
    }

    [Fact]
    public void RunPackage_PermanentStaffOnly_ExcludesIntakeMembers()
    {
        using var db = new TestDb();
        var people = TemplateFixtures.Roster(3);
        var (packageId, recipientIds) = SeedPackage(db, people);

        using (var ctx = db.Factory.CreateDbContext())
        {
            var intake = new Intake { Number = 29, Status = IntakeStatus.Active };
            ctx.Intakes.Add(intake);
            ctx.SaveChanges();

            var member = ctx.Recipients.First(r => r.Id == recipientIds[0]);
            member.IntakeId = intake.Id;
            ctx.SaveChanges();
        }

        var result = Run(db, packageId, selection: new RosterSelection(
            AllRecipients: true,
            RecipientIds: Array.Empty<int>(),
            FitnessFilter: FitnessFilter.All,
            PermanentStaffOnly: true,
            RankCategories: Array.Empty<RankCategory>(),
            Ranks: Array.Empty<string>()));

        Assert.Equal(2, result.Generated);
    }

    [Fact]
    public void RunPackage_RecordsRunRowWithPackageLink()
    {
        using var db = new TestDb();
        var (packageId, _) = SeedPackage(db, TemplateFixtures.Roster(2));

        Run(db, packageId);

        using var ctx = db.Factory.CreateDbContext();
        var run = Assert.Single(ctx.GenerationPackageRuns.ToList());
        Assert.Equal(packageId, run.GenerationPackageId);
    }

    [Fact]
    public void RunPackage_RecordsIntakeOfRoster()
    {
        using var db = new TestDb();
        int intakeId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var intake = new Intake { Number = 15, DisplayNumber = "Набір №15" };
            ctx.Intakes.Add(intake);
            ctx.SaveChanges();
            intakeId = intake.Id;
        }
        var people = TemplateFixtures.Roster(2);
        foreach (var p in people) p.IntakeId = intakeId;
        var (packageId, _) = SeedPackage(db, people);

        Run(db, packageId);

        using var check = db.Factory.CreateDbContext();
        var run = check.GenerationPackageRuns.Single();
        Assert.Equal(intakeId, run.IntakeId);
    }

    [Fact]
    public void RunPackage_PermanentStaffOnly_RecordsNullIntake()
    {
        using var db = new TestDb();
        var (packageId, _) = SeedPackage(db, TemplateFixtures.Roster(2));

        Run(db, packageId, selection: RosterSelection.Everyone with { PermanentStaffOnly = true });

        using var check = db.Factory.CreateDbContext();
        Assert.Null(check.GenerationPackageRuns.Single().IntakeId);
    }

    private static void AddSheetToPackage(TestDb db, int packageId)
    {
        using var ctx = db.Factory.CreateDbContext();
        var sheet = new ExportTemplate
        {
            Name = "Допуск Додаток 5",
            OriginalFileName = "dopusk.xlsx",
            Content = TemplateFixtures.Bytes(TemplateFixtures.DopuskXlsx),
            UploadedAt = DateTime.Now,
            UsesPlaceholders = true
        };
        ctx.ExportTemplates.Add(sheet);
        ctx.SaveChanges();
        ctx.GenerationPackageExportTemplates.Add(new GenerationPackageExportTemplate
        {
            GenerationPackageId = packageId, ExportTemplateId = sheet.Id, SortOrder = 0, FitnessFilter = FitnessFilter.All
        });
        ctx.SaveChanges();
    }

    [Fact]
    public void RunPackage_GroupSheetRecordsIntakeOfRun()
    {
        using var db = new TestDb();
        int intakeId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var intake = new Intake { Number = 15, DisplayNumber = "Набір №15" };
            ctx.Intakes.Add(intake);
            ctx.SaveChanges();
            intakeId = intake.Id;
        }
        var people = TemplateFixtures.Roster(2);
        foreach (var p in people) p.IntakeId = intakeId;
        var (packageId, _) = SeedPackage(db, people);
        AddSheetToPackage(db, packageId);

        var result = Run(db, packageId);

        Assert.Equal(1, result.GroupGenerated);
        using var check = db.Factory.CreateDbContext();
        var sheet = check.GeneratedGroupDocuments.Single();
        Assert.Equal(intakeId, sheet.IntakeId);
        Assert.Equal(check.GenerationPackageRuns.Single().Id, sheet.RunId);
    }

    [Fact]
    public void RunPackage_PermanentStaffOnly_GroupSheetHasNoIntake()
    {
        using var db = new TestDb();
        var (packageId, _) = SeedPackage(db, TemplateFixtures.Roster(2));
        AddSheetToPackage(db, packageId);

        Run(db, packageId, selection: RosterSelection.Everyone with { PermanentStaffOnly = true });

        using var check = db.Factory.CreateDbContext();
        Assert.Null(check.GeneratedGroupDocuments.Single().IntakeId);
    }

    [Fact]
    public void RunPackage_IntakeScope_GeneratesOnlyForMembersOfThatIntake()
    {
        using var db = new TestDb();
        var (packageId, recipientIds) = SeedPackage(db, TemplateFixtures.Roster(3));
        var intakeId = AttachToNewIntake(db, recipientIds[0]);

        var result = Run(db, packageId, selection: RosterSelection.Everyone with { IntakeId = intakeId });

        Assert.Equal(1, result.Generated);
        Assert.Single(Directory.GetFiles(_folder, "*.docx", SearchOption.AllDirectories));
    }

    [Fact]
    public void RunPackage_IntakeScopeWithPermanentStaff_KeepsStaffInPermanentStaffFolder()
    {
        using var db = new TestDb();
        var (packageId, recipientIds) = SeedPackage(db, TemplateFixtures.Roster(3));
        var intakeId = AttachToNewIntake(db, recipientIds[0]);

        var result = Run(db, packageId, selection: RosterSelection.Everyone with
        {
            IntakeId = intakeId,
            IncludePermanentStaff = true
        });

        Assert.Equal(3, result.Generated);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(_folder, "Постійний склад"), "*.docx", SearchOption.AllDirectories).Length);
        Assert.Single(Directory.GetFiles(Path.Combine(_folder, "Набір №29"), "*.docx", SearchOption.AllDirectories));
    }

    private static int AttachToNewIntake(TestDb db, int recipientId)
    {
        using var ctx = db.Factory.CreateDbContext();
        var intake = new Intake { Number = 29, DisplayNumber = "Набір №29", Status = IntakeStatus.Active };
        ctx.Intakes.Add(intake);
        ctx.SaveChanges();

        ctx.Recipients.First(r => r.Id == recipientId).IntakeId = intake.Id;
        ctx.SaveChanges();
        return intake.Id;
    }
}
