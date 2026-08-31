using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Completeness;

// Групові шаблони в комплектності. Історія: 19.08 (вада 1.4) групові прибрали з
// матриці, бо клітинка була беззмістовна («один документ на всіх» у рядку однієї
// людини). 20.08 (v25) вони повернулись - тепер клітинка каже, чи людина В СКЛАДІ
// чинного групового документа, з посиланням на нього.
public class GroupTemplatesOutsideMatrixTests
{
    private static (int PackageId, int IntakeId, int PersonInId, int PersonOutId, int GroupTemplateId, int GroupDocId)
        Seed(TestDb db, bool withParticipants = true)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
        var intake = new Intake { Number = 1, DisplayNumber = "Набір №1" };
        ctx.Intakes.Add(intake);
        var personIn = TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас");
        var personOut = TemplateFixtures.Person(2, "ФРАНКО", "Іван");
        ctx.Recipients.AddRange(personIn, personOut);
        var personal = new Template { Name = "Рапорт ІНДИВІДУАЛЬНИЙ", OriginalFileName = "a.docx", Content = new byte[] { 1 }, UploadedAt = DateTime.Now, Kind = TemplateKind.PerRecipient };
        var group = new Template { Name = "Рапорт ГРУПОВИЙ", OriginalFileName = "b.docx", Content = new byte[] { 1 }, UploadedAt = DateTime.Now, Kind = TemplateKind.Group };
        ctx.Templates.AddRange(personal, group);
        ctx.SaveChanges();
        personIn.IntakeId = intake.Id;
        personOut.IntakeId = intake.Id;

        var package = new GenerationPackage { Name = "П" };
        package.Templates.Add(new GenerationPackageTemplate { TemplateId = personal.Id, SortOrder = 0 });
        package.Templates.Add(new GenerationPackageTemplate { TemplateId = group.Id, SortOrder = 1 });
        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();

        var doc = new GeneratedGroupDocument
        {
            TemplateId = group.Id, IntakeId = intake.Id, GeneratedAt = DateTime.Now, GeneratedByUserId = 1,
            FileName = "group.docx", Version = 8, IsCurrent = true, HasContent = true, RecipientCount = 1
        };
        if (withParticipants)
            doc.Recipients.Add(new GeneratedGroupDocumentRecipient { RecipientId = personIn.Id });
        ctx.GeneratedGroupDocuments.Add(doc);
        ctx.SaveChanges();
        return (package.Id, intake.Id, personIn.Id, personOut.Id, group.Id, doc.Id);
    }

    [Fact]
    public async Task Matrix_GroupColumn_MarksParticipantsAndOnlyThem()
    {
        using var db = new TestDb();
        var (packageId, intakeId, personInId, personOutId, groupTemplateId, groupDocId) = Seed(db);

        var data = await TestServices.Completeness(db).BuildAsync(intakeId, packageId);

        Assert.Contains(data.Templates, t => t.TemplateId == groupTemplateId && t.IsGroup);

        var cell = data.Docs[(personInId, groupTemplateId, false)];
        Assert.True(cell.IsGroup);
        Assert.False(cell.RosterUnknown);
        Assert.Equal(groupDocId, cell.Id);
        Assert.Equal(8, cell.Version);

        Assert.False(data.Docs.ContainsKey((personOutId, groupTemplateId, false)));
    }

    // Документ, згенерований до v25: складу немає (RecipientCount > 0, учасників 0) -
    // усі отримують «склад не записано», а не хибне «немає».
    [Fact]
    public async Task Matrix_LegacyGroupDoc_GivesRosterUnknownToEveryone()
    {
        using var db = new TestDb();
        var (packageId, intakeId, personInId, personOutId, groupTemplateId, _) = Seed(db, withParticipants: false);

        var data = await TestServices.Completeness(db).BuildAsync(intakeId, packageId);

        Assert.True(data.Docs[(personInId, groupTemplateId, false)].RosterUnknown);
        Assert.True(data.Docs[(personOutId, groupTemplateId, false)].RosterUnknown);
    }

    // Персональний список картки («Сформувати повний пакет») групових як і раніше не містить.
    [Fact]
    public async Task RecipientStatus_StillExcludesGroupTemplates()
    {
        using var db = new TestDb();
        var (packageId, _, personInId, _, _, _) = Seed(db);

        var statuses = await TestServices.Completeness(db).GetRecipientStatusAsync(personInId, packageId);

        Assert.Single(statuses);
        Assert.Equal("Рапорт ІНДИВІДУАЛЬНИЙ", statuses[0].TemplateName);
    }

    [Fact]
    public async Task PackageLinks_KeepGroupTemplatesWithFlag()
    {
        using var db = new TestDb();
        var (packageId, _, _, _, groupTemplateId, _) = Seed(db);

        var links = await TestServices.Completeness(db).GetPackageLinksAsync(packageId);

        Assert.Equal(2, links.Count);
        Assert.True(links.Single(l => l.TemplateId == groupTemplateId).IsGroup);
    }

    // Підвал картки: участь конкретної людини.
    [Fact]
    public async Task PackageGroupDocuments_ReportParticipationPerPerson()
    {
        using var db = new TestDb();
        var (packageId, intakeId, personInId, personOutId, groupTemplateId, groupDocId) = Seed(db);

        var svc = TestServices.Completeness(db);
        var forIn = Assert.Single(await svc.GetPackageGroupDocumentsAsync(packageId, intakeId, personInId));
        var forOut = Assert.Single(await svc.GetPackageGroupDocumentsAsync(packageId, intakeId, personOutId));

        Assert.Equal(groupDocId, forIn.GroupDocumentId);
        Assert.True(forIn.IsParticipant);
        Assert.False(forIn.RosterUnknown);
        Assert.False(forOut.IsParticipant);
    }
}

// «Учасники» в «Архів → Групові»: склад конкретної версії, включно з м'яко видаленими.
public class GroupParticipantsQueryTests
{
    [Fact]
    public async Task GetGroupParticipants_ReturnsRosterEvenForSoftDeletedPeople()
    {
        using var db = new TestDb();
        int docId, deletedId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
            var alive = TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас");
            var deleted = TemplateFixtures.Person(2, "ФРАНКО", "Іван");
            ctx.Recipients.AddRange(alive, deleted);
            var group = new Template { Name = "Рапорт ГРУПОВИЙ", OriginalFileName = "b.docx", Content = new byte[] { 1 }, UploadedAt = DateTime.Now, Kind = TemplateKind.Group };
            ctx.Templates.Add(group);
            ctx.SaveChanges();

            var doc = new GeneratedGroupDocument
            {
                TemplateId = group.Id, GeneratedAt = DateTime.Now, GeneratedByUserId = 1,
                FileName = "group.docx", Version = 1, IsCurrent = true, HasContent = true, RecipientCount = 2
            };
            doc.Recipients.Add(new GeneratedGroupDocumentRecipient { RecipientId = alive.Id });
            doc.Recipients.Add(new GeneratedGroupDocumentRecipient { RecipientId = deleted.Id });
            ctx.GeneratedGroupDocuments.Add(doc);
            ctx.SaveChanges();

            deleted.DeletedAt = DateTime.Now;
            ctx.SaveChanges();
            docId = doc.Id;
            deletedId = deleted.Id;
        }

        var participants = await TestServices.Archive(db).GetGroupParticipantsAsync(docId);

        Assert.Equal(2, participants.Count);
        Assert.Contains(participants, p => p.RecipientId == deletedId);
    }
}
