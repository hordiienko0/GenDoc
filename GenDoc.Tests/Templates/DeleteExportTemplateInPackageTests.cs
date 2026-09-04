using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Templates;

public class DeleteExportTemplateInPackageTests
{
    private static (ExportTemplateService Service, int TemplateId, int PackageId) Arrange(TestDb db)
    {
        int templateId, packageId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var template = new ExportTemplate
            {
                Name = "Залік", OriginalFileName = "z.xlsx", Content = new byte[] { 1 }, UploadedAt = DateTime.Now
            };
            ctx.ExportTemplates.Add(template);
            ctx.SaveChanges();
            templateId = template.Id;

            var package = new GenerationPackage { Name = "Зброя" };
            package.ExportTemplates.Add(new GenerationPackageExportTemplate { ExportTemplateId = templateId });
            ctx.GenerationPackages.Add(package);
            ctx.SaveChanges();
            packageId = package.Id;
        }

        return (new ExportTemplateService(db.Factory, new FakeAuditLog(), new FakeCurrentUser()), templateId, packageId);
    }

    private static bool IsAlive(TestDb db, int templateId)
    {
        using var ctx = db.Factory.CreateDbContext();
        return ctx.ExportTemplates.Any(t => t.Id == templateId);
    }

    [Fact]
    public void Delete_WhileTheLivePackageUsesIt_IsBlockedAndNamesThePackage()
    {
        using var db = new TestDb();
        var (service, templateId, _) = Arrange(db);

        var (success, error) = service.Delete(templateId);

        Assert.False(success);
        Assert.Contains("Зброя", error!, StringComparison.Ordinal);
        Assert.True(IsAlive(db, templateId));
    }

    [Fact]
    public void Delete_AfterThePackageWentToTrash_IsAllowed()
    {
        using var db = new TestDb();
        var (service, templateId, packageId) = Arrange(db);

        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.GenerationPackages.First(p => p.Id == packageId).DeletedAt = DateTime.Now;
            ctx.SaveChanges();
        }

        var (success, error) = service.Delete(templateId);

        Assert.True(success, error);
        Assert.False(IsAlive(db, templateId));
    }
}
