using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Archive;

public class SaveGroupAsPathTests
{
    private static int SeedGroupDocument(TestDb db, string fileName)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
        var template = new Template
        {
            Name = "Залік Додаток 8", OriginalFileName = "z.xlsx", Content = new byte[] { 1 },
            UploadedAt = DateTime.Now, Kind = TemplateKind.Group
        };
        ctx.Templates.Add(template);
        ctx.SaveChanges();

        var doc = new GeneratedGroupDocument
        {
            TemplateId = template.Id, GeneratedAt = DateTime.Now, GeneratedByUserId = 1,
            FileName = fileName, Version = 1, IsCurrent = true, HasContent = true, RecipientCount = 3
        };
        ctx.GeneratedGroupDocuments.Add(doc);
        ctx.SaveChanges();

        ctx.GeneratedGroupDocumentContents.Add(new GeneratedGroupDocumentContent
        {
            GeneratedGroupDocumentId = doc.Id, Content = new byte[] { 1, 2, 3 }
        });
        ctx.SaveChanges();
        return doc.Id;
    }

    [Fact]
    public async Task SaveGroupAsAsync_TargetInsideMissingSubfolder_CreatesItAndWritesFile()
    {
        using var db = new TestDb();
        var relative = Path.Combine("Спільні", "Залік Додаток 8", "2026-08-06.xlsx");
        var docId = SeedGroupDocument(db, relative);

        var folder = Path.Combine(Path.GetTempPath(), "gendoc-groupsave-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var target = Path.Combine(folder, relative);

            var result = await TestServices.Archive(db).SaveGroupAsAsync(docId, target);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(File.Exists(target), "Файл не записано - підтеку не створено.");
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(target));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void SuggestedFileName_IsFileNameOnly()
    {
        var relative = Path.Combine("Набір №15", "Акт", "2026-08-14", "КОВАЛЬЧУК В.Б..docx");

        Assert.Equal("КОВАЛЬЧУК В.Б..docx", Path.GetFileName(relative));
    }
}
