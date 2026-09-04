using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Documents;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Archive;

public class VersionSnapshotAndAttachmentTests : IDisposable
{
    private static readonly ArchiveFilter NoFilter = new(null, null, null, null, null, 0, 200);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-attach-{Guid.NewGuid():N}");

    public VersionSnapshotAndAttachmentTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private string WriteFile(string name, int bytes)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    private sealed record Seeded(int DocumentId, int RecipientId, int TemplateId, int OldNodeId, int NewNodeId, int IntakeId);

    private static Seeded Seed(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();

        var user = new UserProfile { FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now };
        ctx.Users.Add(user);

        var root = new OrgNode { Name = "Курс", Depth = 0, SortOrder = 0, Path = "/" };
        ctx.OrgNodes.Add(root);
        ctx.SaveChanges();
        root.Path = $"/{root.Id}/";
        var oldNode = new OrgNode { Name = "Взвод 2", ParentId = root.Id, Depth = 1, SortOrder = 0 };
        var newNode = new OrgNode { Name = "Взвод 3", ParentId = root.Id, Depth = 1, SortOrder = 1 };
        ctx.OrgNodes.AddRange(oldNode, newNode);
        ctx.SaveChanges();
        oldNode.Path = $"{root.Path}{oldNode.Id}/";
        newNode.Path = $"{root.Path}{newNode.Id}/";

        var intake = new Intake { Number = 4, DisplayNumber = "Набір №4" };
        ctx.Intakes.Add(intake);

        var template = new Template
        {
            Name = "Рапорт", OriginalFileName = "r.docx",
            Content = Array.Empty<byte>(), UploadedAt = DateTime.Now, Kind = TemplateKind.PerRecipient
        };
        ctx.Templates.Add(template);

        var person = new Recipient
        {
            LastName = "ШЕВЧЕНКО", FirstName = "Тарас",
            Rank = "майор", Position = "слухач", ServiceNumber = "1", OrgNodeId = oldNode.Id
        };
        ctx.Recipients.Add(person);
        ctx.SaveChanges();

        var doc = new GeneratedDocument
        {
            RecipientId = person.Id,
            TemplateId = template.Id,
            GeneratedAt = DateTime.Now.AddMinutes(-5),
            GeneratedByUserId = user.Id,
            FileName = "rapport.docx",
            SizeBytes = 3,
            Version = 1,
            IsCurrent = true,
            SourceType = DocumentSourceType.Generated,
            OrgNodeIdSnapshot = oldNode.Id,
            OrgPathSnapshot = "Курс / Взвод 2",
            HasContent = true,
            Content = new GeneratedDocumentContent { Content = new byte[] { 1, 2, 3 } }
        };
        ctx.GeneratedDocuments.Add(doc);
        ctx.SaveChanges();

        return new Seeded(doc.Id, person.Id, template.Id, oldNode.Id, newNode.Id, intake.Id);
    }

    private static GeneratedDocument Current(TestDb db, Seeded s)
    {
        using var ctx = db.Factory.CreateDbContext();
        return ctx.GeneratedDocuments.Single(g => g.RecipientId == s.RecipientId && g.TemplateId == s.TemplateId && g.IsCurrent);
    }

    [Fact]
    public async Task A_new_version_takes_the_unit_and_intake_from_the_person_as_they_are_now()
    {
        using var db = new TestDb();
        var s = Seed(db);
        using (var ctx = db.Factory.CreateDbContext())
        {
            var person = ctx.Recipients.Single(r => r.Id == s.RecipientId);
            person.OrgNodeId = s.NewNodeId;
            person.IntakeId = s.IntakeId;
            ctx.SaveChanges();
        }

        var result = await TestServices.Archive(db).UploadManualAsync(s.DocumentId, WriteFile("scan.pdf", 10), null);

        Assert.True(result.Success, result.ErrorMessage);
        var current = Current(db, s);
        Assert.Equal(2, current.Version);
        Assert.Equal(s.NewNodeId, current.OrgNodeIdSnapshot);
        Assert.Equal("Курс / Взвод 3", current.OrgPathSnapshot);
        Assert.Equal(s.IntakeId, current.IntakeId);
    }

    [Fact]
    public async Task A_new_version_for_a_person_in_the_trash_keeps_the_previous_snapshot()
    {
        using var db = new TestDb();
        var s = Seed(db);
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Recipients.Single(r => r.Id == s.RecipientId).DeletedAt = DateTime.Now;
            ctx.SaveChanges();
        }

        var result = await TestServices.Archive(db).UploadManualAsync(s.DocumentId, WriteFile("scan.pdf", 10), null);

        Assert.True(result.Success, result.ErrorMessage);
        var current = Current(db, s);
        Assert.Equal(s.OldNodeId, current.OrgNodeIdSnapshot);
        Assert.Equal("Курс / Взвод 2", current.OrgPathSnapshot);
    }

    [Fact]
    public async Task Attachments_stay_visible_after_a_new_version_and_say_which_version_they_belong_to()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var service = TestServices.Archive(db);

        var attached = await service.AttachAsync(s.DocumentId, WriteFile("signed.pdf", 10), "підписано");
        Assert.True(attached.Success, attached.ErrorMessage);
        var uploaded = await service.UploadManualAsync(s.DocumentId, WriteFile("v2.docx", 10), null);
        Assert.True(uploaded.Success, uploaded.ErrorMessage);

        var row = Assert.Single(await service.QueryAsync(NoFilter));
        var current = await service.GetCurrentRowAsync(s.RecipientId, s.TemplateId);
        var attachments = await service.GetAttachmentsAsync(s.RecipientId, s.TemplateId);

        Assert.Equal(2, row.Version);
        Assert.Equal(1, row.AttachmentCount);
        Assert.Equal(1, current!.AttachmentCount);
        var attachment = Assert.Single(attachments);
        Assert.Equal("signed.pdf", attachment.FileName);
        Assert.Equal(1, attachment.DocumentVersion);
    }

    [Fact]
    public async Task A_deleted_attachment_is_not_counted_across_the_chain()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var service = TestServices.Archive(db);
        await service.AttachAsync(s.DocumentId, WriteFile("signed.pdf", 10), null);
        await service.UploadManualAsync(s.DocumentId, WriteFile("v2.docx", 10), null);
        var attachment = Assert.Single(await service.GetAttachmentsAsync(s.RecipientId, s.TemplateId));

        await service.DeleteAttachmentAsync(attachment.Id);

        var row = Assert.Single(await service.QueryAsync(NoFilter));
        Assert.Equal(0, row.AttachmentCount);
        Assert.Empty(await service.GetAttachmentsAsync(s.RecipientId, s.TemplateId));
    }

    [Fact]
    public async Task Attaching_a_file_above_the_size_limit_is_refused_and_nothing_is_stored()
    {
        using var db = new TestDb();
        var s = Seed(db);
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.AppSettings.Add(new AppSettings { MaxDocumentSizeKb = 1 });
            ctx.SaveChanges();
        }

        var result = await TestServices.Archive(db).AttachAsync(s.DocumentId, WriteFile("huge.pdf", 3 * 1024), null);

        Assert.False(result.Success);
        Assert.Contains("перевищує ліміт", result.ErrorMessage!, StringComparison.Ordinal);
        using var check = db.Factory.CreateDbContext();
        Assert.Empty(check.DocumentAttachments.ToList());
    }

    [Fact]
    public async Task Attaching_to_a_document_that_is_gone_reports_it_instead_of_throwing()
    {
        using var db = new TestDb();
        var s = Seed(db);
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.GeneratedDocuments.Single(g => g.Id == s.DocumentId).DeletedAt = DateTime.Now;
            ctx.SaveChanges();
        }

        var result = await TestServices.Archive(db).AttachAsync(s.DocumentId, WriteFile("scan.pdf", 10), null);

        Assert.False(result.Success);
        Assert.Contains("відсутній в архіві", result.ErrorMessage!, StringComparison.Ordinal);
    }
}
