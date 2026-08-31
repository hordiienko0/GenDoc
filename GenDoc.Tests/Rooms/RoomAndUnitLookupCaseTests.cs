using ClosedXML.Excel;
using GenDoc.Models;
using GenDoc.Services.Import;
using GenDoc.Services.Personnel;
using GenDoc.Services.Recipients;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Rooms;

// Пошук наявної кімнати (і підрозділу) робився ЗАПИТОМ у SQLite: r.Building == b.
// SQLite порівнює рядки побайтово, а NOCASE знає лише латиницю - тож «Корпус А»
// і «корпус а» для бази різні. Кожен другий запис кімнати з іншим регістром
// створював ДУБЛЬ, і люди в одній кімнаті опинялись у двох різних.
// Правило гілки: порівняння кирилиці без урахування регістру - у пам'яті,
// через uk-UA (аудит 2026-08-28).
public class RoomAndUnitLookupCaseTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-rooms-{Guid.NewGuid():N}");

    public RoomAndUnitLookupCaseTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static int SeedRoom(TestDb db, string building, string number)
    {
        using var ctx = db.Factory.CreateDbContext();
        var room = new Room { Building = building, Number = number, Capacity = 6 };
        ctx.Rooms.Add(room);
        ctx.SaveChanges();
        return room.Id;
    }

    private static int SeedRootNode(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        var root = new OrgNode { Name = "Корінь", Depth = 0, SortOrder = 0, Path = "/" };
        ctx.OrgNodes.Add(root);
        ctx.SaveChanges();
        root.Path = $"/{root.Id}/";
        ctx.SaveChanges();
        return root.Id;
    }

    [Fact]
    public async Task PersonnelService_ReusesTheRoomWrittenInAnotherCase()
    {
        using var db = new TestDb();
        var roomId = SeedRoom(db, "Корпус А", "101");
        var nodeId = SeedRootNode(db);
        var service = new PersonnelService(db.Factory, new FakeAuditLog(), new FakeCurrentUser());

        var result = await service.SaveAsync(new PersonEditModel
        {
            LastName = "ТЕСТЕНКО",
            FirstName = "Тест",
            Rank = "солдат",
            Position = "курсант",
            ServiceNumber = "ТЕСТ-КІМНАТА-001",
            RoomBuilding = "корпус а",
            RoomNumber = "101",
            OrgNodeId = nodeId
        });

        Assert.True(result.Success);
        using var ctx = db.Factory.CreateDbContext();
        Assert.Single(ctx.Rooms.ToList());
        Assert.Equal(roomId, Assert.Single(ctx.Recipients.ToList()).RoomId);
    }

    [Fact]
    public void RecipientService_ReusesTheRoomWrittenInAnotherCase()
    {
        using var db = new TestDb();
        var roomId = SeedRoom(db, "Корпус Б", "202");
        var service = new RecipientService(db.Factory, new FakeAuditLog(), new FakeCurrentUser());

        service.Save(new RecipientEditModel
        {
            LastName = "ТЕСТЕНКО",
            FirstName = "Тест",
            Rank = "солдат",
            Position = "курсант",
            ServiceNumber = "ТЕСТ-КІМНАТА-002",
            RoomBuilding = "КОРПУС б",
            RoomNumber = "202"
        }, out var error);

        Assert.Null(error);
        using var ctx = db.Factory.CreateDbContext();
        Assert.Single(ctx.Rooms.ToList());
        Assert.Equal(roomId, Assert.Single(ctx.Recipients.ToList()).RoomId);
    }

    // Та сама помилка в сусідньому методі того ж сервісу: підрозділ шукався
    // запитом u.Name == name.
    [Fact]
    public void RecipientService_ReusesTheUnitWrittenInAnotherCase()
    {
        using var db = new TestDb();
        int unitId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var unit = new Unit { Name = "Перша Рота" };
            ctx.Units.Add(unit);
            ctx.SaveChanges();
            unitId = unit.Id;
        }

        var service = new RecipientService(db.Factory, new FakeAuditLog(), new FakeCurrentUser());
        service.Save(new RecipientEditModel
        {
            LastName = "ТЕСТЕНКО",
            FirstName = "Тест",
            Rank = "солдат",
            Position = "курсант",
            ServiceNumber = "ТЕСТ-ПІДРОЗДІЛ-001",
            UnitName = "перша рота"
        }, out var error);

        Assert.Null(error);
        using var check = db.Factory.CreateDbContext();
        Assert.Single(check.Units.ToList());
        Assert.Equal(unitId, Assert.Single(check.Recipients.ToList()).UnitId);
    }

    [Fact]
    public void Import_ReusesTheRoomWrittenInAnotherCase()
    {
        var path = Path.Combine(_folder, "rooms.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Люди");
            ws.Cell(1, 1).Value = "ПІБ";
            ws.Cell(1, 2).Value = "Корпус";
            ws.Cell(1, 3).Value = "Кімната";
            ws.Cell(2, 1).Value = "ТЕСТЕНКО Тест Тестович";
            ws.Cell(2, 2).Value = "корпус в";
            ws.Cell(2, 3).Value = "303";
            wb.SaveAs(path);
        }

        using var db = new TestDb();
        var roomId = SeedRoom(db, "Корпус В", "303");
        var service = new ImportService(db.Factory, new FakeAuditLog());
        var parsed = service.ParseFile(path);
        foreach (var column in parsed.Columns)
        {
            column.MappedField = column.Header switch
            {
                "ПІБ" => ImportTargetField.FullName,
                "Корпус" => ImportTargetField.Building,
                "Кімната" => ImportTargetField.RoomNumber,
                _ => ImportTargetField.NotImported
            };
        }

        var summary = service.Import(parsed, ImportTarget.FromFile);

        Assert.Equal(1, summary.Imported);
        using var ctx = db.Factory.CreateDbContext();
        Assert.Single(ctx.Rooms.ToList());
        Assert.Equal(roomId, Assert.Single(ctx.Recipients.ToList()).RoomId);
    }

    // Різні кімнати не зливаються: правило про регістр, а не про схожість.
    [Fact]
    public void DifferentRoomsStayDifferent()
    {
        using var db = new TestDb();
        SeedRoom(db, "Корпус А", "101");
        var service = new RecipientService(db.Factory, new FakeAuditLog(), new FakeCurrentUser());

        service.Save(new RecipientEditModel
        {
            LastName = "ТЕСТЕНКО",
            FirstName = "Тест",
            Rank = "солдат",
            Position = "курсант",
            ServiceNumber = "ТЕСТ-КІМНАТА-003",
            RoomBuilding = "Корпус А",
            RoomNumber = "102"
        }, out var error);

        Assert.Null(error);
        using var ctx = db.Factory.CreateDbContext();
        Assert.Equal(2, ctx.Rooms.Count());
    }
}
