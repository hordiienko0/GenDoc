using ClosedXML.Excel;
using GenDoc.Models;
using GenDoc.Services.Import;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Import;

// Зіставлення колонок і нумерація рядків. Три дефекти з аудиту 2026-08-28,
// кожен тихий: файл імпортується «успішно», а в базі не те, що у файлі.
public class ImportColumnMappingTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-map-{Guid.NewGuid():N}");

    public ImportColumnMappingTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static ImportTargetField Map(string header)
    {
        var noteAssigned = false;
        return ImportService.AutoMapHeader(header, ref noteAssigned);
    }

    // Реальні файли підписують колонку командира по-різному, і половина
    // варіантів містить «ПІБ». Правило «піб» стояло вище за «командир», тож
    // колонка командира ставала колонкою ПІБ - і людей звали іменами їхніх
    // командирів.
    [Theory]
    [InlineData("Командир (ПІБ та телефон)")]
    [InlineData("Командир підрозділу (ПІБ, телефон)")]
    [InlineData("ПІБ командира")]
    [InlineData("Командир (ПІП та телефон)")]
    public void CommanderColumn_DoesNotHijackTheFullNameField(string header)
        => Assert.Equal(ImportTargetField.CommanderContact, Map(header));

    // Захист від зворотного перекосу: власне ПІБ має лишитись ПІБ.
    [Theory]
    [InlineData("ПІБ")]
    [InlineData("ПІБ (повністю)")]
    public void PlainFullNameColumn_StillMapsToFullName(string header)
        => Assert.Equal(ImportTargetField.FullName, Map(header));

    [Fact]
    public void ForeignLanguageNameColumn_StillWinsOverFullName()
        => Assert.Equal(ImportTargetField.NameTransliterated, Map("ПІБ на іноземній мові"));

    // Два стовпці, зіставлені з тим самим полем: у ParseRows словник
    // перезаписувався останнім стовпцем, і ПОРОЖНІЙ дубль стирав заповнене
    // значення. Перемагає перше непорожнє.
    [Fact]
    public void DuplicateMapping_KeepsTheFilledColumn()
    {
        var path = Path.Combine(_folder, "duplicate.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Люди");
            ws.Cell(1, 1).Value = "ПІБ";
            ws.Cell(1, 2).Value = "ПІБ (дубль)";
            ws.Cell(1, 3).Value = "Особовий номер";
            ws.Cell(2, 1).Value = "ТЕСТЕНКО Тест Тестович";
            ws.Cell(2, 2).Value = string.Empty;
            ws.Cell(2, 3).Value = "ТЕСТ-ДУБЛЬ-001";
            wb.SaveAs(path);
        }

        using var db = new TestDb();
        var service = new ImportService(db.Factory, new FakeAuditLog());
        var parsed = service.ParseFile(path);
        foreach (var column in parsed.Columns)
        {
            column.MappedField = column.Header switch
            {
                "ПІБ" or "ПІБ (дубль)" => ImportTargetField.FullName,
                "Особовий номер" => ImportTargetField.ServiceNumber,
                _ => ImportTargetField.NotImported
            };
        }

        var summary = service.Import(parsed, ImportTarget.FromFile);

        Assert.Equal(1, summary.Imported);
        using var ctx = db.Factory.CreateDbContext();
        var person = Assert.Single(ctx.Recipients.ToList());
        Assert.Equal("ТЕСТЕНКО", person.LastName);
        Assert.Equal("Тест", person.FirstName);
    }

    // «Рядок N» у звіті мусить збігатися з номером рядка в Excel, інакше
    // оператор іде виправляти не той рядок. RowsUsed() пропускає порожні
    // рядки, а номер рахувався як «порядковий + 2».
    [Fact]
    public void RowNumbersInTheReport_MatchTheWorksheet()
    {
        var path = Path.Combine(_folder, "gap.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Люди");
            ws.Cell(1, 1).Value = "ПІБ";
            ws.Cell(2, 1).Value = "ПЕРШЕНКО Перший Першович";
            // рядок 3 порожній - RowsUsed() його не поверне
            ws.Cell(4, 1).Value = "ЧЕТВЕРТЕНКО Четвертий Четвертович";
            wb.SaveAs(path);
        }

        using var db = new TestDb();
        var service = new ImportService(db.Factory, new FakeAuditLog());
        var parsed = service.ParseFile(path);
        foreach (var column in parsed.Columns)
            column.MappedField = ImportTargetField.FullName;

        var previews = service.Validate(parsed, ImportTarget.FromFile);

        Assert.Equal(2, previews.Count);
        Assert.Equal(2, previews[0].RowNumber);
        Assert.Equal(4, previews[1].RowNumber);
    }

    // Гілку обрав оператор, тож підрозділ із файлу нікуди людину не кладе.
    // ResolveOrgNode усе одно створював вузол під коренем - у дереві з'являлися
    // папки, у яких ніколи нікого немає.
    [Fact]
    public void ChosenBranch_DoesNotCreateAFolderForTheUnitFromTheFile()
    {
        var path = Path.Combine(_folder, "unit.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Люди");
            ws.Cell(1, 1).Value = "ПІБ";
            ws.Cell(1, 2).Value = "Підрозділ";
            ws.Cell(2, 1).Value = "ТЕСТЕНКО Тест Тестович";
            ws.Cell(2, 2).Value = "Фантомна рота";
            wb.SaveAs(path);
        }

        using var db = new TestDb();
        int intakeId, branchId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var root = new OrgNode { Name = "Корінь", Depth = 0, SortOrder = 0, Path = "/" };
            ctx.OrgNodes.Add(root);
            ctx.SaveChanges();
            root.Path = $"/{root.Id}/";

            var intake = new Intake { Number = 14, DisplayNumber = "Набір №14" };
            ctx.Intakes.Add(intake);
            ctx.SaveChanges();

            var branch = new OrgNode
            {
                Name = "Обрана гілка",
                ParentId = root.Id,
                Depth = 1,
                SortOrder = 0,
                IntakeId = intake.Id
            };
            ctx.OrgNodes.Add(branch);
            ctx.SaveChanges();
            branch.Path = $"{root.Path}{branch.Id}/";
            ctx.SaveChanges();

            intakeId = intake.Id;
            branchId = branch.Id;
        }

        var service = new ImportService(db.Factory, new FakeAuditLog());
        var parsed = service.ParseFile(path);
        foreach (var column in parsed.Columns)
        {
            column.MappedField = column.Header switch
            {
                "ПІБ" => ImportTargetField.FullName,
                "Підрозділ" => ImportTargetField.Unit,
                _ => ImportTargetField.NotImported
            };
        }

        var summary = service.Import(parsed, new ImportTarget(ImportTargetKind.Intake, intakeId, branchId));

        Assert.Equal(1, summary.Imported);
        using var check = db.Factory.CreateDbContext();
        Assert.DoesNotContain(check.OrgNodes.ToList(), n => n.Name == "Фантомна рота");
        var person = Assert.Single(check.Recipients.ToList());
        Assert.Equal(branchId, person.OrgNodeId);
    }

    // Постійний склад свідомо НЕ входить у це правило: майстер не дає йому
    // обрати гілку, тож колонка «Підрозділ» у файлі - єдиний сигнал про місце
    // в дереві, і папку за нею треба створити.

    // «Підрозділ» у файлі лишається робочим там, де він і вирішує - у старому
    // режимі без майстра. Тут папка МАЄ з'явитись.
    [Fact]
    public void FileDrivenImport_StillCreatesTheFolderForTheUnitFromTheFile()
    {
        var path = Path.Combine(_folder, "fromfile.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Люди");
            ws.Cell(1, 1).Value = "ПІБ";
            ws.Cell(1, 2).Value = "Підрозділ";
            ws.Cell(2, 1).Value = "ТЕСТЕНКО Тест Тестович";
            ws.Cell(2, 2).Value = "Нова рота";
            wb.SaveAs(path);
        }

        using var db = new TestDb();
        using (var ctx = db.Factory.CreateDbContext())
        {
            var root = new OrgNode { Name = "Корінь", Depth = 0, SortOrder = 0, Path = "/" };
            ctx.OrgNodes.Add(root);
            ctx.SaveChanges();
            root.Path = $"/{root.Id}/";
            ctx.SaveChanges();
        }

        var service = new ImportService(db.Factory, new FakeAuditLog());
        var parsed = service.ParseFile(path);
        foreach (var column in parsed.Columns)
        {
            column.MappedField = column.Header switch
            {
                "ПІБ" => ImportTargetField.FullName,
                "Підрозділ" => ImportTargetField.Unit,
                _ => ImportTargetField.NotImported
            };
        }

        var summary = service.Import(parsed, ImportTarget.FromFile);

        Assert.Equal(1, summary.Imported);
        using var check = db.Factory.CreateDbContext();
        var created = Assert.Single(check.OrgNodes.ToList(), n => n.Name == "Нова рота");
        var person = Assert.Single(check.Recipients.ToList());
        Assert.Equal(created.Id, person.OrgNodeId);
    }
}
