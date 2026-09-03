using ClosedXML.Excel;
using GenDoc.Models;
using GenDoc.Services.Import;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Import;

public class ImportLookupScaleTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-scale-{Guid.NewGuid():N}");

    public ImportLookupScaleTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private const int RowCount = 300;
    private const int DistinctRooms = 60;
    private const int DistinctUnits = 20;

    private string WriteBigWorkbook()
    {
        var path = Path.Combine(_folder, "big.xlsx");

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Люди");
        ws.Cell(1, 1).Value = "ПІБ";
        ws.Cell(1, 2).Value = "Особовий номер";
        ws.Cell(1, 3).Value = "Підрозділ";
        ws.Cell(1, 4).Value = "Корпус";
        ws.Cell(1, 5).Value = "Кімната";

        for (var i = 0; i < RowCount; i++)
        {
            var row = i + 2;
            ws.Cell(row, 1).Value = $"ТЕСТЕНКО{i:D3} Тест Тестович";
            ws.Cell(row, 2).Value = $"ТЕСТ-МАСШТАБ-{i:D4}";
            ws.Cell(row, 3).Value = $"Рота {i % DistinctUnits}";
            ws.Cell(row, 4).Value = i % 2 == 0 ? "Корпус А" : "корпус а";
            ws.Cell(row, 5).Value = $"{100 + i % DistinctRooms}";
        }

        wb.SaveAs(path);
        return path;
    }

    private static ImportParseResult Mapped(ImportParseResult parsed)
    {
        foreach (var column in parsed.Columns)
        {
            column.MappedField = column.Header switch
            {
                "ПІБ" => ImportTargetField.FullName,
                "Особовий номер" => ImportTargetField.ServiceNumber,
                "Підрозділ" => ImportTargetField.Unit,
                "Корпус" => ImportTargetField.Building,
                "Кімната" => ImportTargetField.RoomNumber,
                _ => ImportTargetField.NotImported
            };
        }

        return parsed;
    }

    private static void SeedRoot(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        var root = new OrgNode { Name = "Корінь", Depth = 0, SortOrder = 0, Path = "/" };
        ctx.OrgNodes.Add(root);
        ctx.SaveChanges();
        root.Path = $"/{root.Id}/";
        ctx.SaveChanges();
    }

    [Fact]
    public void ABigFileCreatesEachLookupRowExactlyOnce()
    {
        using var db = new TestDb();
        SeedRoot(db);
        var service = new ImportService(db.Factory, new FakeAuditLog());

        var summary = service.Import(Mapped(service.ParseFile(WriteBigWorkbook())), ImportTarget.FromFile);

        Assert.Equal(RowCount, summary.Imported);
        Assert.Empty(summary.ErrorMessages);

        using var ctx = db.Factory.CreateDbContext();
        Assert.Equal(DistinctRooms, ctx.Rooms.Count());
        Assert.Equal(DistinctUnits, ctx.Units.Count());
        Assert.Equal(DistinctUnits + 1, ctx.OrgNodes.Count());
    }

    [Theory]
    [InlineData("Rooms")]
    [InlineData("Units")]
    public void LookupTableIsReadOnceForTheWholeFile(string table)
    {
        using var db = new TestDb(recordSql: true);
        SeedRoot(db);
        var service = new ImportService(db.Factory, new FakeAuditLog());
        var parsed = Mapped(service.ParseFile(WriteBigWorkbook()));

        db.ClearSql();
        var summary = service.Import(parsed, ImportTarget.FromFile);

        Assert.Equal(RowCount, summary.Imported);

        var selects = db.SelectCount(table);
        Assert.True(selects <= 2,
            $"SELECT до «{table}» виконався {selects} разів на {RowCount} рядків - "
            + "схоже, довідник знову вичитується не один раз.");
    }
}
