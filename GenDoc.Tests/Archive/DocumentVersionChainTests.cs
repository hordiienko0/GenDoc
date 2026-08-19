using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Documents;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Tests.Archive;

// Інваріант усього архіву: для пари (людина, шаблон) серед ЖИВИХ записів
// має бути рівно один IsCurrent. Кожна операція перевіряється саме на це.
public class DocumentVersionChainTests
{
    // Створює людину, шаблон і `versions` версій документа для їхньої пари.
    // Актуальною лишається найвища версія.
    private static (int RecipientId, int TemplateId, List<int> DocumentIds) Seed(TestDb db, int versions)
    {
        using var ctx = db.Factory.CreateDbContext();

        // GeneratedDocument.GeneratedByUserId - обов'язковий FK на UserProfile.
        // Користувача з Id=1 тут не існує, поки не заведемо його самі
        // (FakeCurrentUser лише підмінює контекст виконання, у БД нічого не пише).
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
            Content = Array.Empty<byte>(), UploadedAt = DateTime.Now
        };
        ctx.Users.Add(user);
        ctx.Recipients.Add(person);
        ctx.Templates.Add(template);
        ctx.SaveChanges();

        var ids = new List<int>();
        for (var v = 1; v <= versions; v++)
        {
            var doc = new GeneratedDocument
            {
                RecipientId = person.Id,
                TemplateId = template.Id,
                GeneratedAt = DateTime.Now.AddMinutes(v),
                GeneratedByUserId = 1,
                FileName = $"rapport-v{v}.docx",
                SizeBytes = 10,
                Version = v,
                IsCurrent = v == versions,
                SourceType = DocumentSourceType.Generated,
                HasContent = true,
                Content = new GeneratedDocumentContent { Content = new byte[] { 1, 2, 3 } }
            };
            ctx.GeneratedDocuments.Add(doc);
            ctx.SaveChanges();
            ids.Add(doc.Id);
        }

        return (person.Id, template.Id, ids);
    }

    private static void AssertExactlyOneCurrent(TestDb db, int recipientId, int templateId)
    {
        using var ctx = db.Factory.CreateDbContext();
        var current = ctx.GeneratedDocuments
            .Where(g => g.RecipientId == recipientId && g.TemplateId == templateId && g.IsCurrent)
            .ToList();
        Assert.Single(current);
    }

    [Fact]
    public async Task GetVersionsAsync_ReturnsNewestFirst()
    {
        using var db = new TestDb();
        var (recipientId, templateId, _) = Seed(db, versions: 3);

        var versions = await TestServices.Archive(db).GetVersionsAsync(recipientId, templateId);

        Assert.Equal(new[] { 3, 2, 1 }, versions.Select(v => v.Version).ToArray());
        Assert.True(versions[0].IsCurrent);
        Assert.False(versions[1].IsCurrent);
    }

    [Fact]
    public async Task MakeCurrentAsync_MovesCurrentFlagToChosenVersion()
    {
        using var db = new TestDb();
        var (recipientId, templateId, ids) = Seed(db, versions: 3);

        await TestServices.Archive(db).MakeCurrentAsync(ids[0]); // версія 1

        AssertExactlyOneCurrent(db, recipientId, templateId);
        using var ctx = db.Factory.CreateDbContext();
        Assert.True(ctx.GeneratedDocuments.First(g => g.Id == ids[0]).IsCurrent);
    }

    // Видалення актуальної версії має підняти попередню живу, інакше анти-дубль
    // генерації вважатиме пару вільною і сформує документ наново.
    [Fact]
    public async Task DeleteAsync_PromotesPreviousVersionToCurrent()
    {
        using var db = new TestDb();
        var (recipientId, templateId, ids) = Seed(db, versions: 3);

        await TestServices.Archive(db).DeleteAsync(new[] { ids[2] }); // видаляємо версію 3

        AssertExactlyOneCurrent(db, recipientId, templateId);
        using var ctx = db.Factory.CreateDbContext();
        Assert.True(ctx.GeneratedDocuments.First(g => g.Id == ids[1]).IsCurrent);
        Assert.NotNull(ctx.GeneratedDocuments.IgnoreQueryFilters().First(g => g.Id == ids[2]).DeletedAt);
    }

    [Fact]
    public async Task DeleteAsync_OnlyRemainingVersion_LeavesPairWithoutCurrent()
    {
        using var db = new TestDb();
        var (recipientId, templateId, ids) = Seed(db, versions: 1);

        await TestServices.Archive(db).DeleteAsync(new[] { ids[0] });

        using var ctx = db.Factory.CreateDbContext();
        Assert.Empty(ctx.GeneratedDocuments
            .Where(g => g.RecipientId == recipientId && g.TemplateId == templateId)
            .ToList());
    }

    [Fact]
    public async Task RestoreAsync_HighestVersion_BecomesCurrentAgain()
    {
        using var db = new TestDb();
        var (recipientId, templateId, ids) = Seed(db, versions: 3);
        var service = TestServices.Archive(db);

        await service.DeleteAsync(new[] { ids[2] });
        await service.RestoreAsync(ids[2]);

        AssertExactlyOneCurrent(db, recipientId, templateId);
        using var ctx = db.Factory.CreateDbContext();
        Assert.True(ctx.GeneratedDocuments.First(g => g.Id == ids[2]).IsCurrent);
    }

    // Відновлення НЕ найвищої версії не повинно відбирати актуальність у новішої.
    [Fact]
    public async Task RestoreAsync_LowerVersion_DoesNotStealCurrentFlag()
    {
        using var db = new TestDb();
        var (recipientId, templateId, ids) = Seed(db, versions: 3);
        var service = TestServices.Archive(db);

        await service.DeleteAsync(new[] { ids[0] }); // версія 1, не актуальна
        await service.RestoreAsync(ids[0]);

        AssertExactlyOneCurrent(db, recipientId, templateId);
        using var ctx = db.Factory.CreateDbContext();
        Assert.True(ctx.GeneratedDocuments.First(g => g.Id == ids[2]).IsCurrent);
        Assert.False(ctx.GeneratedDocuments.First(g => g.Id == ids[0]).IsCurrent);
    }

    [Fact]
    public async Task UploadManualAsync_AddsVersionWithManualUploadSourceType()
    {
        using var db = new TestDb();
        var (recipientId, templateId, ids) = Seed(db, versions: 1);

        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.docx");
        await File.WriteAllBytesAsync(path, new byte[] { 9, 9, 9 });
        try
        {
            var result = await TestServices.Archive(db).UploadManualAsync(ids[0], path, "нотатка");
            Assert.True(result.Success, result.ErrorMessage);
        }
        finally
        {
            File.Delete(path);
        }

        AssertExactlyOneCurrent(db, recipientId, templateId);
        using var ctx = db.Factory.CreateDbContext();
        var newest = ctx.GeneratedDocuments
            .Where(g => g.RecipientId == recipientId && g.TemplateId == templateId)
            .OrderByDescending(g => g.Version).First();
        Assert.Equal(2, newest.Version);
        Assert.Equal(DocumentSourceType.ManualUpload, newest.SourceType);
    }

    [Fact]
    public async Task GetDeletedDocumentsAsync_ListsOnlySoftDeleted()
    {
        using var db = new TestDb();
        var (_, _, ids) = Seed(db, versions: 2);
        var service = TestServices.Archive(db);

        await service.DeleteAsync(new[] { ids[1] });

        var deleted = await service.GetDeletedDocumentsAsync();
        Assert.Single(deleted);
        Assert.Equal(2, deleted[0].Version);
    }

    // Легасі-запис без збереженого вмісту не має падати з внутрішнім
    // «Sequence contains no elements» - користувач мусить побачити пояснення.
    [Fact]
    public async Task OpenAsync_DocumentWithoutStoredContent_ReturnsFailureInsteadOfThrowing()
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
                SourceType = DocumentSourceType.Generated, HasContent = false
            };
            ctx.GeneratedDocuments.Add(doc);
            ctx.SaveChanges();
            docId = doc.Id;
        }

        var result = await TestServices.Archive(db).OpenAsync(docId);

        Assert.False(result.Success);
        Assert.Contains("не збережено в архіві", result.ErrorMessage);
    }

    // Фінальне рев'ю, Finding 6: рядок GeneratedDocument міг зникнути між тим, як
    // список відкрили, і кліком по ньому (видалення в іншому сеансі). FirstAsync
    // на батьківському рядку падав з "Sequence contains no elements" - той самий
    // сирий текст, який ця гілка мала прибрати з користувацьких повідомлень.
    [Fact]
    public async Task OpenAsync_StaleDocumentId_ReturnsFailureInsteadOfThrowing()
    {
        using var db = new TestDb();

        var result = await TestServices.Archive(db).OpenAsync(documentId: 999);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("уже відсутній в архіві", result.ErrorMessage);
    }
}
