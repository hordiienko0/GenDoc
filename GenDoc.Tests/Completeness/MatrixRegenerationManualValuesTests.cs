using System.Windows;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Services.Templates;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Archive;
using GenDoc.ViewModels.Completeness;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.Tests.Completeness;

public class MatrixRegenerationManualValuesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-matrix-manual-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private const string OrderTag = "{{номер_наказу}}";
    private const string SheetNumberTag = "{{номер_відомості}}";

    private sealed class ScriptedDialog : IDialogService
    {
        public Dictionary<string, string> Values { get; } = new();
        public int? CourseOfficerId { get; set; }
        public bool Accept { get; set; } = true;
        public List<string> ShownTags { get; } = new();
        public bool CourseOfficerAsked { get; private set; }
        public int Shown { get; private set; }

        public bool? ShowDialog<TViewModel>(TViewModel viewModel, Window? owner = null) where TViewModel : notnull
        {
            if (viewModel is not ManualValuesDialogViewModel dialog) return null;
            Shown++;
            ShownTags.AddRange(dialog.Form.Rows.Select(r => r.Tag));
            CourseOfficerAsked = dialog.Form.CourseOfficer is not null;

            foreach (var row in dialog.Form.Rows)
                if (Values.TryGetValue(row.Tag, out var value)) row.Value = value;

            if (dialog.Form.CourseOfficer is { } picker && CourseOfficerId is int id)
                picker.Selected = picker.Options.First(o => o.RecipientId == id);

            return Accept;
        }
    }

    private sealed record Seeded(int IntakeId, int PackageId, int PersonId, int TemplateId, int SheetId, int SecondOfficerId);

    private Seeded Seed(TestDb db, bool withSheet = false)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now });
        ctx.AppSettings.Add(new AppSettings { DetectStaleDocuments = true, DefaultOutputFolder = _folder });

        var intake = new Intake { Number = 3, DisplayNumber = "Набір №3", Status = IntakeStatus.Active };
        ctx.Intakes.Add(intake);
        ctx.SaveChanges();

        var person = TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас");
        person.IntakeId = intake.Id;
        person.FitnessCategory = "придатний";
        var first = new Recipient { Id = 2, LastName = "Ковальчук", FirstName = "Василь", Rank = "капітан", IsCourseOfficer = true };
        var second = new Recipient { Id = 3, LastName = "Мельник", FirstName = "Петро", Rank = "майор", IsCourseOfficer = true };
        ctx.Recipients.AddRange(person, first, second);

        var template = new Template
        {
            Name = "Наказ", OriginalFileName = "nakaz.docx", Content = BuildDocx(),
            UploadedAt = DateTime.Now, Kind = TemplateKind.PerRecipient
        };
        ctx.Templates.Add(template);
        ctx.SaveChanges();

        foreach (var tag in new[] { "{{піб}}", OrderTag })
        {
            var (sourceType, fieldName) = PlaceholderTagMaps.Classify(tag);
            ctx.TemplateFieldMappings.Add(new TemplateFieldMapping
            {
                TemplateId = template.Id, PlaceholderTag = tag, SourceType = sourceType, FieldName = fieldName
            });
        }

        var package = new GenerationPackage { Name = "Пакет" };
        package.Templates.Add(new GenerationPackageTemplate
        {
            TemplateId = template.Id, SortOrder = 0,
            RequirementRegular = TemplateRequirement.Required, RequirementLimited = TemplateRequirement.Required
        });

        var sheetId = 0;
        if (withSheet)
        {
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
            sheet.ColumnMappings.Add(new ExportTemplateColumnMapping
            {
                ColumnIndex = 0, HeaderText = string.Empty, FieldKey = string.Empty,
                PlaceholderTag = SheetNumberTag, SourceType = MappingSourceType.Manual
            });
            sheet.ColumnMappings.Add(new ExportTemplateColumnMapping
            {
                ColumnIndex = 0, HeaderText = string.Empty, FieldKey = nameof(ExportFieldKey.CourseOfficerSignature),
                PlaceholderTag = "{{курсовий_офіцер}}", SourceType = MappingSourceType.Recipient
            });
            ctx.ExportTemplates.Add(sheet);
            ctx.SaveChanges();
            sheetId = sheet.Id;
            package.ExportTemplates.Add(new GenerationPackageExportTemplate
            {
                ExportTemplateId = sheet.Id, SortOrder = 0, FitnessFilter = FitnessFilter.All
            });
        }

        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();

        return new Seeded(intake.Id, package.Id, person.Id, template.Id, sheetId, second.Id);
    }

    private static byte[] BuildDocx()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            doc.AddMainDocumentPart().Document = new Document(new Body(
                new Paragraph(new Run(new Text("{{піб}} наказ {{номер_наказу}}")))));
            doc.MainDocumentPart!.Document!.Save();
        }
        return stream.ToArray();
    }

    private static byte[] BuildSheet()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Відомість");
        sheet.Cell(1, 1).Value = "Відомість № {{номер_відомості}}";
        sheet.Cell(2, 1).Value = "{{піб}}";
        sheet.Cell(4, 1).Value = "{{курсовий_офіцер}}";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private sealed class RecordingBuilder : IManualTagFormBuilder
    {
        private readonly IManualTagFormBuilder _inner;
        public List<int?> IntakeIds { get; } = new();

        public RecordingBuilder(IManualTagFormBuilder inner) => _inner = inner;

        public Task<GenDoc.ViewModels.Generation.ManualTagFormViewModel> BuildAsync(
            IReadOnlyList<string> tags, string contextKey, bool needsCourseOfficer = false, int? intakeId = null)
        {
            IntakeIds.Add(intakeId);
            return _inner.BuildAsync(tags, contextKey, needsCourseOfficer, intakeId);
        }

        public Task SaveAsync(string contextKey, GenDoc.ViewModels.Generation.ManualTagFormViewModel form)
            => _inner.SaveAsync(contextKey, form);
    }

    private static async Task<CompletenessViewModel> CreateViewModelAsync(
        TestDb db, ScriptedDialog dialog, IManualTagFormBuilder? builder = null)
    {
        var provider = new ServiceCollection()
            .AddSingleton<IGenerationService>(TestServices.Generation(db))
            .AddSingleton<IOutputFolderService>(new OutputFolderService(db.Factory))
            .BuildServiceProvider();

        var vm = new CompletenessViewModel(
            TestServices.Completeness(db, 1),
            TestServices.Archive(db),
            dialog,
            provider,
            builder ?? TestServices.ManualTagForm(db, TestServices.Staff(db, 1), new FakeCurrentUser(), new FakeIntakeAccessor(), 1));
        await vm.InitializeAsync();
        return vm;
    }

    private static void MakeStale(TestDb db, int personId)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Recipients.First(r => r.Id == personId).LastName = "ШЕВЧЕНКО-НОВИЙ";
        ctx.SaveChanges();
    }

    private static List<GeneratedDocument> Versions(TestDb db, int personId)
    {
        using var ctx = db.Factory.CreateDbContext();
        return ctx.GeneratedDocuments.Where(g => g.RecipientId == personId).OrderBy(g => g.Version).ToList();
    }

    private static string DocxText(TestDb db, int documentId)
    {
        using var ctx = db.Factory.CreateDbContext();
        var content = ctx.GeneratedDocumentContents.Single(c => c.GeneratedDocumentId == documentId).Content;
        using var doc = WordprocessingDocument.Open(new MemoryStream(content), false);
        return string.Concat(doc.MainDocumentPart!.Document!.Body!.Descendants<Text>().Select(t => t.Text));
    }

    [Fact]
    public async Task RegenerateStaleCells_AsksForManualValuesAndPutsThemIntoTheNewVersion()
    {
        using var db = new TestDb();
        var s = Seed(db);
        await TestServices.Completeness(db).GenerateForPairAsync(s.PersonId, s.TemplateId,
            new Dictionary<string, string> { [OrderTag] = "1" });
        MakeStale(db, s.PersonId);

        var dialog = new ScriptedDialog { Values = { [OrderTag] = "77" } };
        var vm = await CreateViewModelAsync(db, dialog);
        var cell = vm.Rows.Single().Cells.Single(c => c.TemplateId == s.TemplateId);
        Assert.True(cell.IsStale);

        var outcome = await vm.RegenerateCellsAsync(new[] { cell });

        Assert.NotNull(outcome);
        Assert.Equal(1, outcome!.Value.Done);
        Assert.Contains(OrderTag, dialog.ShownTags);
        var current = Assert.Single(Versions(db, s.PersonId), v => v.IsCurrent);
        Assert.Equal(2, current.Version);
        Assert.Contains("наказ 77", DocxText(db, current.Id));
    }

    [Fact]
    public async Task RegenerateCell_CancelledDialog_LeavesTheArchiveUntouched()
    {
        using var db = new TestDb();
        var s = Seed(db);
        await TestServices.Completeness(db).GenerateForPairAsync(s.PersonId, s.TemplateId,
            new Dictionary<string, string> { [OrderTag] = "1" });

        var dialog = new ScriptedDialog { Accept = false };
        var vm = await CreateViewModelAsync(db, dialog);
        var cell = vm.Rows.Single().Cells.Single(c => c.TemplateId == s.TemplateId);

        var outcome = await vm.RegenerateCellsAsync(new[] { cell });

        Assert.Null(outcome);
        Assert.Equal(1, dialog.Shown);
        Assert.Single(Versions(db, s.PersonId));
    }

    [Fact]
    public async Task GenerateMissing_AsksSheetTagsAndCourseOfficer_AndUsesBoth()
    {
        using var db = new TestDb();
        var s = Seed(db, withSheet: true);

        var dialog = new ScriptedDialog
        {
            Values = { [OrderTag] = "5", [SheetNumberTag] = "13" },
            CourseOfficerId = s.SecondOfficerId
        };
        var vm = await CreateViewModelAsync(db, dialog);

        var outcome = await vm.GenerateMissingCoreAsync(includeOptional: false);

        Assert.NotNull(outcome);
        Assert.Equal(2, outcome!.Value.Done);
        Assert.Empty(outcome.Value.Errors);
        Assert.Equal(1, dialog.Shown);
        Assert.Contains(SheetNumberTag, dialog.ShownTags);
        Assert.Contains(OrderTag, dialog.ShownTags);
        Assert.True(dialog.CourseOfficerAsked);

        var personal = Assert.Single(Versions(db, s.PersonId));
        Assert.Contains("наказ 5", DocxText(db, personal.Id));

        using var ctx = db.Factory.CreateDbContext();
        var sheet = Assert.Single(ctx.GeneratedGroupDocuments.Where(g => g.ExportTemplateId == s.SheetId).ToList());
        var content = ctx.GeneratedGroupDocumentContents.Single(c => c.GeneratedGroupDocumentId == sheet.Id).Content;
        using var workbook = new XLWorkbook(new MemoryStream(content));
        var cells = workbook.Worksheets.First().RangeUsed()!.CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Contains(cells, t => t.Contains("Відомість № 13", StringComparison.Ordinal));
        Assert.Contains(cells, t => t.Contains("Мельник", StringComparison.Ordinal));
        Assert.DoesNotContain(cells, t => t.Contains("Ковальчук", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RegenerateCell_AsksManualFormForTheSelectedIntake_NotTheActiveOne()
    {
        using var db = new TestDb();
        var s = Seed(db);
        await TestServices.Completeness(db).GenerateForPairAsync(s.PersonId, s.TemplateId,
            new Dictionary<string, string> { [OrderTag] = "1" });
        MakeStale(db, s.PersonId);

        var dialog = new ScriptedDialog { Values = { [OrderTag] = "2" } };
        var builder = new RecordingBuilder(
            TestServices.ManualTagForm(db, TestServices.Staff(db, 1), new FakeCurrentUser(), new FakeIntakeAccessor(), 1));
        var vm = await CreateViewModelAsync(db, dialog, builder);
        var cell = vm.Rows.Single().Cells.Single(c => c.TemplateId == s.TemplateId);

        var outcome = await vm.RegenerateCellsAsync(new[] { cell });

        Assert.NotNull(outcome);
        Assert.Equal(s.IntakeId, Assert.Single(builder.IntakeIds));
    }
}
