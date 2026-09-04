using System.Windows;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Completeness;
using GenDoc.Services.Generation;
using GenDoc.Services.Templates;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Completeness;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.Tests.Completeness;

public class GroupColumnStalenessTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-stale-group-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private sealed class AcceptingDialog : IDialogService
    {
        public bool? ShowDialog<TViewModel>(TViewModel viewModel, Window? owner = null) where TViewModel : notnull => true;
    }

    private sealed record Seeded(int IntakeId, int PackageId, int FirstId, int SecondId, int SheetId, int GroupTemplateId);

    private Seeded Seed(TestDb db, bool detectStale = true)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
        ctx.AppSettings.Add(new AppSettings { DetectStaleDocuments = detectStale, DefaultOutputFolder = _folder });
        ctx.OrganizationSettings.Add(new OrganizationSettings
        {
            UnitNumber = "А1234", City = "Львів", CommanderRank = "полковник", CommanderFullName = "І. ПЕТРЕНКО",
            CommanderPosition = "начальник", HrOfficerFullName = "К. КАДРОВ", UnitFullName = "Коледж"
        });

        var intake = new Intake { Number = 1, DisplayNumber = "Набір №1", Status = IntakeStatus.Active };
        ctx.Intakes.Add(intake);

        var first = TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас");
        var second = TemplateFixtures.Person(2, "ФРАНКО", "Іван");
        first.FitnessCategory = "придатний";
        second.FitnessCategory = "придатний";
        ctx.Recipients.AddRange(first, second);

        var sheet = new ExportTemplate
        {
            Name = "Роздавальна", OriginalFileName = "sheet.xlsx", Content = BuildSheet(),
            UploadedAt = DateTime.Now, UsesPlaceholders = true, TemplateRowIndex = 2
        };
        sheet.ColumnMappings.Add(new ExportTemplateColumnMapping
        {
            ColumnIndex = 1, HeaderText = string.Empty, FieldKey = nameof(ExportFieldKey.FullNameFormatted),
            PlaceholderTag = "{{піб}}", SourceType = MappingSourceType.Recipient
        });
        ctx.ExportTemplates.Add(sheet);

        var bytes = TemplateFixtures.Bytes(TemplateFixtures.RaportGroupDocx);
        var group = new Template
        {
            Name = "Рапорт котлове ГРУПОВИЙ", OriginalFileName = "group.docx",
            Content = bytes, UploadedAt = DateTime.Now, Kind = TemplateKind.Group
        };
        ctx.Templates.Add(group);
        ctx.SaveChanges();

        using (var stream = new MemoryStream(bytes))
        using (var doc = WordprocessingDocument.Open(stream, false))
        {
            foreach (var (tag, insideBlock) in TemplateService.ScanPlaceholders(doc).Tags)
            {
                var (sourceType, fieldName) = PlaceholderTagMaps.Classify(tag);
                ctx.TemplateFieldMappings.Add(new TemplateFieldMapping
                {
                    TemplateId = group.Id, PlaceholderTag = tag,
                    SourceType = sourceType, FieldName = fieldName, IsInsideRepeatingBlock = insideBlock
                });
            }
        }

        first.IntakeId = intake.Id;
        second.IntakeId = intake.Id;

        var package = new GenerationPackage { Name = "Пакет" };
        package.Templates.Add(new GenerationPackageTemplate { TemplateId = group.Id, SortOrder = 0 });
        package.ExportTemplates.Add(new GenerationPackageExportTemplate
        {
            ExportTemplateId = sheet.Id, SortOrder = 0, FitnessFilter = FitnessFilter.All
        });
        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();

        return new Seeded(intake.Id, package.Id, first.Id, second.Id, sheet.Id, group.Id);
    }

    private static byte[] BuildSheet()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Відомість");
        sheet.Cell(1, 1).Value = "Роздавальна відомість";
        sheet.Cell(2, 1).Value = "{{піб}}";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private void GenerateSheet(TestDb db, Seeded s, params int[] people)
        => TestServices.Generation(db).GenerateTemplatesForRecipients(
            Array.Empty<int>(), new[] { s.SheetId }, people, _folder,
            new Dictionary<string, string>(), new Progress<string>());

    private void GenerateGroupDocx(TestDb db, Seeded s, params int[] people)
        => TestServices.Generation(db).GenerateTemplatesForRecipients(
            new[] { s.GroupTemplateId }, Array.Empty<int>(), people, _folder,
            new Dictionary<string, string>(), new Progress<string>());

    private static void LeaveIntake(TestDb db, int personId)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Recipients.Single(r => r.Id == personId).IntakeId = null;
        ctx.SaveChanges();
    }

    private static void Rename(TestDb db, int personId)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Recipients.Single(r => r.Id == personId).LastName = "ШЕВЧЕНКО-НОВИЙ";
        ctx.SaveChanges();
    }

    private static MatrixDocDto Cell(MatrixData data, int personId, int templateId, bool isExport)
    {
        Assert.True(data.Docs.TryGetValue((personId, templateId, isExport), out var cell));
        return cell!;
    }

    [Fact]
    public async Task AFreshlyGeneratedSheetIsNotStale()
    {
        using var db = new TestDb();
        var s = Seed(db);
        GenerateSheet(db, s, s.FirstId, s.SecondId);

        var data = await TestServices.Completeness(db).BuildAsync(s.IntakeId, s.PackageId);

        Assert.False(Cell(data, s.FirstId, s.SheetId, true).IsStale);
        Assert.False(Cell(data, s.SecondId, s.SheetId, true).IsStale);
    }

    [Fact]
    public async Task RemovingAPersonFromTheIntakeMakesTheSheetStale()
    {
        using var db = new TestDb();
        var s = Seed(db);
        GenerateSheet(db, s, s.FirstId, s.SecondId);
        LeaveIntake(db, s.SecondId);

        var data = await TestServices.Completeness(db).BuildAsync(s.IntakeId, s.PackageId);

        Assert.True(Cell(data, s.FirstId, s.SheetId, true).IsStale);
        Assert.Equal(1, ICompletenessService.CountGaps(data).Stale);
    }

    [Fact]
    public async Task ChangingDataThatIsPrintedInTheSheetMakesItStale()
    {
        using var db = new TestDb();
        var s = Seed(db);
        GenerateSheet(db, s, s.FirstId, s.SecondId);
        Rename(db, s.FirstId);

        var data = await TestServices.Completeness(db).BuildAsync(s.IntakeId, s.PackageId);

        Assert.True(Cell(data, s.FirstId, s.SheetId, true).IsStale);
        Assert.True(Cell(data, s.SecondId, s.SheetId, true).IsStale);
    }

    [Fact]
    public async Task WithStaleDetectionOffTheSheetStaysFresh()
    {
        using var db = new TestDb();
        var s = Seed(db, detectStale: false);
        GenerateSheet(db, s, s.FirstId, s.SecondId);
        LeaveIntake(db, s.SecondId);

        var data = await TestServices.Completeness(db).BuildAsync(s.IntakeId, s.PackageId);

        Assert.False(Cell(data, s.FirstId, s.SheetId, true).IsStale);
    }

    [Fact]
    public async Task ASheetGeneratedWithManualValuesIsNotStaleBecauseOfThem()
    {
        using var db = new TestDb();
        var s = Seed(db);
        TestServices.Generation(db).GenerateTemplatesForRecipients(
            Array.Empty<int>(), new[] { s.SheetId }, new[] { s.FirstId, s.SecondId }, _folder,
            new Dictionary<string, string> { ["{{номер}}"] = "7" }, new Progress<string>());

        var data = await TestServices.Completeness(db).BuildAsync(s.IntakeId, s.PackageId);

        Assert.False(Cell(data, s.FirstId, s.SheetId, true).IsStale);
    }

    [Fact]
    public async Task AGroupDocxFollowsTheSameRule()
    {
        using var db = new TestDb();
        var s = Seed(db);
        GenerateGroupDocx(db, s, s.FirstId, s.SecondId);

        var fresh = await TestServices.Completeness(db).BuildAsync(s.IntakeId, s.PackageId);
        Assert.False(Cell(fresh, s.FirstId, s.GroupTemplateId, false).IsStale);

        LeaveIntake(db, s.SecondId);

        var afterLeaving = await TestServices.Completeness(db).BuildAsync(s.IntakeId, s.PackageId);
        Assert.True(Cell(afterLeaving, s.FirstId, s.GroupTemplateId, false).IsStale);
    }

    private static async Task<CompletenessViewModel> CreateViewModelAsync(TestDb db)
    {
        var provider = new ServiceCollection()
            .AddSingleton<IGenerationService>(TestServices.Generation(db))
            .AddSingleton<IOutputFolderService>(new OutputFolderService(db.Factory))
            .BuildServiceProvider();

        var vm = new CompletenessViewModel(
            TestServices.Completeness(db, 1),
            TestServices.Archive(db),
            new AcceptingDialog(),
            provider,
            TestServices.ManualTagForm(db, TestServices.Staff(db, 1), new FakeCurrentUser(), new FakeIntakeAccessor(), 1));
        await vm.InitializeAsync();
        return vm;
    }

    [Fact]
    public async Task RegenerateStaleRebuildsTheSheetForTheCurrentRoster()
    {
        using var db = new TestDb();
        var s = Seed(db);
        GenerateSheet(db, s, s.FirstId, s.SecondId);
        GenerateGroupDocx(db, s, s.FirstId, s.SecondId);
        LeaveIntake(db, s.SecondId);

        var vm = await CreateViewModelAsync(db);
        Assert.Equal(2, vm.StaleCount);

        var outcome = await vm.RegenerateStaleCoreAsync();

        Assert.NotNull(outcome);
        Assert.Equal(2, outcome!.Value.Done);
        Assert.Empty(outcome.Value.Errors);

        using (var ctx = db.Factory.CreateDbContext())
        {
            var current = ctx.GeneratedGroupDocuments
                .Where(g => g.ExportTemplateId == s.SheetId && g.IsCurrent && g.IntakeId == s.IntakeId)
                .Select(g => new { g.Version, Participants = g.Recipients.Select(r => r.RecipientId).ToList() })
                .Single();
            Assert.Equal(2, current.Version);
            Assert.Equal(new[] { s.FirstId }, current.Participants);
        }

        await vm.InitializeAsync();
        Assert.Equal(0, vm.StaleCount);
    }
}
