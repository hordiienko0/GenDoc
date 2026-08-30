using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Templates;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Templates;

/// <summary>
/// DeletePackage ставить пакету DeletedAt, але рядки GenerationPackageTemplates
/// не чіпає. TemplateService.Delete перевіряє використання проєкцією
/// pt.GenerationPackage!.Name, до якої застосовується глобальний фільтр
/// м'якого видалення - для видаленого пакета вона повертає null, тобто список
/// «використовується в пакетах» стає списком з одного null.
///
/// Наслідок: створили пакет, додали шаблон, видалили пакет - і шаблон видалити
/// вже неможливо НІКОЛИ, а повідомлення читається як «Неможливо видалити:
/// шаблон використовується в пакетах: .» з порожнім переліком, тобто без
/// жодної підказки, що робити (аудит 2026-08-28).
/// </summary>
public class DeleteTemplateAfterPackageRemovedTests
{
    private static (TemplateService Templates, int TemplateId, int PackageId) Arrange(TestDb db)
    {
        int templateId, packageId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var template = new Template
            {
                Name = "Акт", OriginalFileName = "a.docx", Content = new byte[] { 1 },
                UploadedAt = DateTime.Now, Kind = TemplateKind.PerRecipient
            };
            ctx.Templates.Add(template);
            ctx.SaveChanges();
            templateId = template.Id;

            var package = new GenerationPackage { Name = "Зброя" };
            package.Templates.Add(new GenerationPackageTemplate { TemplateId = templateId, SortOrder = 0 });
            ctx.GenerationPackages.Add(package);
            ctx.SaveChanges();
            packageId = package.Id;
        }

        return (new TemplateService(db.Factory, new FakeAuditLog(), new FakeCurrentUser()), templateId, packageId);
    }

    [Fact]
    public void Delete_WhileTheLivePackageUsesIt_IsBlockedAndNamesThePackage()
    {
        using var db = new TestDb();
        var (templates, templateId, _) = Arrange(db);

        var (success, error) = templates.Delete(templateId);

        Assert.False(success);
        Assert.Contains("Зброя", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Delete_AfterThePackageWentToTrash_IsAllowed()
    {
        using var db = new TestDb();
        var (templates, templateId, packageId) = Arrange(db);

        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.GenerationPackages.First(p => p.Id == packageId).DeletedAt = DateTime.Now;
            ctx.SaveChanges();
        }

        var (success, error) = templates.Delete(templateId);

        Assert.True(success, error);
    }
}
