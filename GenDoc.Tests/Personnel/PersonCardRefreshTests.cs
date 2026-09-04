using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Services.Intakes;
using GenDoc.Services.Personnel;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Generation;
using GenDoc.ViewModels.Personnel;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.Tests.Personnel;

public class PersonCardRefreshTests
{
    private sealed record Seeded(int PersonId, int TemplateId, int PackageId);

    private static async Task<Seeded> SeedAsync(TestDb db)
    {
        int templateId, packageId, rootId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
            ctx.AppSettings.Add(new AppSettings { DetectStaleDocuments = true });
            ctx.OrganizationSettings.Add(new OrganizationSettings
            {
                UnitNumber = "А1234", City = "Львів", CommanderRank = "полковник", CommanderFullName = "І. ПЕТРЕНКО",
                CommanderPosition = "начальник", HrOfficerFullName = "К. КАДРОВ", UnitFullName = "Коледж"
            });
            var root = new OrgNode { Name = "Курс", Depth = 0, SortOrder = 0, Path = "/" };
            ctx.OrgNodes.Add(root);
            var template = new Template
            {
                Name = "Рапорт", OriginalFileName = "rapport.docx",
                Content = TemplateFixtures.Bytes(TemplateFixtures.RaportIndividualDocx),
                UploadedAt = DateTime.Now, Kind = GenDoc.Models.Enums.TemplateKind.PerRecipient
            };
            ctx.Templates.Add(template);
            ctx.SaveChanges();
            root.Path = $"/{root.Id}/";
            foreach (var tag in new[] { "{{звання_зв}}", "{{піб_зв}}", "{{прибув}}", "{{таким}}" })
            {
                var (sourceType, fieldName) = GenDoc.Services.Templates.PlaceholderTagMaps.Classify(tag);
                ctx.TemplateFieldMappings.Add(new TemplateFieldMapping
                { TemplateId = template.Id, PlaceholderTag = tag, SourceType = sourceType, FieldName = fieldName });
            }
            var package = new GenerationPackage { Name = "Пакет" };
            package.Templates.Add(new GenerationPackageTemplate { TemplateId = template.Id, SortOrder = 0 });
            ctx.GenerationPackages.Add(package);
            ctx.SaveChanges();
            templateId = template.Id;
            packageId = package.Id;
            rootId = root.Id;
        }

        var saved = await Personnel(db).SaveAsync(new PersonEditModel
        {
            LastName = "ШЕВЧЕНКО", FirstName = "Тарас", MiddleName = "Григорович",
            Rank = "солдат", Position = "курсант", ServiceNumber = "СН0001", OrgNodeId = rootId
        });
        Assert.True(saved.Success);
        return new Seeded(saved.Id, templateId, packageId);
    }

    private static PersonnelService Personnel(TestDb db)
        => new(db.Factory, new FakeAuditLog(), new FakeCurrentUser());

    private static async Task<PersonCardViewModel> OpenCardAsync(TestDb db, int personId)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db.Factory);
        services.AddSingleton<IAuditLogService>(new FakeAuditLog());
        services.AddSingleton<ICurrentUserContext>(new FakeCurrentUser());
        services.AddSingleton<IUserSettingsService>(TestServices.UserSettings(db, 1));
        services.AddSingleton<GenDoc.Services.Completeness.ICompletenessService>(_ => TestServices.Completeness(db, 1));
        services.AddSingleton<IIntakeService, IntakeService>();
        var provider = services.BuildServiceProvider();

        var personnel = Personnel(db);
        var model = await personnel.GetForEditAsync(personId);
        var card = new PersonCardViewModel(
            personnel, TestServices.Completeness(db, 1), TestServices.Archive(db), TestServices.Generation(db),
            provider.GetRequiredService<IIntakeService>(), new NoDialogs(), new NoManualTags(),
            new OutputFolderService(db.Factory), model!, "Курс");
        card.SelectedTabIndex = 1;
        await card.DocumentsLoad;
        return card;
    }

    private static List<MatrixChangedMessage> Listen(object recipient)
    {
        var received = new List<MatrixChangedMessage>();
        WeakReferenceMessenger.Default.Register<MatrixChangedMessage>(recipient, (_, m) => received.Add(m));
        return received;
    }

    [Fact]
    public async Task GenerateDocument_SendsMatrixChangedAfterSuccess()
    {
        using var db = new TestDb();
        var s = await SeedAsync(db);
        var card = await OpenCardAsync(db, s.PersonId);
        var recipient = new object();
        var received = Listen(recipient);
        try
        {
            var row = Assert.Single(card.DocumentRows);
            Assert.Equal("Немає", row.StateText);

            await card.GenerateDocumentCommand.ExecuteAsync(row);

            Assert.NotEmpty(received);
            Assert.Equal("Є", Assert.Single(card.DocumentRows).StateText);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
        }
    }

    [Fact]
    public async Task GenerateMissing_SendsMatrixChangedAfterSuccess()
    {
        using var db = new TestDb();
        var s = await SeedAsync(db);
        var card = await OpenCardAsync(db, s.PersonId);
        var recipient = new object();
        var received = Listen(recipient);
        try
        {
            Assert.True(card.HasMissingDocuments);

            await card.GenerateMissingCommand.ExecuteAsync(null);

            Assert.NotEmpty(received);
            Assert.False(card.HasMissingDocuments);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
        }
    }

    private sealed class NoDialogs : IDialogService
    {
        public bool? ShowDialog<TViewModel>(TViewModel viewModel, Window? owner = null) where TViewModel : notnull => null;
    }

    private sealed class NoManualTags : IManualTagFormBuilder
    {
        public Task<ManualTagFormViewModel> BuildAsync(IReadOnlyList<string> tags, string contextKey, bool needsCourseOfficer = false)
            => throw new NotSupportedException();

        public Task SaveAsync(string contextKey, ManualTagFormViewModel form) => Task.CompletedTask;
    }
}
