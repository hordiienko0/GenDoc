using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Completeness;

namespace GenDoc.Tests.Completeness;

// Відтворення живої вади 2026-08-24: «⚙ Вимоги» на «Комплектності» не відкриває
// діалог. Ініціалізація в'ю-моделі має проходити на пакеті з персональним,
// груповим і Excel-шаблоном - як у робочій базі.
public class PackageRequirementsInitTests
{
    [Fact]
    public async Task InitializeAsync_LoadsRowsForMixedPackage()
    {
        using var db = new TestDb();
        int packageId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
            var personal = new Template { Name = "Рапорт ІНДИВІДУАЛЬНИЙ", OriginalFileName = "a.docx", Content = new byte[] { 1 }, UploadedAt = DateTime.Now, Kind = TemplateKind.PerRecipient };
            var group = new Template { Name = "Рапорт ГРУПОВИЙ", OriginalFileName = "b.docx", Content = new byte[] { 1 }, UploadedAt = DateTime.Now, Kind = TemplateKind.Group };
            ctx.Templates.AddRange(personal, group);
            var export = new ExportTemplate { Name = "Відомість", OriginalFileName = "v.xlsx", Content = new byte[] { 1 }, UploadedAt = DateTime.Now, UsesPlaceholders = true };
            ctx.ExportTemplates.Add(export);
            ctx.SaveChanges();

            var package = new GenerationPackage { Name = "Котлове" };
            package.Templates.Add(new GenerationPackageTemplate { TemplateId = personal.Id, SortOrder = 0 });
            package.Templates.Add(new GenerationPackageTemplate { TemplateId = group.Id, SortOrder = 1 });
            ctx.GenerationPackages.Add(package);
            ctx.SaveChanges();
            ctx.GenerationPackageExportTemplates.Add(new GenerationPackageExportTemplate
            {
                GenerationPackageId = package.Id, ExportTemplateId = export.Id, SortOrder = 0
            });
            ctx.SaveChanges();
            packageId = package.Id;
        }

        var vm = new PackageRequirementsViewModel(
            TestServices.Completeness(db), TestServices.Generation(db), db.Factory);

        await vm.InitializeAsync(packageId, previewIntakeId: 4);

        Assert.Equal(2, vm.Rows.Count);
        Assert.Single(vm.ExportRows);
        Assert.True(vm.CanSave);
    }
}
