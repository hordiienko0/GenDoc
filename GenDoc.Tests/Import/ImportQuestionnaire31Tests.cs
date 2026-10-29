using ClosedXML.Excel;
using GenDoc.Models;
using GenDoc.Services.Import;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Import;

// Анкета 31 набору ЗРВ (15.10-31.10.2026): заголовки й переноси рядків - як у документі, люди вигадані.
public class ImportQuestionnaire31Tests : IDisposable
{
    private static readonly string[] Headers =
    {
        "№ з/п",
        "Військове звання",
        "ПІБ",
        "Дата \nнародження",
        "Національність",
        "Освіта цивільна",
        "Освіта військова\n (ВОС)",
        "ВОС",
        "З якого часу в ЗСУ",
        "яким РТЦТК та СП\n призваний",
        "Сімейний стан\n (ПІП дружини рік народження; ім’я і рік народження дитини)",
        "Адреса реєстрації",
        "Адреса фактичного проживання",
        "телефон",
        "Примітка",
        "ІПН",
        "Паспорт",
        "Місце народження",
        "Довідка\nВЛК № \nвід якої дати",
        "Наказ про присвоєння в/звання",
        "ПІБ на іноземній мові",
        "Служив/не служив",
        "Примітка"
    };

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-q31-{Guid.NewGuid():N}");

    public ImportQuestionnaire31Tests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static ImportTargetField[] MapAll()
    {
        var noteAssigned = false;
        return Headers.Select(h => ImportService.AutoMapHeader(h, ref noteAssigned)).ToArray();
    }

    [Fact]
    public void EveryColumnOfTheForm_MapsToItsOwnField()
    {
        Assert.Equal(new[]
        {
            ImportTargetField.NotImported,
            ImportTargetField.Rank,
            ImportTargetField.FullName,
            ImportTargetField.DateOfBirth,
            ImportTargetField.Nationality,
            ImportTargetField.CivilEducation,
            ImportTargetField.MilitaryEducation,
            ImportTargetField.Vos,
            ImportTargetField.ArmedForcesSince,
            ImportTargetField.ConscriptionOffice,
            ImportTargetField.MaritalStatus,
            ImportTargetField.RegistrationAddress,
            ImportTargetField.ResidenceAddress,
            ImportTargetField.Phone,
            ImportTargetField.Note,
            ImportTargetField.TaxNumber,
            ImportTargetField.Passport,
            ImportTargetField.PlaceOfBirth,
            ImportTargetField.MedicalBoard,
            ImportTargetField.RankOrder,
            ImportTargetField.NameTransliterated,
            ImportTargetField.ServedBefore,
            ImportTargetField.ExtraNote
        }, MapAll());
    }

    private string WriteWorkbook(Action<IXLWorksheet> fillRows)
    {
        var path = Path.Combine(_folder, $"{Guid.NewGuid():N}.xlsx");
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Рудки");
        for (var c = 0; c < Headers.Length; c++) ws.Cell(1, c + 1).Value = Headers[c];
        fillRows(ws);
        wb.SaveAs(path);
        return path;
    }

    private static void SetDate(IXLCell cell, DateTime value)
    {
        cell.Value = value;
        cell.Style.DateFormat.Format = "dd.mm.yyyy";
    }

    private static void FillTestPerson(IXLWorksheet ws, int row, string fullName, string medicalBoard)
    {
        ws.Cell(row, 1).Value = row - 1;
        ws.Cell(row, 2).Value = "молодший лейтенант";
        ws.Cell(row, 3).Value = fullName;
        SetDate(ws.Cell(row, 4), new DateTime(1990, 3, 14));
        ws.Cell(row, 5).Value = "українець";
        ws.Cell(row, 6).Value = "ТНУ у 2012 році";
        ws.Cell(row, 7).Value = "ВП при ТНУ у 2012 р. ВОС 100100";
        SetDate(ws.Cell(row, 9), new DateTime(2026, 10, 15));
        ws.Cell(row, 10).Value = "Тестовий РТЦК та СП, м. Тестів";
        ws.Cell(row, 11).Value = "одружений, дружина - Тестенко Олена Іванівна\n01.01.1991\nДіти - син Іван 02.02.2015";
        ws.Cell(row, 12).Value = "Тестівська обл., м. Тестів, вул. Перша 1";
        ws.Cell(row, 13).Value = "м. Тестів, вул. Друга 2, кв. 3";
        ws.Cell(row, 14).Value = "0000000001";
        ws.Cell(row, 16).Value = "0000000000";
        ws.Cell(row, 17).Value = "паспорт ТТ 000000 виданий Тестівським РВ 01 січня 2006 р";
        ws.Cell(row, 18).Value = "м. Тестів, Тестівська обл";
        ws.Cell(row, 19).Value = medicalBoard;
        ws.Cell(row, 20).Value = "МОУ № 1 від 01.01.2012";
    }

    private static int SeedIntake(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        var intake = new Intake { Number = 31, DisplayNumber = "Набір №31" };
        ctx.Intakes.Add(intake);
        ctx.SaveChanges();
        return intake.Id;
    }

    [Fact]
    public void Import_FillsEveryFieldOfTheForm()
    {
        using var db = new TestDb();
        var intakeId = SeedIntake(db);
        var service = new ImportService(db.Factory, new FakeAuditLog());

        var parsed = service.ParseFile(WriteWorkbook(ws =>
            FillTestPerson(ws, 2, "ТЕСТЕНКО Петро Іванович",
                "№2026-0000-0000-0000-0 ВЛК при Тестовому РТЦК та СП від 15.10.2026 р. - придатний до в/с")));
        var preview = Assert.Single(service.Validate(parsed, new ImportTarget(ImportTargetKind.Intake, intakeId)));
        Assert.NotEqual(ImportRowStatus.Error, preview.Status);

        var summary = service.Import(parsed, new ImportTarget(ImportTargetKind.Intake, intakeId));
        Assert.Equal(1, summary.Imported);

        using var ctx = db.Factory.CreateDbContext();
        var r = Assert.Single(ctx.Recipients.ToList());
        Assert.Equal("ТЕСТЕНКО", r.LastName);
        Assert.Equal("Петро", r.FirstName);
        Assert.Equal("Іванович", r.MiddleName);
        Assert.Equal("молодший лейтенант", r.Rank);
        Assert.Equal(new DateOnly(1990, 3, 14), r.DateOfBirth);
        Assert.Equal("українець", r.Nationality);
        Assert.Equal("ТНУ у 2012 році", r.CivilEducation);
        Assert.Equal("ВП при ТНУ у 2012 р. ВОС 100100", r.MilitaryEducation);
        Assert.Null(r.Vos);
        Assert.Equal(new DateOnly(2026, 10, 15), r.ArmedForcesSince);
        Assert.Equal("Тестовий РТЦК та СП, м. Тестів", r.ConscriptionOffice);
        Assert.StartsWith("одружений, дружина - Тестенко Олена Іванівна", r.MaritalStatus);
        Assert.Contains("Діти - син Іван", r.MaritalStatus);
        Assert.Equal("Тестівська обл., м. Тестів, вул. Перша 1", r.RegistrationAddress);
        Assert.Equal("м. Тестів, вул. Друга 2, кв. 3", r.ResidenceAddress);
        Assert.Equal("0000000001", r.Phone);
        Assert.Equal("0000000000", r.TaxNumber);
        Assert.StartsWith("паспорт ТТ 000000", r.Passport);
        Assert.Equal("м. Тестів, Тестівська обл", r.PlaceOfBirth);
        Assert.StartsWith("№2026-0000-0000-0000-0 ВЛК", r.MedicalBoard);
        Assert.Equal("МОУ № 1 від 01.01.2012", r.RankOrder);
        Assert.Null(r.Note);
        Assert.Null(r.ExtraNote);
    }

    [Fact]
    public void Fitness_IsReadFromTheEndOfTheMedicalBoardCertificate()
    {
        using var db = new TestDb();
        var intakeId = SeedIntake(db);
        var service = new ImportService(db.Factory, new FakeAuditLog());

        var parsed = service.ParseFile(WriteWorkbook(ws =>
        {
            FillTestPerson(ws, 2, "ПЕРШЕНКО Іван Іванович", "№1 ВЛК при ТЦК від 15.10.2026 р. - придатний до в/с");
            FillTestPerson(ws, 3, "ДРУГЕНКО Іван Іванович", "№2 ВЛК при ТЦК від 16.10.2026 р. - обмежено придатний");
            FillTestPerson(ws, 4, "ТРЕТЕНКО Іван Іванович", "№3 Додаткова позаштатна ВЛК при ТЦК");
        }));
        service.Import(parsed, new ImportTarget(ImportTargetKind.Intake, intakeId));

        using var ctx = db.Factory.CreateDbContext();
        var fitness = ctx.Recipients.ToList().ToDictionary(r => r.LastName, r => r.FitnessCategory);
        Assert.Equal("придатний", fitness["ПЕРШЕНКО"]);
        Assert.Equal("обмежено придатний", fitness["ДРУГЕНКО"]);
        Assert.Null(fitness["ТРЕТЕНКО"]);
    }

    [Fact]
    public void NumberedBlankRowsOfTheForm_AreNotRowsAtAll()
    {
        using var db = new TestDb();
        var service = new ImportService(db.Factory, new FakeAuditLog());

        var parsed = service.ParseFile(WriteWorkbook(ws =>
        {
            FillTestPerson(ws, 2, "ТЕСТЕНКО Петро Іванович", "ВЛК - придатний");
            ws.Cell(3, 1).Value = 2;
            ws.Cell(4, 1).Value = 3;
        }));

        var preview = Assert.Single(service.Validate(parsed));
        Assert.Equal(ImportRowStatus.Ok, preview.Status);

        var summary = service.Import(parsed);
        Assert.Equal((1, 0, 0), (summary.Imported, summary.Skipped, summary.Errors));
    }

    [Fact]
    public void UnreadableArmedForcesDate_IsAWarningNotAnError()
    {
        using var db = new TestDb();
        var service = new ImportService(db.Factory, new FakeAuditLog());

        var parsed = service.ParseFile(WriteWorkbook(ws =>
        {
            FillTestPerson(ws, 2, "ТЕСТЕНКО Петро Іванович", "ВЛК - придатний");
            ws.Cell(2, 9).Value = "з осені 2026";
        }));

        var preview = Assert.Single(service.Validate(parsed));
        Assert.Equal(ImportRowStatus.Warning, preview.Status);
        Assert.Contains("З якого часу в ЗСУ", preview.Note);
    }
}
