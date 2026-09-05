using ClosedXML.Excel;
using GenDoc.Models;
using GenDoc.Models.Enums;
using TemplateKind = GenDoc.Models.Enums.TemplateKind;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Services.Templates;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Generation;
using GenDoc.ViewModels.Staff;
using GenDoc.ViewModels.Templates;

namespace GenDoc.Tests.Templates;

public class TemplatesListPolishTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    private string TempFile(string extension, byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, content);
        _tempFiles.Add(path);
        return path;
    }

    private string TwoSheetWorkbook()
    {
        using var workbook = new XLWorkbook();
        var first = workbook.AddWorksheet("Відомість");
        first.Cell(1, 1).Value = "ПІБ";
        first.Cell(1, 2).Value = "Звання";
        first.Cell(2, 1).Value = "{{піб}}";
        first.Cell(2, 2).Value = "{{звання}}";
        var second = workbook.AddWorksheet("Підписи");
        second.Cell(1, 1).Value = "Дата {{дата_аркуша}}";
        second.Cell(3, 1).Value = "{{курсовий_офіцер}}";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return TempFile(".xlsx", stream.ToArray());
    }

    private static ExportTemplateService ExportService(TestDb db, FakeAuditLog? audit = null)
        => new(db.Factory, audit ?? new FakeAuditLog(), new FakeCurrentUser());

    private static TemplateService DocxService(TestDb db, FakeAuditLog? audit = null)
        => new(db.Factory, audit ?? new FakeAuditLog(), new FakeCurrentUser());

    private static TemplatesViewModel ViewModel(TestDb db, FakeAuditLog? audit = null)
        => new(ExportService(db, audit), DocxService(db, audit), null!);

    private static int SeedDocx(TestDb db, string name, TemplateKind kind = TemplateKind.PerRecipient,
        TemplateAudience audience = TemplateAudience.Intake)
    {
        using var ctx = db.Factory.CreateDbContext();
        var template = new Template
        {
            Name = name, OriginalFileName = $"{name}.docx", Content = new byte[] { 1 },
            UploadedAt = DateTime.Now, Kind = kind, Audience = audience
        };
        template.FieldMappings.Add(new TemplateFieldMapping
        {
            PlaceholderTag = "{{піб}}", SourceType = MappingSourceType.Recipient, FieldName = "FullNameFormatted"
        });
        ctx.Templates.Add(template);
        ctx.SaveChanges();
        return template.Id;
    }

    [Fact]
    public void UploadXlsx_LockedFile_AsksToCloseExcel()
    {
        using var db = new TestDb();
        var path = TwoSheetWorkbook();
        using var holder = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        var result = ExportService(db).UploadTemplate(path);

        Assert.False(result.Success);
        Assert.Contains("Закрийте файл в Excel і спробуйте ще раз", result.ErrorMessage);
        using var ctx = db.Factory.CreateDbContext();
        Assert.Empty(ctx.ExportTemplates.ToList());
    }

    [Fact]
    public void UploadXlsx_NotAWorkbook_SaysSo()
    {
        using var db = new TestDb();
        var path = TempFile(".xlsx", new byte[] { 1, 2, 3 });

        var result = ExportService(db).UploadTemplate(path);

        Assert.False(result.Success);
        Assert.Contains("не є книгою Excel", result.ErrorMessage);
    }

    [Fact]
    public void UploadXlsx_MissingFile_ReportsNotFound()
    {
        using var db = new TestDb();

        var result = ExportService(db).UploadTemplate(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx"));

        Assert.False(result.Success);
        Assert.Contains("не знайдено", result.ErrorMessage);
    }

    [Fact]
    public void UploadXlsx_ScansEverySheet_SoSecondSheetTagsBecomeMappings()
    {
        using var db = new TestDb();
        var service = ExportService(db);

        var result = service.UploadTemplate(TwoSheetWorkbook());

        Assert.True(result.Success, result.ErrorMessage);
        using var ctx = db.Factory.CreateDbContext();
        var template = ctx.ExportTemplates.Single();
        Assert.True(template.UsesPlaceholders);
        Assert.Equal(2, template.TemplateRowIndex);
        var tags = service.GetMappings(template.Id).Select(m => m.PlaceholderTag).ToList();
        Assert.Contains("{{піб}}", tags);
        Assert.Contains("{{дата_аркуша}}", tags);
        Assert.Contains("{{курсовий_офіцер}}", tags);
        Assert.Contains("{{дата_аркуша}}", service.GetManualTags(template.Id));
    }

    [Fact]
    public void GroupDocxTemplate_IsBadgedAsGroupInTheList()
    {
        using var db = new TestDb();
        SeedDocx(db, "Рапорт груповий", TemplateKind.Group);
        SeedDocx(db, "Рапорт особистий");

        var vm = ViewModel(db);

        var group = vm.DocxTemplates.Single(t => t.Name == "Рапорт груповий");
        var personal = vm.DocxTemplates.Single(t => t.Name == "Рапорт особистий");
        Assert.True(group.IsGroup);
        Assert.Equal("Груповий", group.KindBadge);
        Assert.False(personal.IsGroup);
    }

    [Fact]
    public void SaveShortName_WritesAuditAndShowsFeedback()
    {
        using var db = new TestDb();
        var id = SeedDocx(db, "Рапорт");
        var audit = new FakeAuditLog();
        var vm = ViewModel(db, audit);
        var item = vm.DocxTemplates.Single();
        item.ShortNameEdit = "Рап.";

        vm.SaveShortNameCommand.Execute(item);

        Assert.Contains($"update:Template:{id}", audit.Entries);
        Assert.Contains("Коротку назву збережено", vm.ListStatusMessage);
        using var ctx = db.Factory.CreateDbContext();
        Assert.Equal("Рап.", ctx.Templates.Single().ShortName);
    }

    [Fact]
    public void ChangingAudience_WritesAuditAndShowsFeedback()
    {
        using var db = new TestDb();
        var id = SeedDocx(db, "Рапорт");
        var audit = new FakeAuditLog();
        var vm = ViewModel(db, audit);

        vm.DocxTemplates.Single().IsForPermanentStaff = true;

        Assert.Contains($"update:Template:{id}", audit.Entries);
        Assert.Contains("Постійний склад", vm.ListStatusMessage);
    }

    [Fact]
    public void RefreshLists_KeepsTheSelectedTemplateBoundToTheFreshRow()
    {
        using var db = new TestDb();
        var id = SeedDocx(db, "Рапорт");
        var vm = ViewModel(db);
        vm.SelectTemplateCommand.Execute(vm.DocxTemplates.Single());
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Templates.Single().Name = "Рапорт (оновлено)";
            ctx.SaveChanges();
        }

        vm.RefreshLists();

        var fresh = vm.DocxTemplates.Single();
        Assert.Equal("Рапорт (оновлено)", fresh.Name);
        Assert.Same(fresh, vm.SelectedDocxTemplate);
        Assert.Same(fresh, vm.SelectedTemplate);
        Assert.True(fresh.IsSelected);
        Assert.True(fresh.MappingsLoaded);
        Assert.Equal(id, fresh.Id);
    }

    [Fact]
    public void DeletingTheSelectedTemplate_ClearsTheMappingPanel()
    {
        using var db = new TestDb();
        SeedDocx(db, "Рапорт");
        var vm = ViewModel(db);
        var item = vm.DocxTemplates.Single();
        vm.SelectTemplateCommand.Execute(item);

        var (success, _) = vm.DeleteDocxTemplateCore(item);

        Assert.True(success);
        Assert.Null(vm.SelectedTemplate);
        Assert.Null(vm.SelectedDocxTemplate);
        Assert.False(vm.HasSelectedTemplate);
        Assert.Empty(vm.DocxTemplates);
    }

    [Fact]
    public void DeletingTheSelectedExportTemplate_ClearsTheMappingPanel()
    {
        using var db = new TestDb();
        var service = ExportService(db);
        Assert.True(service.UploadTemplate(TwoSheetWorkbook()).Success);
        var vm = ViewModel(db);
        var item = vm.DocumentExcelTemplates.Single();
        vm.SelectTemplateCommand.Execute(item);

        var (success, _) = vm.DeleteExportTemplateCore(item);

        Assert.True(success);
        Assert.Null(vm.SelectedTemplate);
        Assert.Null(vm.SelectedExportTemplate);
        Assert.Empty(vm.DocumentExcelTemplates);
    }

    private sealed class NoManualTags : IManualTagFormBuilder
    {
        public Task<ManualTagFormViewModel> BuildAsync(IReadOnlyList<string> tags, string contextKey, bool needsCourseOfficer = false, int? intakeId = null)
            => throw new NotSupportedException();

        public Task SaveAsync(string contextKey, ManualTagFormViewModel form) => Task.CompletedTask;
    }

    [Fact]
    public void StaffDocDialog_OffersOnlyPerRecipientTemplatesForTripsAndLeave()
    {
        using var db = new TestDb();
        SeedDocx(db, "Рапорт груповий", TemplateKind.Group, TemplateAudience.PermanentStaff);
        SeedDocx(db, "Рапорт відрядження", TemplateKind.PerRecipient, TemplateAudience.PermanentStaff);
        var vm = new StaffDocDialogViewModel(
            TestServices.Generation(db), TestServices.Archive(db), new NoManualTags(), TestServices.Staff(db));

        vm.Initialize(StaffEventKind.BusinessTrip, new[] { (1, "Ковальчук В. Б.") });

        var option = Assert.Single(vm.Templates);
        Assert.Equal("Рапорт відрядження", option.Name);
    }
}
