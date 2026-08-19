using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Documents;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Archive;

// Вибір гілки в дереві - це ЩЕ ОДИН фільтр над тим самим списком, а не заміна
// списку. Тому решта фільтрів, колонки, версії й підвантаження лишаються як є,
// а гілка додає до запиту префікс збереженого шляху.
public class ArchiveFolderFilterTests
{
    private static void Seed(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();

        ctx.Users.Add(new UserProfile { Id = 1, FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now });
        ctx.Templates.Add(new Template
        {
            Id = 1, Name = "Акт", OriginalFileName = "akt.docx",
            Content = Array.Empty<byte>(), UploadedAt = DateTime.Now
        });
        ctx.Recipients.Add(new Recipient { Id = 1, LastName = "Ковальчук", FirstName = "Василь" });
        ctx.SaveChanges();

        void Add(int id, string fileName) => ctx.GeneratedDocuments.Add(new GeneratedDocument
        {
            Id = id,
            RecipientId = 1,
            TemplateId = 1,
            GeneratedAt = DateTime.Now,
            GeneratedByUserId = 1,
            FileName = fileName,
            Version = 1,
            IsCurrent = true,
            HasContent = true,
            SourceType = DocumentSourceType.Generated
        });

        Add(1, @"Набір №15\Акти\2026-08-17\КОВАЛЬЧУК В. Б..docx");
        Add(2, @"Набір №15\Довідки\2026-08-15\КОВАЛЬЧУК В. Б..docx");
        Add(3, @"Набір №14\Акти\2026-08-10\КОВАЛЬЧУК В. Б..docx");
        Add(4, "Старий документ.docx");
        ctx.SaveChanges();
    }

    private static ArchiveFilter FolderFilter(string? folder) =>
        new(null, null, null, null, null, Skip: 0, Take: 50, FolderPath: folder);

    [Fact]
    public async Task QueryAsync_WithoutFolder_ReturnsEverything()
    {
        using var db = new TestDb();
        Seed(db);

        var rows = await TestServices.Archive(db).QueryAsync(FolderFilter(null));

        Assert.Equal(4, rows.Count);
    }

    [Fact]
    public async Task QueryAsync_WithFolder_ReturnsOnlyDocumentsUnderThatBranch()
    {
        using var db = new TestDb();
        Seed(db);

        var rows = await TestServices.Archive(db).QueryAsync(FolderFilter("Набір №15"));

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.StartsWith(@"Набір №15\", r.FileName, StringComparison.Ordinal));
    }

    // Глибша гілка звужує сильніше - і не чіпає однойменну гілку в іншому наборі.
    [Fact]
    public async Task QueryAsync_WithNestedFolder_DoesNotLeakTheSameNameFromAnotherIntake()
    {
        using var db = new TestDb();
        Seed(db);

        var rows = await TestServices.Archive(db).QueryAsync(FolderFilter(@"Набір №15\Акти"));

        var row = Assert.Single(rows);
        Assert.Equal(1, row.Id);
    }

    // «Без розкладки» - записи до переходу на папки, у них шляху немає взагалі.
    // Вони мусять бути досяжні, а не випадати зі списку назавжди.
    [Fact]
    public async Task QueryAsync_UnsortedFolder_ReturnsRecordsThatHaveNoPath()
    {
        using var db = new TestDb();
        Seed(db);

        var rows = await TestServices.Archive(db).QueryAsync(
            FolderFilter(ArchiveFolderTree.UnsortedFolder));

        var row = Assert.Single(rows);
        Assert.Equal("Старий документ.docx", row.FileName);
    }

    // Дерево будується з УСІХ документів, що проходять решту фільтрів, але без
    // урахування самої обраної гілки - інакше клік по гілці обрізав би дерево
    // до неї самої, і повернутися вгору стало б нікуди.
    [Fact]
    public async Task GetFolderTreeAsync_IgnoresTheSelectedBranchSoTheTreeStaysWhole()
    {
        using var db = new TestDb();
        Seed(db);

        var tree = await TestServices.Archive(db).GetFolderTreeAsync(
            FolderFilter(@"Набір №15\Акти"));

        Assert.Equal(3, tree.Count);
        Assert.Contains(tree, n => n.Name == "Набір №15");
        Assert.Contains(tree, n => n.Name == "Набір №14");
        Assert.Contains(tree, n => n.Name == ArchiveFolderTree.UnsortedFolder);
    }
}
