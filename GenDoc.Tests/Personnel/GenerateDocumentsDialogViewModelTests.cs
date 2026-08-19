using GenDoc.ViewModels.Personnel;

namespace GenDoc.Tests.Personnel;

public class GenerateDocumentsDialogViewModelTests
{
    [Fact]
    public void OrderTemplates_DefaultPackageFirst_ThenOthers_NoGroup()
    {
        var all = new List<(int Id, string Name)> { (1, "Довідка"), (2, "Рапорт"), (3, "Допуск") };
        var packageIds = new List<int> { 2 };

        var ordered = GenerateDocumentsDialogViewModel.OrderTemplates(all, packageIds);

        Assert.Equal(new[] { 2, 1, 3 }, ordered.Select(t => t.Id));
        Assert.Equal("Типовий пакет", ordered[0].Group);
        Assert.Equal("Інші шаблони", ordered[1].Group);
    }

    [Fact]
    public void RecipientsSummary_OneOrMany()
    {
        Assert.Equal("ШЕВЧЕНКО Т.Г.", GenerateDocumentsDialogViewModel.BuildRecipientsSummary(new[] { "ШЕВЧЕНКО Т.Г." }));
        Assert.Equal("ШЕВЧЕНКО Т.Г. та ще 2", GenerateDocumentsDialogViewModel.BuildRecipientsSummary(new[] { "ШЕВЧЕНКО Т.Г.", "А", "Б" }));
    }
}

public class GenerateDocumentsDialogInitializeTests
{
    [Fact]
    public async Task Initialize_PutsDefaultPackageTemplatesFirst()
    {
        using var db = new GenDoc.Tests.Infrastructure.TestDb();
        int templateId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var t = new GenDoc.Models.Template { Name = "Рапорт", OriginalFileName = "a.docx", Content = new byte[] { 1 }, UploadedAt = DateTime.Now, Kind = GenDoc.Models.Enums.TemplateKind.PerRecipient };
            var other = new GenDoc.Models.Template { Name = "Довідка", OriginalFileName = "b.docx", Content = new byte[] { 1 }, UploadedAt = DateTime.Now, Kind = GenDoc.Models.Enums.TemplateKind.PerRecipient };
            ctx.Templates.AddRange(t, other);
            ctx.SaveChanges();
            var package = new GenDoc.Models.GenerationPackage { Name = "Зброя" };
            package.Templates.Add(new GenDoc.Models.GenerationPackageTemplate { TemplateId = t.Id, SortOrder = 0 });
            ctx.GenerationPackages.Add(package);
            ctx.SaveChanges();
            templateId = t.Id;
        }

        var vm = new GenerateDocumentsDialogViewModel(
            GenDoc.Tests.Infrastructure.TestServices.Generation(db),
            GenDoc.Tests.Infrastructure.TestServices.Completeness(db),
            GenDoc.Tests.Infrastructure.TestServices.Archive(db),
            null!, null!, null!,
            new[] { (1, "ШЕВЧЕНКО Т.Г.") });

        await vm.InitializeAsync();

        Assert.Equal("Типовий пакет", vm.Templates.Single(t => t.Id == templateId && !t.IsExport).Group);
        Assert.Equal("Інші шаблони", vm.Templates.Single(t => t.Name == "Довідка").Group);
    }
}
