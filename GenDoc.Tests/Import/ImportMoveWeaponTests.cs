using ClosedXML.Excel;
using GenDoc.Models;
using GenDoc.Services.Import;
using GenDoc.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Tests.Import;

/// <summary>
/// ParseWeaponUnits викликався рівно в одному місці - гілці ВСТАВКИ нового
/// рядка. MoveExistingRecipient оновлював близько тридцяти полів анкети, а
/// WeaponRaw не читав узагалі, тож «Перемістити» проходило «успішно», а зброя
/// не з'являлась - і мітки зброї в шаблонах лишались порожні.
///
/// Той самий клас помилки, що з Include(Weapons) у комплектності: зброя живе в
/// окремій таблиці, і про неї забувають (аудит 2026-08-28).
/// </summary>
public class ImportMoveWeaponTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-move-weapon-{Guid.NewGuid():N}");

    public ImportMoveWeaponTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private string WriteWorkbook(string fileName, string weapon)
    {
        var path = Path.Combine(_folder, fileName);
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Люди");
        ws.Cell(1, 1).Value = "ПІБ";
        ws.Cell(1, 2).Value = "Особовий номер";
        ws.Cell(1, 3).Value = "Найменування, серія та номер особистої зброї";
        ws.Cell(2, 1).Value = "ТЕСТЕНКО Тест Тестович";
        ws.Cell(2, 2).Value = "ТЕСТ-ЗБРОЯ-001";
        ws.Cell(2, 3).Value = weapon;
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
                "Найменування, серія та номер особистої зброї" => ImportTargetField.Weapon,
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
    public void Move_WithWeaponInFile_WritesTheWeapon()
    {
        using var db = new TestDb();
        var (oldIntake, newIntake) = SeedIntakes(db);
        var service = new ImportService(db.Factory, new FakeAuditLog());

        // Перший прогін: людина без зброї.
        service.Import(
            Mapped(service.ParseFile(WriteWorkbook("first.xlsx", ""))),
            new ImportTarget(ImportTargetKind.Intake, oldIntake));

        // Другий: той самий особовий номер, тепер зі зброєю, з позначкою «Перенести».
        var parsed = Mapped(service.ParseFile(WriteWorkbook("second.xlsx", "АКМ № АБ1234")));
        var preview = Assert.Single(service.Validate(parsed, new ImportTarget(ImportTargetKind.Intake, newIntake)));

        var result = service.Import(
            parsed, new ImportTarget(ImportTargetKind.Intake, newIntake),
            moveRowNumbers: new[] { preview.RowNumber });

        Assert.Equal(1, result.Moved);

        using var ctx = db.Factory.CreateDbContext();
        var person = ctx.Recipients.Include(r => r.Weapons).Single();
        var weapon = Assert.Single(person.Weapons);
        Assert.Equal("АКМ", weapon.Name);
        Assert.Equal("АБ1234", weapon.SerialNumber);
    }

    // Порожня колонка не повинна СТИРАТИ наявну зброю: перенесення оновлює те,
    // що є у файлі, а не нищить те, чого у файлі немає. Так само поводяться всі
    // інші поля анкети (KeepNullable).
    [Fact]
    public void Move_WithEmptyWeaponColumn_KeepsExistingWeapon()
    {
        using var db = new TestDb();
        var (oldIntake, newIntake) = SeedIntakes(db);
        var service = new ImportService(db.Factory, new FakeAuditLog());

        service.Import(
            Mapped(service.ParseFile(WriteWorkbook("first.xlsx", "ПМ № ГГ7777"))),
            new ImportTarget(ImportTargetKind.Intake, oldIntake));

        var parsed = Mapped(service.ParseFile(WriteWorkbook("second.xlsx", "")));
        var preview = Assert.Single(service.Validate(parsed, new ImportTarget(ImportTargetKind.Intake, newIntake)));

        service.Import(
            parsed, new ImportTarget(ImportTargetKind.Intake, newIntake),
            moveRowNumbers: new[] { preview.RowNumber });

        using var ctx = db.Factory.CreateDbContext();
        var person = ctx.Recipients.Include(r => r.Weapons).Single();
        Assert.Equal("ПМ", Assert.Single(person.Weapons).Name);
    }

    // Зброя у файлі ЗАМІНЮЄ наявну, а не додається до неї - інакше повторні
    // імпорти множили б однакові одиниці.
    [Fact]
    public void Move_WithDifferentWeapon_ReplacesInsteadOfAppending()
    {
        using var db = new TestDb();
        var (oldIntake, newIntake) = SeedIntakes(db);
        var service = new ImportService(db.Factory, new FakeAuditLog());

        service.Import(
            Mapped(service.ParseFile(WriteWorkbook("first.xlsx", "ПМ № ГГ7777"))),
            new ImportTarget(ImportTargetKind.Intake, oldIntake));

        var parsed = Mapped(service.ParseFile(WriteWorkbook("second.xlsx", "АКМ № АБ1234")));
        var preview = Assert.Single(service.Validate(parsed, new ImportTarget(ImportTargetKind.Intake, newIntake)));

        service.Import(
            parsed, new ImportTarget(ImportTargetKind.Intake, newIntake),
            moveRowNumbers: new[] { preview.RowNumber });

        using var ctx = db.Factory.CreateDbContext();
        var person = ctx.Recipients.Include(r => r.Weapons).Single();
        Assert.Equal("АКМ", Assert.Single(person.Weapons).Name);
    }
}
