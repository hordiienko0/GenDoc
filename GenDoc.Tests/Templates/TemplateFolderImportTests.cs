using GenDoc.Services.Templates;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Templates;

public class TemplateFolderImportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"gendoc-folder-{Guid.NewGuid():N}");

    public TemplateFolderImportTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string Copy(string sourcePath, string relativePath)
    {
        var target = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(sourcePath, target, overwrite: true);
        return target;
    }

    private static TemplateFolderImportService Service(TestDb db) => new(
        db.Factory,
        new TemplateService(db.Factory, new FakeAuditLog(), new FakeCurrentUser()),
        new GenDoc.Services.ExportTemplateService(db.Factory, new FakeAuditLog(), new FakeCurrentUser()),
        TestServices.Generation(db));

    [Fact]
    public void Folder_WithoutSubfolders_BecomesOnePackage()
    {
        using var db = new TestDb();
        Copy(TemplateFixtures.RaportGroupDocx, Path.Combine("Котлове забезпечення", "Рапорт груповий.docx"));
        Copy(TemplateFixtures.DopuskXlsx, Path.Combine("Котлове забезпечення", "Відомість.xlsx"));

        var result = Service(db).ImportFolder(Path.Combine(_root, "Котлове забезпечення"));

        Assert.True(result.Success, result.ErrorMessage);
        var package = Assert.Single(result.Packages);
        Assert.Equal("Котлове забезпечення", package.Name);
        Assert.Equal(1, package.TemplateCount);
        Assert.Equal(1, package.ExportTemplateCount);
        Assert.Empty(result.Failures);

        using var ctx = db.Factory.CreateDbContext();
        var saved = Assert.Single(ctx.GenerationPackages);
        Assert.Equal("Котлове забезпечення", saved.Name);
    }

    [Fact]
    public void PerRecipientTemplates_AreUploadedButStayOutOfThePackage()
    {
        using var db = new TestDb();
        Copy(TemplateFixtures.RaportGroupDocx, Path.Combine("Котлове", "Рапорт груповий.docx"));
        Copy(TemplateFixtures.RaportIndividualDocx, Path.Combine("Котлове", "Рапорт персональний.docx"));

        var result = Service(db).ImportFolder(Path.Combine(_root, "Котлове"));

        var package = Assert.Single(result.Packages);
        Assert.Equal(1, package.TemplateCount);
        Assert.Equal(1, package.PerRecipientCount);

        using var ctx = db.Factory.CreateDbContext();
        Assert.Equal(2, ctx.Templates.Count());
        var saved = Assert.Single(ctx.GenerationPackages);
        var linked = Assert.Single(ctx.GenerationPackageTemplates.Where(t => t.GenerationPackageId == saved.Id));
        Assert.Equal(
            GenDoc.Models.Enums.TemplateKind.Group,
            ctx.Templates.First(t => t.Id == linked.TemplateId).Kind);
    }

    [Fact]
    public void FolderOfOnlyPerRecipientTemplates_MakesNoPackage()
    {
        using var db = new TestDb();
        Copy(TemplateFixtures.RaportIndividualDocx, Path.Combine("Рапорти", "Рапорт.docx"));

        var result = Service(db).ImportFolder(Path.Combine(_root, "Рапорти"));

        Assert.True(result.Success, result.ErrorMessage);
        var package = Assert.Single(result.Packages);
        Assert.Equal(0, package.TemplateCount);
        Assert.Equal(1, package.PerRecipientCount);

        using var ctx = db.Factory.CreateDbContext();
        Assert.Single(ctx.Templates);
        Assert.Empty(ctx.GenerationPackages);
    }

    [Fact]
    public void EachSubfolder_BecomesItsOwnPackage()
    {
        using var db = new TestDb();
        Copy(TemplateFixtures.RaportGroupDocx, Path.Combine("комплекти", "Котлове", "Рапорт.docx"));
        Copy(TemplateFixtures.RaportGroupDocx, Path.Combine("комплекти", "Стрільби", "Витяг.docx"));
        Copy(TemplateFixtures.ZalikXlsx, Path.Combine("комплекти", "Стрільби", "Залік.xlsx"));

        var result = Service(db).ImportFolder(Path.Combine(_root, "комплекти"));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(new[] { "Котлове", "Стрільби" }, result.Packages.Select(p => p.Name).ToArray());
        Assert.Equal(new[] { 1, 1 }, result.Packages.Select(p => p.TemplateCount).ToArray());
        Assert.Equal(new[] { 0, 1 }, result.Packages.Select(p => p.ExportTemplateCount).ToArray());
    }

    [Fact]
    public void AlreadyUploadedTemplate_IsReusedInsteadOfDuplicated()
    {
        using var db = new TestDb();
        Copy(TemplateFixtures.RaportGroupDocx, Path.Combine("Котлове", "Рапорт.docx"));
        var service = Service(db);

        service.ImportFolder(Path.Combine(_root, "Котлове"));
        var second = service.ImportFolder(Path.Combine(_root, "Котлове"));

        Assert.True(second.Success, second.ErrorMessage);
        Assert.Equal(1, second.Packages.Single().ReusedCount);
        Assert.Equal(1, second.Packages.Single().TemplateCount);

        using var ctx = db.Factory.CreateDbContext();
        Assert.Single(ctx.Templates);
        Assert.Equal(2, ctx.GenerationPackages.Count());
        Assert.Equal(
            new[] { "Котлове", "Котлове (2)" },
            ctx.GenerationPackages.Select(p => p.Name).OrderBy(n => n).ToArray());
    }

    [Fact]
    public void BrokenFile_IsReportedAndTheRestStillImports()
    {
        using var db = new TestDb();
        Copy(TemplateFixtures.RaportGroupDocx, Path.Combine("Комплект", "Рапорт.docx"));
        File.WriteAllText(Path.Combine(_root, "Комплект", "Зламаний.docx"), "не документ Word");

        var result = Service(db).ImportFolder(Path.Combine(_root, "Комплект"));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(1, result.Packages.Single().TemplateCount);
        var failure = Assert.Single(result.Failures);
        Assert.Contains("Зламаний.docx", failure);
    }

    [Fact]
    public void FolderWithoutTemplates_IsReportedAsAnError()
    {
        using var db = new TestDb();
        Directory.CreateDirectory(Path.Combine(_root, "Порожня"));
        File.WriteAllText(Path.Combine(_root, "Порожня", "нотатка.txt"), "текст");

        var result = Service(db).ImportFolder(Path.Combine(_root, "Порожня"));

        Assert.False(result.Success);
        Assert.Contains(".docx", result.ErrorMessage);
        Assert.Empty(result.Packages);
    }

    [Fact]
    public void MissingFolder_IsReportedAsAnError()
    {
        using var db = new TestDb();

        var result = Service(db).ImportFolder(Path.Combine(_root, "нема такої"));

        Assert.False(result.Success);
        Assert.Contains("не знайдено", result.ErrorMessage);
    }

    [Fact]
    public void Summary_NamesEveryPackageAndEveryFailure()
    {
        var result = new FolderImportResult(
            true, null,
            new[]
            {
                new FolderImportPackage("Стрільби", 9, 3, 0, 4),
                new FolderImportPackage("Котлове (2)", 5, 0, 5)
            },
            new[] { "Зламаний.docx: Файл не є документом Word (.docx)." });

        var text = GenDoc.ViewModels.Templates.TemplatesViewModel.BuildFolderImportSummary(result);

        Assert.Contains("Зібрано пакетів генерації: 2", text);
        Assert.Contains("Стрільби - групових шаблонів: 9, відомостей: 3, персональних поза пакетом: 4", text);
        Assert.Contains("Котлове (2) - групових шаблонів: 5, вже були завантажені: 5", text);
        Assert.Contains("Не вдалося завантажити:", text);
        Assert.Contains("Зламаний.docx", text);
        Assert.Contains("Персональні шаблони завантажені", text);
    }

    [Fact]
    public void Summary_WithoutPersonalTemplates_DoesNotTalkAboutThem()
    {
        var result = new FolderImportResult(
            true, null, new[] { new FolderImportPackage("Стрільби", 9, 3, 0) }, Array.Empty<string>());

        var text = GenDoc.ViewModels.Templates.TemplatesViewModel.BuildFolderImportSummary(result);

        Assert.DoesNotContain("Персональні шаблони", text);
    }
}
