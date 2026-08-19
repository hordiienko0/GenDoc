using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Documents;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Archive;

// 2.5: «Друк» - ті самі байти документа, що й «Відкрити», але через shell-verb print.
public class ArchivePrintTests
{
    [Fact]
    public async Task Print_SendsDocumentBytesToShellPrint()
    {
        using var db = new TestDb();
        int docId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
            var person = TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас");
            var template = new Template { Name = "Рапорт", OriginalFileName = "a.docx", Content = new byte[] { 1 }, UploadedAt = DateTime.Now, Kind = TemplateKind.PerRecipient };
            ctx.Recipients.Add(person); ctx.Templates.Add(template); ctx.SaveChanges();
            var doc = new GeneratedDocument
            {
                RecipientId = person.Id, TemplateId = template.Id, GeneratedAt = DateTime.Now, GeneratedByUserId = 1,
                FileName = "ШЕВЧЕНКО Тарас Рапорт.docx", Version = 1, IsCurrent = true, HasContent = true,
                SourceType = DocumentSourceType.Generated, Content = new GeneratedDocumentContent { Content = new byte[] { 1, 2, 3 } }
            };
            ctx.GeneratedDocuments.Add(doc); ctx.SaveChanges();
            docId = doc.Id;
        }
        var temp = new FakeTempFiles();
        var service = new DocumentArchiveService(db.Factory, new FakeAuditLog(), new FakeCurrentUser(), temp,
            new NoOpWatermarkService(), new DocumentGenerationService(), new DocumentHashService());

        var result = await service.PrintAsync(docId);

        Assert.True(result.Success);
        var printed = Assert.Single(temp.Printed);
        Assert.Equal("ШЕВЧЕНКО Тарас Рапорт.docx", printed.FileName);
        Assert.Equal(new byte[] { 1, 2, 3 }, printed.Content);
    }
}
