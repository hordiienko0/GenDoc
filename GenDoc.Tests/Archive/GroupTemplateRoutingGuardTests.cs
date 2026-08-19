using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Tests.Archive;

// Template.Kind == Group, потрапивши в один із двох одноосібних шляхів генерації
// (перегенерація з архіву, добудова відсутнього документа з матриці комплектності),
// раніше мовчки віддавався в GenerateOne - і той стирав маркер повторюваного блоку,
// видаючи документ без списку людей і без жодного попередження. Обидва тести тут
// перевіряють, що новий шар - перевірка Kind ПЕРЕД зверненням до GenerateOne - ловить
// це раніше і повертає користувачу зрозуміле пояснення замість спроби згенерувати.
public class GroupTemplateRoutingGuardTests
{
    // GeneratedDocument.GeneratedByUserId - обов'язковий FK на UserProfile.
    // Той самий трюк, що в DocumentVersionChainTests/GroupDocumentArchiveTests:
    // заводимо UserProfile без явного Id і даємо SQLite призначити 1.
    private static void SeedUser(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        var user = new UserProfile
        {
            FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now
        };
        ctx.Users.Add(user);
        ctx.SaveChanges();
    }

    [Fact]
    public async Task RegenerateAsync_GroupTemplate_ReturnsFailureNamingTemplate_WithoutCallingGenerateOne()
    {
        using var db = new TestDb();
        SeedUser(db);

        int docId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var person = new Recipient
            {
                LastName = "ШЕВЧЕНКО", FirstName = "Тарас",
                Rank = "майор", Position = "слухач", ServiceNumber = "1"
            };
            var template = new Template
            {
                Name = "Список слухачів", OriginalFileName = "list.docx",
                Content = Array.Empty<byte>(), UploadedAt = DateTime.Now,
                Kind = TemplateKind.Group
            };
            ctx.Recipients.Add(person);
            ctx.Templates.Add(template);
            ctx.SaveChanges();

            var doc = new GeneratedDocument
            {
                RecipientId = person.Id, TemplateId = template.Id,
                GeneratedAt = DateTime.Now, GeneratedByUserId = 1,
                FileName = "list.docx", SizeBytes = 1, Version = 1, IsCurrent = true,
                SourceType = DocumentSourceType.Generated, HasContent = true,
                Content = new GeneratedDocumentContent { Content = new byte[] { 1 } }
            };
            ctx.GeneratedDocuments.Add(doc);
            ctx.SaveChanges();
            docId = doc.Id;
        }

        var result = await TestServices.Archive(db).RegenerateAsync(docId, new Dictionary<string, string>());

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("Список слухачів", result.ErrorMessage);

        // Жодної нової версії документа не додано - відмова сталась ДО генерації,
        // лишився рівно один запис (той, що засіяли вище).
        using var check = db.Factory.CreateDbContext();
        Assert.Single(check.GeneratedDocuments.IgnoreQueryFilters());
    }

    [Fact]
    public async Task GenerateForPairAsync_GroupTemplate_ReturnsFailureNamingTemplate_WithoutCallingGenerateOne()
    {
        using var db = new TestDb();
        SeedUser(db);

        int recipientId, templateId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var person = new Recipient
            {
                LastName = "ПЕТРЕНКО", FirstName = "Петро",
                Rank = "капітан", Position = "слухач", ServiceNumber = "2"
            };
            var template = new Template
            {
                Name = "Відомість видачі", OriginalFileName = "roster.docx",
                Content = Array.Empty<byte>(), UploadedAt = DateTime.Now,
                Kind = TemplateKind.Group
            };
            ctx.Recipients.Add(person);
            ctx.Templates.Add(template);
            ctx.SaveChanges();
            recipientId = person.Id;
            templateId = template.Id;
        }

        var result = await TestServices.Completeness(db)
            .GenerateForPairAsync(recipientId, templateId, new Dictionary<string, string>());

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("Відомість видачі", result.ErrorMessage);

        // Жодного документа не з'явилось у БД - відмова сталась ДО генерації.
        using var check = db.Factory.CreateDbContext();
        Assert.Empty(check.GeneratedDocuments.Where(g => g.RecipientId == recipientId && g.TemplateId == templateId));
    }
}
