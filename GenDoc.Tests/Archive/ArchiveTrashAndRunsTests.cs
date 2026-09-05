using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Documents;
using GenDoc.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Tests.Archive;

public class ArchiveTrashAndRunsTests
{
    private static readonly ArchiveFilter NoFilter = new(null, null, null, null, null, 0, 200);

    private sealed record Seeded(
        int UserId, int IntakeId, int TemplateId, int ExportTemplateId, int PackageId,
        int RecipientId, int DocumentId, int RunId);

    private static Seeded Seed(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        var user = new UserProfile { FullName = "Курсовий Офіцер", PasswordHash = "x", CreatedAt = DateTime.Now };
        ctx.Users.Add(user);
        var intake = new Intake { Number = 4, DisplayNumber = "Набір №4", Status = IntakeStatus.Active };
        ctx.Intakes.Add(intake);
        var template = new Template
        {
            Name = "Рапорт", OriginalFileName = "r.docx", Content = new byte[] { 1 },
            UploadedAt = DateTime.Now, Kind = TemplateKind.PerRecipient
        };
        ctx.Templates.Add(template);
        var export = new ExportTemplate { Name = "Залік", OriginalFileName = "z.xlsx", UploadedAt = DateTime.Now };
        ctx.ExportTemplates.Add(export);
        var package = new GenerationPackage { Name = "Зброя" };
        ctx.GenerationPackages.Add(package);
        var person = TemplateFixtures.Person(0, "ШЕВЧЕНКО", "Тарас");
        person.IntakeId = intake.Id;
        ctx.Recipients.Add(person);
        ctx.SaveChanges();

        person.IntakeId = intake.Id;
        var run = new GenerationPackageRun
        {
            GenerationPackageId = package.Id, RunAt = DateTime.Now, RunByUserId = user.Id,
            IntakeId = intake.Id, GeneratedCount = 1, SkippedCount = 2, ErrorCount = 0
        };
        ctx.GenerationPackageRuns.Add(run);
        ctx.SaveChanges();

        var doc = new GeneratedDocument
        {
            RecipientId = person.Id, TemplateId = template.Id, IntakeId = intake.Id, RunId = run.Id,
            FileName = "Набір №4/Рапорт/ШЕВЧЕНКО Тарас.docx", GeneratedAt = DateTime.Now, GeneratedByUserId = user.Id,
            Version = 1, IsCurrent = true, HasContent = true, SizeBytes = 3,
            Content = new GeneratedDocumentContent { Content = new byte[] { 1, 2, 3 } }
        };
        ctx.GeneratedDocuments.Add(doc);
        ctx.SaveChanges();

        return new Seeded(user.Id, intake.Id, template.Id, export.Id, package.Id, person.Id, doc.Id, run.Id);
    }

    private static void SoftDelete<T>(TestDb db, int id) where T : class, GenDoc.Models.Common.ISoftDeletable
    {
        using var ctx = db.Factory.CreateDbContext();
        var entity = ctx.Set<T>().IgnoreQueryFilters().First(e => EF.Property<int>(e, "Id") == id);
        entity.DeletedAt = DateTime.Now;
        ctx.SaveChanges();
    }

    private static int AddGroupDocument(TestDb db, Seeded s, int? runId = null, bool isCurrent = true, int version = 1)
    {
        using var ctx = db.Factory.CreateDbContext();
        var doc = new GeneratedGroupDocument
        {
            ExportTemplateId = s.ExportTemplateId, IntakeId = s.IntakeId, RunId = runId,
            GeneratedAt = DateTime.Now.AddMinutes(version), GeneratedByUserId = s.UserId,
            FileName = $"zalik-v{version}.xlsx", SizeBytes = 5, RecipientCount = 3,
            Version = version, IsCurrent = isCurrent, HasContent = true,
            Content = new GeneratedGroupDocumentContent { Content = new byte[] { 1, 2 } }
        };
        ctx.GeneratedGroupDocuments.Add(doc);
        ctx.SaveChanges();
        return doc.Id;
    }

    private static int AddVersion(TestDb db, Seeded s, int version, bool isCurrent, int? runId = null)
    {
        using var ctx = db.Factory.CreateDbContext();
        foreach (var current in ctx.GeneratedDocuments.Where(g => g.RecipientId == s.RecipientId && g.IsCurrent))
            current.IsCurrent = !isCurrent && current.IsCurrent;
        var doc = new GeneratedDocument
        {
            RecipientId = s.RecipientId, TemplateId = s.TemplateId, IntakeId = s.IntakeId, RunId = runId,
            FileName = $"rapport-v{version}.docx", GeneratedAt = DateTime.Now.AddMinutes(version),
            GeneratedByUserId = s.UserId, Version = version, IsCurrent = isCurrent, HasContent = true, SizeBytes = 3,
            Content = new GeneratedDocumentContent { Content = new byte[] { 9, 9, 9 } }
        };
        ctx.GeneratedDocuments.Add(doc);
        ctx.SaveChanges();
        return doc.Id;
    }

    [Fact]
    public async Task QueryGroup_TemplateInTrash_KeepsTheNameAndMarksTemplateDead()
    {
        using var db = new TestDb();
        var s = Seed(db);
        AddGroupDocument(db, s);
        SoftDelete<ExportTemplate>(db, s.ExportTemplateId);

        var rows = await TestServices.Archive(db).QueryGroupAsync(new GroupArchiveFilter(null, null, null, 0, 50));

        var row = Assert.Single(rows);
        Assert.Equal("Залік", row.TemplateName);
        Assert.False(row.TemplateAlive);
    }

    [Fact]
    public async Task AuthorInTrash_DoesNotHideVersionsOrGroupDocuments()
    {
        using var db = new TestDb();
        var s = Seed(db);
        AddGroupDocument(db, s);
        SoftDelete<UserProfile>(db, s.UserId);
        var archive = TestServices.Archive(db);

        var versions = await archive.GetVersionsAsync(s.RecipientId, s.TemplateId);
        var groupRows = await archive.QueryGroupAsync(new GroupArchiveFilter(null, null, null, 0, 50));
        var groupVersions = await archive.GetGroupVersionsAsync(s.ExportTemplateId, null, s.IntakeId);

        Assert.Equal("Курсовий Офіцер", Assert.Single(versions).Author);
        Assert.Equal("Курсовий Офіцер", Assert.Single(groupRows).Author);
        Assert.Equal("Курсовий Офіцер", Assert.Single(groupVersions).Author);
    }

    [Fact]
    public async Task RunItems_DeletedDocument_IsMarkedDeletedWithoutOpen()
    {
        using var db = new TestDb();
        var s = Seed(db);
        await TestServices.Archive(db).DeleteAsync(new[] { s.DocumentId });

        var items = await TestServices.Archive(db).GetRunItemsAsync(s.RunId);

        var item = Assert.Single(items);
        Assert.Equal("видалено", item.Status);
        Assert.False(item.HasContent);
        Assert.Null(item.DocumentId);
    }

    [Fact]
    public async Task RunItems_SupersededDocument_PointsToTheCurrentVersion()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var v2 = AddVersion(db, s, version: 2, isCurrent: true);

        var items = await TestServices.Archive(db).GetRunItemsAsync(s.RunId);

        var item = Assert.Single(items);
        Assert.Equal("є новіша в.2", item.Status);
        Assert.Equal(v2, item.DocumentId);
        Assert.False(item.IsError);
    }

    [Fact]
    public async Task RunItems_IncludeGroupDocumentsOfTheRun()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var groupId = AddGroupDocument(db, s, runId: s.RunId);

        var items = await TestServices.Archive(db).GetRunItemsAsync(s.RunId);

        var group = Assert.Single(items, i => i.IsGroup);
        Assert.Equal(groupId, group.DocumentId);
        Assert.Equal("Залік", group.TemplateName);
        Assert.Contains("3", group.Person);
        Assert.True(group.HasContent);
    }

    [Fact]
    public async Task Runs_PackageInTrash_KeepsTheNameAndFlagsIt()
    {
        using var db = new TestDb();
        var s = Seed(db);
        SoftDelete<GenerationPackage>(db, s.PackageId);

        var runs = await TestServices.Archive(db).GetRunsAsync(null, null);

        var run = Assert.Single(runs);
        Assert.Equal("Зброя", run.PackageName);
        Assert.True(run.PackageInTrash);
    }

    [Fact]
    public async Task FilterOptions_ComeFromExistingDocuments_IncludingTrashedTemplatesAndIntakes()
    {
        using var db = new TestDb();
        var s = Seed(db);
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Intakes.Add(new Intake { Number = 9, DisplayNumber = "Набір №9" });
            ctx.Templates.Add(new Template
            {
                Name = "Без документів", OriginalFileName = "n.docx", Content = new byte[] { 1 },
                UploadedAt = DateTime.Now, Kind = TemplateKind.PerRecipient
            });
            ctx.SaveChanges();
        }
        SoftDelete<Template>(db, s.TemplateId);
        SoftDelete<Intake>(db, s.IntakeId);

        var options = await TestServices.Archive(db).GetFilterOptionsAsync();

        var template = Assert.Single(options.Templates);
        Assert.Equal(s.TemplateId, template.Id);
        Assert.Contains("у кошику", template.Name);
        var trashed = Assert.Single(options.Intakes, i => i.Id == s.IntakeId);
        Assert.Contains("у кошику", trashed.Label);
        Assert.Contains(options.Intakes, i => i.Label.StartsWith("Набір №9"));
    }

    [Fact]
    public async Task Query_IntakeInTrash_StillShowsTheIntakeNumber()
    {
        using var db = new TestDb();
        var s = Seed(db);
        SoftDelete<Intake>(db, s.IntakeId);

        var rows = await TestServices.Archive(db).QueryAsync(NoFilter);

        Assert.Equal(4, Assert.Single(rows).IntakeNumber);
    }

    [Fact]
    public async Task Query_MarksStaleWhenThePersonChangedAfterGeneration()
    {
        using var db = new TestDb();
        var s = Seed(db);
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.TemplateFieldMappings.Add(new TemplateFieldMapping
            {
                TemplateId = s.TemplateId, PlaceholderTag = "{{прізвище}}",
                SourceType = MappingSourceType.Recipient, FieldName = "LastName"
            });
            ctx.SaveChanges();
            var mappings = ctx.TemplateFieldMappings.Where(m => m.TemplateId == s.TemplateId).ToList();
            var person = ctx.Recipients.Include(r => r.Unit).Include(r => r.Room).Include(r => r.OrgNode).Include(r => r.Weapons)
                .First(r => r.Id == s.RecipientId);
            ctx.GeneratedDocuments.First(g => g.Id == s.DocumentId).SourceHash =
                new DocumentHashService().ComputeSourceHash(mappings, person, null);
            ctx.SaveChanges();
        }

        var fresh = await TestServices.Archive(db).QueryAsync(NoFilter);
        Assert.False(Assert.Single(fresh).IsStale);

        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Recipients.First(r => r.Id == s.RecipientId).LastName = "ФРАНКО";
            ctx.SaveChanges();
        }

        var changed = await TestServices.Archive(db).QueryAsync(NoFilter);
        Assert.True(Assert.Single(changed).IsStale);
    }

    [Fact]
    public async Task Open_ContentHashMismatch_OpensWithAWarning()
    {
        using var db = new TestDb();
        var s = Seed(db);
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.GeneratedDocuments.First(g => g.Id == s.DocumentId).ContentHash = "DEADBEEF";
            ctx.SaveChanges();
        }

        var result = await TestServices.Archive(db).OpenAsync(s.DocumentId);

        Assert.True(result.Success);
        Assert.NotNull(result.Warning);
        Assert.Contains("контрольн", result.Warning!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Open_ContentHashMatches_NoWarning()
    {
        using var db = new TestDb();
        var s = Seed(db);
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.GeneratedDocuments.First(g => g.Id == s.DocumentId).ContentHash =
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[] { 1, 2, 3 }));
            ctx.SaveChanges();
        }

        var result = await TestServices.Archive(db).OpenAsync(s.DocumentId);

        Assert.True(result.Success);
        Assert.Null(result.Warning);
    }

    [Fact]
    public async Task DeleteAllVersions_RemovesTheWholeChain()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var v2 = AddVersion(db, s, version: 2, isCurrent: true);
        var archive = TestServices.Archive(db);

        Assert.Equal(2, await archive.CountVersionsAsync(new[] { v2 }));
        await archive.DeleteAsync(new[] { v2 }, allVersions: true);

        var rows = await archive.QueryAsync(NoFilter);
        Assert.Empty(rows);
        Assert.Equal(2, (await archive.GetDeletedDocumentsAsync()).Count);
    }

    [Fact]
    public async Task DeleteCurrentOnly_PromotesThePreviousVersion()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var v2 = AddVersion(db, s, version: 2, isCurrent: true);
        var archive = TestServices.Archive(db);

        await archive.DeleteAsync(new[] { v2 }, allVersions: false);

        Assert.Equal(1, Assert.Single(await archive.QueryAsync(NoFilter)).Version);
    }

    [Fact]
    public async Task DeletedAttachments_AppearInTrashAndCanBeRestored()
    {
        using var db = new TestDb();
        var s = Seed(db);
        int attachmentId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var attachment = new DocumentAttachment
            {
                GeneratedDocumentId = s.DocumentId, FileName = "скан.pdf", Content = new byte[] { 1 },
                SizeBytes = 1, UploadedBy = "Тест", UploadedAt = DateTime.Now
            };
            ctx.DocumentAttachments.Add(attachment);
            ctx.SaveChanges();
            attachmentId = attachment.Id;
        }
        var archive = TestServices.Archive(db);
        await archive.DeleteAttachmentAsync(attachmentId);

        var deleted = Assert.Single(await archive.GetDeletedAttachmentsAsync());
        Assert.Equal("скан.pdf", deleted.FileName);
        Assert.Contains("ШЕВЧЕНКО", deleted.Person);
        Assert.Equal("Рапорт", deleted.TemplateName);

        await archive.RestoreAttachmentAsync(attachmentId);

        Assert.Empty(await archive.GetDeletedAttachmentsAsync());
        Assert.Single(await archive.GetAttachmentsAsync(s.RecipientId, s.TemplateId));
    }

    [Fact]
    public async Task Print_AuditsOnlyWhenThePrintProcessStarted()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var audit = new FakeAuditLog();
        var temp = new FakeTempFiles { PrintStarts = false };
        var archive = new DocumentArchiveService(
            db.Factory, audit, new FakeCurrentUser(), temp, new NoOpWatermarkService(),
            new GenDoc.Services.Generation.DocumentGenerationService(), new DocumentHashService());

        var result = await archive.PrintAsync(s.DocumentId);

        Assert.False(result.Success);
        Assert.DoesNotContain(audit.Entries, e => e.StartsWith("Надруковано"));

        temp.PrintStarts = true;
        var ok = await archive.PrintAsync(s.DocumentId);

        Assert.True(ok.Success);
        Assert.Contains(audit.Entries, e => e.StartsWith("Надруковано"));
    }

    [Fact]
    public async Task DocumentActions_LogPersonTemplateAndVersion()
    {
        using var db = new TestDb();
        var s = Seed(db);
        var audit = new FakeAuditLog();
        var archive = new DocumentArchiveService(
            db.Factory, audit, new FakeCurrentUser(), new FakeTempFiles(), new NoOpWatermarkService(),
            new GenDoc.Services.Generation.DocumentGenerationService(), new DocumentHashService());

        await archive.OpenAsync(s.DocumentId);
        await archive.DeleteAsync(new[] { s.DocumentId });

        Assert.Equal(2, audit.Details.Count);
        Assert.All(audit.Details, d =>
        {
            Assert.Contains("ШЕВЧЕНКО Тарас", d);
            Assert.Contains("Рапорт · в.1", d);
        });
    }

    [Fact]
    public void TempFileName_DropsFolderSegments()
    {
        Assert.Equal("ШЕВЧЕНКО Тарас.docx", SecureTempFileService.TempFileName("Набір №4/Рапорт/ШЕВЧЕНКО Тарас.docx"));
        Assert.Equal("ШЕВЧЕНКО Тарас.docx", SecureTempFileService.TempFileName(@"Набір №4\Рапорт\ШЕВЧЕНКО Тарас.docx"));
    }
}
