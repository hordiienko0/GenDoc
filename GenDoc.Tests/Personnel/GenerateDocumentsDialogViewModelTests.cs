using CommunityToolkit.Mvvm.Messaging;
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

public class GenerateDocumentsDialogInitializeTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-dialog-{Guid.NewGuid():N}");
    public void Dispose() { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }

    [Fact]
    public async Task Generate_SendsMatrixChangedAfterASuccessfulRun()
    {
        using var db = new GenDoc.Tests.Infrastructure.TestDb();
        int templateId, personId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Users.Add(new GenDoc.Models.UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
            ctx.AppSettings.Add(new GenDoc.Models.AppSettings { DefaultOutputFolder = _folder });
            var template = new GenDoc.Models.Template
            {
                Name = "Рапорт", OriginalFileName = "rapport.docx",
                Content = GenDoc.Tests.Infrastructure.TemplateFixtures.Bytes(GenDoc.Tests.Infrastructure.TemplateFixtures.RaportIndividualDocx),
                UploadedAt = DateTime.Now, Kind = GenDoc.Models.Enums.TemplateKind.PerRecipient
            };
            ctx.Templates.Add(template);
            var person = GenDoc.Tests.Infrastructure.TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас");
            ctx.Recipients.Add(person);
            ctx.SaveChanges();
            templateId = template.Id;
            personId = person.Id;
        }

        var vm = new GenerateDocumentsDialogViewModel(
            GenDoc.Tests.Infrastructure.TestServices.Generation(db),
            GenDoc.Tests.Infrastructure.TestServices.Completeness(db),
            GenDoc.Tests.Infrastructure.TestServices.Archive(db),
            null!, null!, new GenDoc.Services.Generation.OutputFolderService(db.Factory),
            new[] { (personId, "ШЕВЧЕНКО Т.Г.") });
        await vm.InitializeAsync();
        vm.Templates.Single(t => t.Id == templateId && !t.IsExport).IsChecked = true;

        var recipient = new object();
        var received = 0;
        CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default
            .Register<GenDoc.Services.MatrixChangedMessage>(recipient, (_, _) => received++);
        try
        {
            await vm.GenerateCommand.ExecuteAsync(null);
        }
        finally
        {
            CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.UnregisterAll(recipient);
        }

        Assert.NotNull(vm.Result);
        Assert.StartsWith("Згенеровано 1", vm.Result!.SummaryText);
        Assert.True(received >= 1);
    }

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
