using ClosedXML.Excel;
using GenDoc.Models;
using GenDoc.Services.Import;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Import;

// Наскрізний прогін «Перемістити» через СПРАВЖНІЙ файл: ParseFile читає .xlsx з
// диска, Validate знаходить збіг, Import переносить картку.
//
// Навіщо саме так: попередні тести кроку 4 будували ImportParseResult руками,
// тобто обходили розбір файлу. Тут проходить увесь ланцюг - саме той, який
// виконується, коли оператор обирає файл у майстрі. Єдине, чого тут немає, -
// натискання по кнопці; сам системний діалог вибору файлу автоматизувати
// надійно не вдалося.
public class ImportMoveEndToEndTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-move-{Guid.NewGuid():N}");

    public ImportMoveEndToEndTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private string WriteWorkbook(string fileName, string rank)
    {
        var path = Path.Combine(_folder, fileName);

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Люди");
        ws.Cell(1, 1).Value = "ПІБ";
        ws.Cell(1, 2).Value = "Особовий номер";
        ws.Cell(1, 3).Value = "Звання";
        ws.Cell(2, 1).Value = "ТЕСТЕНКО Тест Тестович";
        ws.Cell(2, 2).Value = "ТЕСТ-МІГРАЦІЯ-001";
        ws.Cell(2, 3).Value = rank;
        wb.SaveAs(path);

        return path;
    }

    // Зіставлення колонок робить оператор на кроці 3; тут повторюємо його вибір.
    private static ImportParseResult Mapped(ImportParseResult parsed)
    {
        foreach (var column in parsed.Columns)
        {
            column.MappedField = column.Header switch
            {
                "ПІБ" => ImportTargetField.FullName,
                "Особовий номер" => ImportTargetField.ServiceNumber,
                "Звання" => ImportTargetField.Rank,
                _ => ImportTargetField.NotImported
            };
        }

        return parsed;
    }

    private static (int OldIntake, int NewIntake) SeedIntakes(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        var older = new Intake { Number = 14, DisplayNumber = "Набір №14" };
        var newer = new Intake { Number = 15, DisplayNumber = "Набір №15" };
        ctx.Intakes.AddRange(older, newer);
        ctx.SaveChanges();
        return (older.Id, newer.Id);
    }

    [Fact]
    public void SecondImportOfTheSameFile_MovesThePersonInsteadOfSkippingHer()
    {
        using var db = new TestDb();
        var (oldIntake, newIntake) = SeedIntakes(db);
        var service = new ImportService(db.Factory, new FakeAuditLog());

        // --- перший прогін: людини ще немає, вона просто додається ---
        var first = service.Import(
            Mapped(service.ParseFile(WriteWorkbook("first.xlsx", "солдат"))),
            new ImportTarget(ImportTargetKind.Intake, oldIntake));

        Assert.Equal(1, first.Imported);
        Assert.Equal(0, first.Moved);

        int recipientId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var person = Assert.Single(ctx.Recipients.ToList());
            recipientId = person.Id;
            Assert.Equal(oldIntake, person.IntakeId);
            Assert.Equal("солдат", person.Rank);
        }

        // --- другий прогін: той самий особовий номер, інший набір ---
        var parsed = Mapped(service.ParseFile(WriteWorkbook("second.xlsx", "сержант")));

        var preview = Assert.Single(service.Validate(parsed, new ImportTarget(ImportTargetKind.Intake, newIntake)));
        Assert.Equal(ImportRowStatus.Duplicate, preview.Status);
        Assert.Equal(recipientId, preview.ExistingRecipientId);

        var second = service.Import(
            parsed,
            new ImportTarget(ImportTargetKind.Intake, newIntake),
            moveRowNumbers: new[] { preview.RowNumber });

        Assert.Equal(1, second.Moved);
        Assert.Equal(0, second.Imported);
        Assert.Equal(0, second.Skipped);

        using (var ctx = db.Factory.CreateDbContext())
        {
            // Людина ОДНА: перенесення не має плодити другу картку.
            var person = Assert.Single(ctx.Recipients.ToList());
            Assert.Equal(recipientId, person.Id);
            Assert.Equal(newIntake, person.IntakeId);
            Assert.Equal("сержант", person.Rank);
        }
    }

    // Той самий файл без позначки - стара поведінка: рядок пропускається,
    // картка лишається недоторканою.
    [Fact]
    public void SecondImportWithoutTheMark_LeavesTheCardAlone()
    {
        using var db = new TestDb();
        var (oldIntake, newIntake) = SeedIntakes(db);
        var service = new ImportService(db.Factory, new FakeAuditLog());

        service.Import(
            Mapped(service.ParseFile(WriteWorkbook("first.xlsx", "солдат"))),
            new ImportTarget(ImportTargetKind.Intake, oldIntake));

        var second = service.Import(
            Mapped(service.ParseFile(WriteWorkbook("second.xlsx", "сержант"))),
            new ImportTarget(ImportTargetKind.Intake, newIntake));

        Assert.Equal(0, second.Moved);
        Assert.Equal(1, second.Skipped);

        using var ctx = db.Factory.CreateDbContext();
        var person = Assert.Single(ctx.Recipients.ToList());
        Assert.Equal(oldIntake, person.IntakeId);
        Assert.Equal("солдат", person.Rank);
    }
}
