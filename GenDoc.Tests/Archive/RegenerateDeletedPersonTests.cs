using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Documents;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Archive;

public class RegenerateDeletedPersonTests
{
    private static readonly ArchiveFilter NoFilter = new(null, null, null, null, null, 0, 200);

    private static (int DocumentId, int RecipientId) Seed(TestDb db, bool personDeleted)
    {
        using var ctx = db.Factory.CreateDbContext();

        var user = new UserProfile { FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now };
        ctx.Users.Add(user);

        var template = new Template
        {
            Name = "Рапорт", OriginalFileName = "r.docx",
            Content = Array.Empty<byte>(), UploadedAt = DateTime.Now, Kind = TemplateKind.PerRecipient
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

        if (personDeleted)
        {
            person.DeletedAt = DateTime.Now;
            ctx.SaveChanges();
        }

        return (doc.Id, person.Id);
    }

    [Fact]
    public async Task Regenerating_for_a_person_in_the_trash_fails_softly_instead_of_throwing()
    {
        using var db = new TestDb();
        var (docId, _) = Seed(db, personDeleted: true);

        var result = await TestServices.Archive(db).RegenerateAsync(docId, new Dictionary<string, string>());

        Assert.False(result.Success);
        Assert.Contains("кошик", result.ErrorMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Regenerating_a_document_that_is_gone_reports_it_instead_of_throwing()
    {
        using var db = new TestDb();
        var (docId, _) = Seed(db, personDeleted: false);
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.GeneratedDocuments.First(g => g.Id == docId).DeletedAt = DateTime.Now;
            ctx.SaveChanges();
        }

        var result = await TestServices.Archive(db).RegenerateAsync(docId, new Dictionary<string, string>());

        Assert.False(result.Success);
        Assert.Contains("відсутній в архіві", result.ErrorMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_archive_row_knows_when_its_person_is_in_the_trash()
    {
        using var db = new TestDb();
        Seed(db, personDeleted: true);

        var row = Assert.Single(await TestServices.Archive(db).QueryAsync(NoFilter));

        Assert.False(row.RecipientAlive);
    }

    [Fact]
    public async Task The_archive_row_of_a_living_person_is_marked_alive()
    {
        using var db = new TestDb();
        var (_, recipientId) = Seed(db, personDeleted: false);
        var service = TestServices.Archive(db);

        var row = Assert.Single(await service.QueryAsync(NoFilter));
        var current = await service.GetCurrentRowAsync(recipientId, row.TemplateId);

        Assert.True(row.RecipientAlive);
        Assert.True(current!.RecipientAlive);
    }
}
