using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Documents;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Archive;

public class ArchiveExportAndFilterTests : IDisposable
{
    private readonly string _outputFolder =
        Path.Combine(Path.GetTempPath(), $"gendoc-archive-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_outputFolder)) Directory.Delete(_outputFolder, recursive: true);
    }

    private static List<int> SeedTwoDocumentsWithSameName(TestDb db, string? orgPathA, string? orgPathB)
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

        var ids = new List<int>();
        foreach (var orgPath in new[] { orgPathA, orgPathB })
        {
            var person = new Recipient
            {
                LastName = "ШЕВЧЕНКО", FirstName = "Тарас", MiddleName = "Григорович",
                Rank = "майор", Position = "слухач", ServiceNumber = Guid.NewGuid().ToString("N")[..5]
            };
            ctx.Recipients.Add(person);
            ctx.SaveChanges();

            var doc = new GeneratedDocument
            {
                RecipientId = person.Id,
                TemplateId = template.Id,
                GeneratedAt = new DateTime(2026, 3, 15),
                GeneratedByUserId = 1,
                FileName = "rapport.docx",
                SizeBytes = 3,
                Version = 1,
                IsCurrent = true,
                SourceType = DocumentSourceType.Generated,
                OrgPathSnapshot = orgPath,
                HasContent = true,
                Content = new GeneratedDocumentContent { Content = new byte[] { 1, 2, 3 } }
            };
            ctx.GeneratedDocuments.Add(doc);
            ctx.SaveChanges();
            ids.Add(doc.Id);
        }

        return ids;
    }

    [Fact]
    public async Task SaveManyAsync_LaysDocumentsOutByOrgPathSnapshot()
    {
        using var db = new TestDb();
        var ids = SeedTwoDocumentsWithSameName(db, "Курс 1 / Взвод 2", "Курс 3");

        var (saved, errors) = await TestServices.Archive(db).SaveManyAsync(ids, _outputFolder);

        Assert.Equal(2, saved);
        Assert.Empty(errors);
        Assert.True(Directory.Exists(Path.Combine(_outputFolder, "Курс 1", "Взвод 2")));
        Assert.True(Directory.Exists(Path.Combine(_outputFolder, "Курс 3")));
    }

    [Fact]
    public async Task SaveManyAsync_NameCollisionInSameFolder_AddsNumericSuffix()
    {
        using var db = new TestDb();
        var ids = SeedTwoDocumentsWithSameName(db, "Курс 1", "Курс 1");

        var (saved, errors) = await TestServices.Archive(db).SaveManyAsync(ids, _outputFolder);

        Assert.Equal(2, saved);
        Assert.Empty(errors);
        var files = Directory.GetFiles(Path.Combine(_outputFolder, "Курс 1"));
        Assert.Equal(2, files.Length);
        Assert.Contains(files, f => Path.GetFileNameWithoutExtension(f).EndsWith("_2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SaveManyAsync_DocumentWithoutStoredContent_ReportsErrorLineInsteadOfThrowing()
    {
        using var db = new TestDb();
        int docId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var user = new UserProfile
            {
                FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now
            };
            var person = new Recipient
            {
                LastName = "БЕЗВМІСТУ", FirstName = "Іван",
                Rank = "капітан", Position = "слухач", ServiceNumber = "7"
            };
            var template = new Template
            {
                Name = "Рапорт", OriginalFileName = "r.docx",
                Content = Array.Empty<byte>(), UploadedAt = DateTime.Now
            };
            ctx.Users.Add(user);
            ctx.Recipients.Add(person);
            ctx.Templates.Add(template);
            ctx.SaveChanges();

            var doc = new GeneratedDocument
            {
                RecipientId = person.Id, TemplateId = template.Id,
                GeneratedAt = DateTime.Now, GeneratedByUserId = 1,
                FileName = "no-content.docx", SizeBytes = 0,
                Version = 1, IsCurrent = true,
                SourceType = DocumentSourceType.Generated,
                HasContent = false
            };
            ctx.GeneratedDocuments.Add(doc);
            ctx.SaveChanges();
            docId = doc.Id;
        }

        var (saved, errors) = await TestServices.Archive(db).SaveManyAsync(new[] { docId }, _outputFolder);

        Assert.Equal(0, saved);
        Assert.Single(errors);
        Assert.Contains("не збережено в архіві", errors[0]);
    }

    [Fact]
    public async Task RegenerateAsync_DeletedTemplate_ReturnsHumanReadableFailure()
    {
        using var db = new TestDb();
        int docId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var user = new UserProfile
            {
                FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now
            };
            var person = new Recipient
            {
                LastName = "ШЕВЧЕНКО", FirstName = "Тарас",
                Rank = "майор", Position = "слухач", ServiceNumber = "1"
            };
            var template = new Template
            {
                Name = "Рапорт", OriginalFileName = "r.docx",
                Content = Array.Empty<byte>(), UploadedAt = DateTime.Now,
                DeletedAt = DateTime.Now
            };
            ctx.Users.Add(user);
            ctx.Recipients.Add(person);
            ctx.Templates.Add(template);
            ctx.SaveChanges();

            var doc = new GeneratedDocument
            {
                RecipientId = person.Id, TemplateId = template.Id,
                GeneratedAt = DateTime.Now, GeneratedByUserId = 1,
                FileName = "r.docx", SizeBytes = 1, Version = 1, IsCurrent = true,
                SourceType = DocumentSourceType.Generated, HasContent = true,
                Content = new GeneratedDocumentContent { Content = new byte[] { 1 } }
            };
            ctx.GeneratedDocuments.Add(doc);
            ctx.SaveChanges();
            docId = doc.Id;
        }

        var result = await TestServices.Archive(db).RegenerateAsync(docId, new Dictionary<string, string>());

        Assert.False(result.Success);
        Assert.Equal("Шаблон видалено - перегенерація неможлива", result.ErrorMessage);
    }

    [Fact]
    public async Task QueryAsync_FiltersByYearAndTemplate()
    {
        using var db = new TestDb();
        int templateA, templateB;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var user = new UserProfile
            {
                FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now
            };
            var person = new Recipient
            {
                LastName = "ШЕВЧЕНКО", FirstName = "Тарас",
                Rank = "майор", Position = "слухач", ServiceNumber = "1"
            };
            var a = new Template { Name = "А", OriginalFileName = "a.docx", Content = Array.Empty<byte>(), UploadedAt = DateTime.Now };
            var b = new Template { Name = "Б", OriginalFileName = "b.docx", Content = Array.Empty<byte>(), UploadedAt = DateTime.Now };
            ctx.Users.Add(user);
            ctx.Recipients.Add(person);
            ctx.Templates.AddRange(a, b);
            ctx.SaveChanges();
            templateA = a.Id;
            templateB = b.Id;

            ctx.GeneratedDocuments.AddRange(
                new GeneratedDocument
                {
                    RecipientId = person.Id, TemplateId = a.Id,
                    GeneratedAt = new DateTime(2025, 5, 1), GeneratedByUserId = 1,
                    FileName = "a-2025.docx", SizeBytes = 1, Version = 1, IsCurrent = true,
                    SourceType = DocumentSourceType.Generated, HasContent = true
                },
                new GeneratedDocument
                {
                    RecipientId = person.Id, TemplateId = b.Id,
                    GeneratedAt = new DateTime(2026, 5, 1), GeneratedByUserId = 1,
                    FileName = "b-2026.docx", SizeBytes = 1, Version = 1, IsCurrent = true,
                    SourceType = DocumentSourceType.Generated, HasContent = true
                });
            ctx.SaveChanges();
        }

        var service = TestServices.Archive(db);

        var byYear = await service.QueryAsync(new ArchiveFilter(null, null, null, null, 2026, 0, 50));
        Assert.Equal("b-2026.docx", Assert.Single(byYear).FileName);

        var byTemplate = await service.QueryAsync(new ArchiveFilter(null, templateA, null, null, null, 0, 50));
        Assert.Equal("a-2025.docx", Assert.Single(byTemplate).FileName);

        var stats = await service.GetStatsAsync(new ArchiveFilter(null, templateB, null, null, null, 0, 50));
        Assert.Equal(1, stats.Count);
    }
}
