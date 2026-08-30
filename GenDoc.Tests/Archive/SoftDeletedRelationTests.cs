using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Archive;

/// <summary>
/// Документ переживає і людину, і шаблон: в архіві їх навмисно видно після
/// того, як картку чи шаблон прибрали в кошик. Але зв'язки
/// GeneratedDocument→Recipient і →Template обов'язкові (Id не nullable), тож
/// EF будує INNER JOIN, глобальний фільтр м'якого видалення прибирає рядок, і
/// FirstAsync падає з сирим «Sequence contains no elements».
///
/// ApplyFilter і головна проєкція це вже обходять (підзапит з
/// IgnoreQueryFilters), а SaveManyAsync, RegenerateAsync і GetCurrentRowAsync -
/// ні, хоча RecordGoneMessage заведений саме під такі випадки
/// (аудит 2026-08-28).
/// </summary>
public class SoftDeletedRelationTests
{
    private static (int DocId, int RecipientId, int TemplateId) Seed(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });

        var person = TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас");
        ctx.Recipients.Add(person);

        var template = new Template
        {
            Name = "Акт", OriginalFileName = "a.docx", Content = new byte[] { 1 },
            UploadedAt = DateTime.Now, Kind = TemplateKind.PerRecipient
        };
        ctx.Templates.Add(template);
        ctx.SaveChanges();

        var doc = new GeneratedDocument
        {
            RecipientId = person.Id, TemplateId = template.Id,
            FileName = "a.docx", GeneratedAt = DateTime.Now, GeneratedByUserId = 1,
            Version = 1, IsCurrent = true, HasContent = true
        };
        ctx.GeneratedDocuments.Add(doc);
        ctx.SaveChanges();

        ctx.GeneratedDocumentContents.Add(new GeneratedDocumentContent
        {
            GeneratedDocumentId = doc.Id, Content = new byte[] { 1, 2, 3 }
        });
        ctx.SaveChanges();

        return (doc.Id, person.Id, template.Id);
    }

    private static void SoftDeleteTemplate(TestDb db, int templateId)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Templates.First(t => t.Id == templateId).DeletedAt = DateTime.Now;
        ctx.SaveChanges();
    }

    private static void SoftDeleteRecipient(TestDb db, int recipientId)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Recipients.First(r => r.Id == recipientId).DeletedAt = DateTime.Now;
        ctx.SaveChanges();
    }

    [Fact]
    public async Task SaveManyAsync_TemplateInTrash_StillExportsTheDocument()
    {
        using var db = new TestDb();
        var (docId, _, templateId) = Seed(db);
        SoftDeleteTemplate(db, templateId);

        var folder = Path.Combine(Path.GetTempPath(), "gendoc-soft-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var result = await TestServices.Archive(db).SaveManyAsync(new[] { docId }, folder);

            Assert.Empty(result.Errors);
            Assert.Equal(1, result.Saved);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task SaveManyAsync_RecipientInTrash_StillExportsTheDocument()
    {
        using var db = new TestDb();
        var (docId, recipientId, _) = Seed(db);
        SoftDeleteRecipient(db, recipientId);

        var folder = Path.Combine(Path.GetTempPath(), "gendoc-soft-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var result = await TestServices.Archive(db).SaveManyAsync(new[] { docId }, folder);

            Assert.Empty(result.Errors);
            Assert.Equal(1, result.Saved);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    // Перегенерація для видаленого шаблону НЕ повинна виконуватись - але й падати
    // сирим винятком теж: користувач має побачити задумане повідомлення. Раніше
    // гілка «Шаблон видалено» була недосяжна, бо FirstAsync кидав раніше за неї.
    [Fact]
    public async Task RegenerateAsync_TemplateInTrash_ReturnsMessageInsteadOfThrowing()
    {
        using var db = new TestDb();
        var (docId, _, templateId) = Seed(db);
        SoftDeleteTemplate(db, templateId);

        var result = await TestServices.Archive(db).RegenerateAsync(docId, new Dictionary<string, string>());

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    // Після дії над рядком в'ю-модель перечитує його через GetCurrentRowAsync;
    // null означав «нічого не оновлюємо», і таблиця тихо показувала стару версію.
    [Fact]
    public async Task GetCurrentRowAsync_TemplateInTrash_StillReturnsTheRow()
    {
        using var db = new TestDb();
        var (_, recipientId, templateId) = Seed(db);
        SoftDeleteTemplate(db, templateId);

        var row = await TestServices.Archive(db).GetCurrentRowAsync(recipientId, templateId);

        Assert.NotNull(row);
        Assert.False(row!.TemplateAlive);
    }

    [Fact]
    public async Task GetCurrentRowAsync_RecipientInTrash_StillReturnsTheRow()
    {
        using var db = new TestDb();
        var (_, recipientId, templateId) = Seed(db);
        SoftDeleteRecipient(db, recipientId);

        var row = await TestServices.Archive(db).GetCurrentRowAsync(recipientId, templateId);

        Assert.NotNull(row);
        Assert.Equal("ШЕВЧЕНКО", row!.LastName);
    }
}
