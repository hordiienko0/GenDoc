using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Completeness;

public class GroupSheetsInMatrixTests
{
    private sealed record Seeded(
        int PackageId, int IntakeId, int FitPersonId, int LimitedPersonId,
        int SheetId, int SheetDocId, int PersonalTemplateId);

    private static Seeded Seed(
        TestDb db,
        FitnessFilter filter = FitnessFilter.All,
        bool generateSheet = true,
        bool includeLimited = true)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });

        var intake = new Intake { Number = 1, DisplayNumber = "Набір №1" };
        ctx.Intakes.Add(intake);

        var fit = TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас");
        fit.FitnessCategory = "придатний";
        var limited = TemplateFixtures.Person(2, "ФРАНКО", "Іван");
        limited.FitnessCategory = "обмежено придатний";
        ctx.Recipients.AddRange(fit, limited);

        var personal = new Template
        {
            Name = "Рапорт", OriginalFileName = "a.docx", Content = new byte[] { 1 },
            UploadedAt = DateTime.Now, Kind = TemplateKind.PerRecipient
        };
        ctx.Templates.Add(personal);

        var sheet = new ExportTemplate
        {
            Name = "Котлове забезпечення", OriginalFileName = "kotlove.xlsx",
            Content = new byte[] { 1 }, UploadedAt = DateTime.Now, UsesPlaceholders = true
        };
        ctx.ExportTemplates.Add(sheet);
        ctx.SaveChanges();

        fit.IntakeId = intake.Id;
        limited.IntakeId = intake.Id;

        var package = new GenerationPackage { Name = "Пакет" };
        package.Templates.Add(new GenerationPackageTemplate { TemplateId = personal.Id, SortOrder = 0 });
        package.ExportTemplates.Add(new GenerationPackageExportTemplate
        {
            ExportTemplateId = sheet.Id, SortOrder = 0, FitnessFilter = filter
        });
        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();

        var sheetDocId = 0;
        if (generateSheet)
        {
            var doc = new GeneratedGroupDocument
            {
                ExportTemplateId = sheet.Id,
                IntakeId = intake.Id,
                GeneratedAt = DateTime.Now,
                GeneratedByUserId = 1,
                FileName = "kotlove.xlsx",
                Version = 3,
                IsCurrent = true,
                HasContent = true,
                RecipientCount = includeLimited ? 2 : 1
            };
            doc.Recipients.Add(new GeneratedGroupDocumentRecipient { RecipientId = fit.Id });
            if (includeLimited)
                doc.Recipients.Add(new GeneratedGroupDocumentRecipient { RecipientId = limited.Id });

            ctx.GeneratedGroupDocuments.Add(doc);
            ctx.SaveChanges();
            sheetDocId = doc.Id;
        }

        return new Seeded(package.Id, intake.Id, fit.Id, limited.Id, sheet.Id, sheetDocId, personal.Id);
    }

    [Fact]
    public async Task ASheetOfThePackageBecomesAMatrixColumn()
    {
        using var db = new TestDb();
        var s = Seed(db);

        var data = await TestServices.Completeness(db).BuildAsync(s.IntakeId, s.PackageId);

        var column = Assert.Single(data.Templates, t => t.IsExport);
        Assert.Equal(s.SheetId, column.TemplateId);
        Assert.Equal("Котлове забезпечення", column.Name);
        Assert.True(column.IsGroup);
    }

    [Fact]
    public async Task APersonInTheSheetGetsAPresentCell()
    {
        using var db = new TestDb();
        var s = Seed(db);

        var data = await TestServices.Completeness(db).BuildAsync(s.IntakeId, s.PackageId);

        Assert.True(data.Docs.TryGetValue((s.FitPersonId, s.SheetId, true), out var cell));
        Assert.Equal(s.SheetDocId, cell!.Id);
        Assert.True(cell.IsGroup);
        Assert.True(cell.IsExport);
        Assert.False(cell.IsStale);
    }

    [Fact]
    public async Task APersonMissingFromTheSheetHasNoCell()
    {
        using var db = new TestDb();
        var s = Seed(db, includeLimited: false);

        var data = await TestServices.Completeness(db).BuildAsync(s.IntakeId, s.PackageId);

        Assert.True(data.Docs.ContainsKey((s.FitPersonId, s.SheetId, true)));
        Assert.False(data.Docs.ContainsKey((s.LimitedPersonId, s.SheetId, true)));
    }

    [Fact]
    public async Task NoCellsAtAllUntilTheSheetIsGenerated()
    {
        using var db = new TestDb();
        var s = Seed(db, generateSheet: false);

        var data = await TestServices.Completeness(db).BuildAsync(s.IntakeId, s.PackageId);

        Assert.Single(data.Templates, t => t.IsExport);
        Assert.False(data.Docs.ContainsKey((s.FitPersonId, s.SheetId, true)));
    }

    [Fact]
    public async Task AnAllFilterMakesTheSheetRequiredForEveryone()
    {
        using var db = new TestDb();
        var s = Seed(db, FitnessFilter.All);

        var data = await TestServices.Completeness(db).BuildAsync(s.IntakeId, s.PackageId);
        var column = Assert.Single(data.Templates, t => t.IsExport);

        Assert.Equal(TemplateRequirement.Required, column.RequirementRegular);
        Assert.Equal(TemplateRequirement.Required, column.RequirementLimited);
    }

    [Fact]
    public async Task ARegularOnlyFilterLeavesLimitedPeopleOut()
    {
        using var db = new TestDb();
        var s = Seed(db, FitnessFilter.RegularOnly);

        var data = await TestServices.Completeness(db).BuildAsync(s.IntakeId, s.PackageId);
        var column = Assert.Single(data.Templates, t => t.IsExport);

        Assert.Equal(TemplateRequirement.Required, column.RequirementRegular);
        Assert.Equal(TemplateRequirement.NotApplicable, column.RequirementLimited);
    }

    [Fact]
    public async Task ALimitedOnlyFilterLeavesRegularPeopleOut()
    {
        using var db = new TestDb();
        var s = Seed(db, FitnessFilter.LimitedOnly);

        var data = await TestServices.Completeness(db).BuildAsync(s.IntakeId, s.PackageId);
        var column = Assert.Single(data.Templates, t => t.IsExport);

        Assert.Equal(TemplateRequirement.NotApplicable, column.RequirementRegular);
        Assert.Equal(TemplateRequirement.Required, column.RequirementLimited);
    }

    [Fact]
    public async Task ASheetNeverAppearsInThePerPersonList()
    {
        using var db = new TestDb();
        var s = Seed(db);

        var statuses = await TestServices.Completeness(db)
            .GetRecipientStatusAsync(s.FitPersonId, s.PackageId);

        Assert.Single(statuses);
        Assert.Equal("Рапорт", statuses[0].TemplateName);
    }

    [Fact]
    public async Task ASheetAndATemplateWithTheSameIdDoNotShareACell()
    {
        using var db = new TestDb();
        var s = Seed(db, generateSheet: false);

        Assert.Equal(s.PersonalTemplateId, s.SheetId);

        var data = await TestServices.Completeness(db).BuildAsync(s.IntakeId, s.PackageId);

        Assert.Equal(2, data.Templates.Count);
        Assert.Single(data.Templates, t => t.IsExport);
        Assert.Single(data.Templates, t => !t.IsExport);
    }
}
