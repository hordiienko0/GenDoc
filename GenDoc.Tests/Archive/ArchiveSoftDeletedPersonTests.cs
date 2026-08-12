using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Documents;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Archive;

// Документ живе довше за людину: людину можна прибрати в кошик (м'яке видалення),
// а її документи лишаються в архіві. Лічильник унизу екрана рахує їх завжди, бо
// не чіпає таблицю Recipients, — тож список зобов'язаний показувати те саме.
public class ArchiveSoftDeletedPersonTests
{
    private static readonly ArchiveFilter NoFilter = new(null, null, null, null, null, 0, 200);

    private static int SeedDocumentForSoftDeletedPerson(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();

        var user = new UserProfile
        {
            FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now
        };
        ctx.Users.Add(user);

        var template = new Template
        {
            Name = "Рапорт", OriginalFileName = "r.docx",
            Content = Array.Empty<byte>(), UploadedAt = DateTime.Now
        };
        ctx.Templates.Add(template);

        var person = new Recipient
        {
            LastName = "ПОНОМАРЕНКО", FirstName = "Ігор", MiddleName = "Петрович",
            Rank = "капітан", Position = "слухач", ServiceNumber = "77123"
        };
        ctx.Recipients.Add(person);
        ctx.SaveChanges();

        var doc = new GeneratedDocument
        {
            RecipientId = person.Id,
            TemplateId = template.Id,
            GeneratedAt = new DateTime(2026, 3, 15),
            GeneratedByUserId = user.Id,
            FileName = "rapport.docx",
            SizeBytes = 3,
            Version = 1,
            IsCurrent = true,
            SourceType = DocumentSourceType.Generated,
            HasContent = true,
            Content = new GeneratedDocumentContent { Content = new byte[] { 1, 2, 3 } }
        };
        ctx.GeneratedDocuments.Add(doc);
        ctx.SaveChanges();

        // Людина йде в кошик уже після того, як документ згенеровано.
        person.DeletedAt = DateTime.Now;
        ctx.SaveChanges();

        return doc.Id;
    }

    [Fact]
    public async Task Document_of_a_person_in_the_trash_stays_in_the_list()
    {
        using var db = new TestDb();
        var docId = SeedDocumentForSoftDeletedPerson(db);

        var rows = await TestServices.Archive(db).QueryAsync(NoFilter);

        Assert.Single(rows);
        Assert.Equal(docId, rows[0].Id);
    }

    [Fact]
    public async Task List_and_footer_counter_agree_when_the_person_is_in_the_trash()
    {
        using var db = new TestDb();
        SeedDocumentForSoftDeletedPerson(db);

        var service = TestServices.Archive(db);
        var rows = await service.QueryAsync(NoFilter);
        var stats = await service.GetStatsAsync(NoFilter);

        // Саме це розходження бачив користувач: «351 документів» у підвалі
        // і «Нічого не знайдено» в таблиці.
        Assert.Equal(stats.Count, rows.Count);
    }

    [Fact]
    public async Task Name_of_a_person_in_the_trash_is_still_shown()
    {
        using var db = new TestDb();
        SeedDocumentForSoftDeletedPerson(db);

        var rows = await TestServices.Archive(db).QueryAsync(NoFilter);

        Assert.Equal("ПОНОМАРЕНКО", rows[0].LastName);
        Assert.Equal("Ігор", rows[0].FirstName);
    }
}
