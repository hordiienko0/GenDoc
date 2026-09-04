using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Completeness;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Personnel;

namespace GenDoc.Tests.Personnel;

public class PersonCardGroupDocumentsTests
{
    private sealed record Seeded(int PackageId, int IntakeId, int FitId, int LimitedId, int GroupTemplateId, int SheetId);

    private static Seeded Seed(TestDb db)
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

        var group = new Template
        {
            Name = "Рапорт ГРУПОВИЙ", OriginalFileName = "g.docx", Content = new byte[] { 1 },
            UploadedAt = DateTime.Now, Kind = TemplateKind.Group
        };
        ctx.Templates.Add(group);
        var sheet = new ExportTemplate
        {
            Name = "Роздавальна", OriginalFileName = "r.xlsx", Content = new byte[] { 1 },
            UploadedAt = DateTime.Now, UsesPlaceholders = true
        };
        ctx.ExportTemplates.Add(sheet);
        ctx.SaveChanges();

        fit.IntakeId = intake.Id;
        limited.IntakeId = intake.Id;

        var package = new GenerationPackage { Name = "П" };
        package.Templates.Add(new GenerationPackageTemplate
        {
            TemplateId = group.Id, SortOrder = 0,
            RequirementRegular = TemplateRequirement.Required,
            RequirementLimited = TemplateRequirement.NotApplicable
        });
        package.ExportTemplates.Add(new GenerationPackageExportTemplate
        {
            ExportTemplateId = sheet.Id, SortOrder = 0, FitnessFilter = FitnessFilter.RegularOnly
        });
        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();

        return new Seeded(package.Id, intake.Id, fit.Id, limited.Id, group.Id, sheet.Id);
    }

    private static int AddGroupDoc(TestDb db, Seeded s, int version, bool sheet, params int[] participants)
    {
        using var ctx = db.Factory.CreateDbContext();
        var doc = new GeneratedGroupDocument
        {
            TemplateId = sheet ? null : s.GroupTemplateId,
            ExportTemplateId = sheet ? s.SheetId : null,
            IntakeId = s.IntakeId, GeneratedAt = DateTime.Now, GeneratedByUserId = 1,
            FileName = sheet ? "r.xlsx" : "g.docx", Version = version, IsCurrent = true, HasContent = true,
            RecipientCount = participants.Length
        };
        foreach (var id in participants) doc.Recipients.Add(new GeneratedGroupDocumentRecipient { RecipientId = id });
        ctx.GeneratedGroupDocuments.Add(doc);
        ctx.SaveChanges();
        return doc.Id;
    }

    [Fact]
    public async Task ANotApplicableGroupDocumentIsNotAGapForThatPerson()
    {
        using var db = new TestDb();
        var s = Seed(db);

        var statuses = await TestServices.Completeness(db).GetPackageGroupDocumentsAsync(s.PackageId, s.IntakeId, s.LimitedId);

        Assert.Equal(2, statuses.Count);
        Assert.All(statuses, st => Assert.Equal(TemplateRequirement.NotApplicable, st.Requirement));
        Assert.False(PersonCardViewModel.FindGaps(Array.Empty<RecipientDocStatus>(), statuses).Group);
    }

    [Fact]
    public async Task SheetsOfThePackageAreListedNextToGroupDocx()
    {
        using var db = new TestDb();
        var s = Seed(db);

        var statuses = await TestServices.Completeness(db).GetPackageGroupDocumentsAsync(s.PackageId, s.IntakeId, s.FitId);

        var sheet = Assert.Single(statuses, st => st.IsExport);
        Assert.Equal("Роздавальна", sheet.TemplateName);
        Assert.Equal(TemplateRequirement.Required, sheet.Requirement);
        Assert.Single(statuses, st => !st.IsExport && st.TemplateId == s.GroupTemplateId);
    }

    [Fact]
    public async Task ARequiredGroupDocumentWithoutThePersonIsAGap()
    {
        using var db = new TestDb();
        var s = Seed(db);
        AddGroupDoc(db, s, version: 1, sheet: true, s.LimitedId);

        var statuses = await TestServices.Completeness(db).GetPackageGroupDocumentsAsync(s.PackageId, s.IntakeId, s.FitId);

        Assert.True(PersonCardViewModel.FindGaps(Array.Empty<RecipientDocStatus>(), statuses).Group);
        Assert.False(PersonCardViewModel.FindGaps(Array.Empty<RecipientDocStatus>(), statuses).Personal);
    }

    [Fact]
    public async Task TheNewestCurrentDocumentIsUsed()
    {
        using var db = new TestDb();
        var s = Seed(db);
        AddGroupDoc(db, s, version: 7, sheet: true, s.FitId);
        AddGroupDoc(db, s, version: 5, sheet: true, s.LimitedId);
        AddGroupDoc(db, s, version: 2, sheet: false, s.LimitedId);
        var newestDocx = AddGroupDoc(db, s, version: 3, sheet: false, s.FitId);

        var statuses = await TestServices.Completeness(db).GetPackageGroupDocumentsAsync(s.PackageId, s.IntakeId, s.FitId);

        var sheet = Assert.Single(statuses, st => st.IsExport);
        Assert.Equal(7, sheet.Version);
        Assert.True(sheet.IsParticipant);
        var docx = Assert.Single(statuses, st => !st.IsExport);
        Assert.Equal(newestDocx, docx.GroupDocumentId);
        Assert.True(docx.IsParticipant);
    }

    [Fact]
    public void OnlyMissingRequiredPersonalDocumentsLightTheFullPackageButton()
    {
        var personal = new[]
        {
            new RecipientDocStatus(1, "Рапорт", 10, 1, HasContent: true, IsStale: false),
            new RecipientDocStatus(2, "Довідка", null, 0, HasContent: false, IsStale: false, TemplateRequirement.Optional)
        };
        var groups = new[]
        {
            new PackageGroupDocumentStatus(3, "Відомість", null, 0, Requirement: TemplateRequirement.Required)
        };

        var gaps = PersonCardViewModel.FindGaps(personal, groups);

        Assert.False(gaps.Personal);
        Assert.True(gaps.Group);
    }

    [Fact]
    public void AMissingRequiredPersonalDocumentIsAPersonalGap()
    {
        var personal = new[]
        {
            new RecipientDocStatus(1, "Рапорт", null, 0, HasContent: false, IsStale: false)
        };

        Assert.True(PersonCardViewModel.FindGaps(personal, Array.Empty<PackageGroupDocumentStatus>()).Personal);
    }

    [Fact]
    public void MissingPersonalRowShowsItsRequirement()
    {
        var required = new RecipientDocRowViewModel(new RecipientDocStatus(1, "Рапорт", null, 0, false, false));
        var optional = new RecipientDocRowViewModel(new RecipientDocStatus(1, "Рапорт", null, 0, false, false, TemplateRequirement.Optional));
        var notApplicable = new RecipientDocRowViewModel(new RecipientDocStatus(1, "Рапорт", null, 0, false, false, TemplateRequirement.NotApplicable));
        var present = new RecipientDocRowViewModel(new RecipientDocStatus(1, "Рапорт", 5, 2, true, false));

        Assert.Equal("Немає", required.StateText);
        Assert.Equal("обов'язковий", required.MetaText);
        Assert.Equal("необов'язковий", optional.MetaText);
        Assert.Equal("не застосовується", notApplicable.MetaText);
        Assert.Equal("версія 2", present.MetaText);
    }

    [Fact]
    public void GroupRowsShowTheRequirementWhenTheDocumentIsMissing()
    {
        var missingRequired = new GroupDocumentRowViewModel(
            new PackageGroupDocumentStatus(1, "Відомість", null, 0, Requirement: TemplateRequirement.Required));
        var notApplicable = new GroupDocumentRowViewModel(
            new PackageGroupDocumentStatus(1, "Відомість", null, 0, Requirement: TemplateRequirement.NotApplicable));
        var notInRoster = new GroupDocumentRowViewModel(
            new PackageGroupDocumentStatus(1, "Відомість", 9, 4, IsParticipant: false, Requirement: TemplateRequirement.Optional));
        var inRoster = new GroupDocumentRowViewModel(
            new PackageGroupDocumentStatus(1, "Відомість", 9, 4, IsParticipant: true));

        Assert.Equal("ще не сформовано · обов'язковий", missingRequired.StateText);
        Assert.Equal("не застосовується для цієї категорії", notApplicable.StateText);
        Assert.Equal("не входить до чинного складу (в.4) · необов'язковий", notInRoster.StateText);
        Assert.Equal("у складі, в.4", inRoster.StateText);
    }

    [Fact]
    public void GroupGapsGetTheirOwnHintInsteadOfTheFullPackageButton()
        => Assert.Equal("Відомості формуються на екрані «Генерація» або в «Комплектності»", PersonCardViewModel.GroupGapsHint);
}
