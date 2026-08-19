using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Completeness;

// Вада 1.4: груповий шаблон (один документ на весь склад) показувався в картці особи
// як персональний «Немає · Згенерувати» і давав порожню колонку в матриці.
public class GroupTemplatesOutsideMatrixTests
{
    private static (int PackageId, int IntakeId, int PersonId, int GroupTemplateId) Seed(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
        var intake = new Intake { Number = 1, DisplayNumber = "Набір №1" };
        ctx.Intakes.Add(intake);
        var person = TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас");
        ctx.Recipients.Add(person);
        var personal = new Template { Name = "Рапорт ІНДИВІДУАЛЬНИЙ", OriginalFileName = "a.docx", Content = new byte[] { 1 }, UploadedAt = DateTime.Now, Kind = TemplateKind.PerRecipient };
        var group = new Template { Name = "Рапорт ГРУПОВИЙ", OriginalFileName = "b.docx", Content = new byte[] { 1 }, UploadedAt = DateTime.Now, Kind = TemplateKind.Group };
        ctx.Templates.AddRange(personal, group);
        ctx.SaveChanges();
        person.IntakeId = intake.Id;

        var package = new GenerationPackage { Name = "П" };
        package.Templates.Add(new GenerationPackageTemplate { TemplateId = personal.Id, SortOrder = 0 });
        package.Templates.Add(new GenerationPackageTemplate { TemplateId = group.Id, SortOrder = 1 });
        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();

        ctx.GeneratedGroupDocuments.Add(new GeneratedGroupDocument
        {
            TemplateId = group.Id, IntakeId = intake.Id, GeneratedAt = DateTime.Now, GeneratedByUserId = 1,
            FileName = "group.docx", Version = 8, IsCurrent = true, HasContent = true, RecipientCount = 3
        });
        ctx.SaveChanges();
        return (package.Id, intake.Id, person.Id, group.Id);
    }

    [Fact]
    public async Task Matrix_ExcludesGroupTemplates()
    {
        using var db = new TestDb();
        var (packageId, intakeId, _, _) = Seed(db);

        var data = await TestServices.Completeness(db).BuildAsync(intakeId, packageId);

        Assert.Single(data.Templates);
        Assert.Equal("Рапорт ІНДИВІДУАЛЬНИЙ", data.Templates[0].Name);
    }

    [Fact]
    public async Task RecipientStatus_ExcludesGroupTemplates()
    {
        using var db = new TestDb();
        var (packageId, _, personId, _) = Seed(db);

        var statuses = await TestServices.Completeness(db).GetRecipientStatusAsync(personId, packageId);

        Assert.Single(statuses);
        Assert.Equal("Рапорт ІНДИВІДУАЛЬНИЙ", statuses[0].TemplateName);
    }

    [Fact]
    public async Task PackageLinks_KeepGroupTemplatesWithFlag()
    {
        using var db = new TestDb();
        var (packageId, _, _, groupTemplateId) = Seed(db);

        var links = await TestServices.Completeness(db).GetPackageLinksAsync(packageId);

        Assert.Equal(2, links.Count);
        Assert.True(links.Single(l => l.TemplateId == groupTemplateId).IsGroup);
        Assert.False(links.Single(l => l.TemplateId != groupTemplateId).IsGroup);
    }

    [Fact]
    public async Task PackageGroupDocuments_ReturnCurrentVersionForIntake()
    {
        using var db = new TestDb();
        var (packageId, intakeId, _, groupTemplateId) = Seed(db);

        var docs = await TestServices.Completeness(db).GetPackageGroupDocumentsAsync(packageId, intakeId);

        var doc = Assert.Single(docs);
        Assert.Equal(groupTemplateId, doc.TemplateId);
        Assert.Equal(8, doc.Version);
        Assert.NotNull(doc.GroupDocumentId);
    }
}
