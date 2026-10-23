using ClosedXML.Excel;
using GenDoc.Models;
using GenDoc.Services.Import;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Import;

public class ImportFitnessFromMedicalBoardTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-vlk-{Guid.NewGuid():N}");

    public ImportFitnessFromMedicalBoardTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private string WriteWorkbook(bool withFitnessColumn, params (string Name, string Conclusion, string Fitness)[] people)
    {
        var path = Path.Combine(_folder, $"{Guid.NewGuid():N}.xlsx");
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Анкетні дані");
        ws.Cell(1, 1).Value = "ПІБ";
        ws.Cell(1, 2).Value = "Висновок ВЛК";
        if (withFitnessColumn) ws.Cell(1, 3).Value = "Категорія придатності";
        for (var i = 0; i < people.Length; i++)
        {
            ws.Cell(i + 2, 1).Value = people[i].Name;
            ws.Cell(i + 2, 2).Value = people[i].Conclusion;
            if (withFitnessColumn) ws.Cell(i + 2, 3).Value = people[i].Fitness;
        }
        wb.SaveAs(path);
        return path;
    }

    private static int SeedIntake(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        var intake = new Intake { Number = 30, DisplayNumber = "Набір №30" };
        ctx.Intakes.Add(intake);
        ctx.SaveChanges();
        return intake.Id;
    }

    private static Dictionary<string, string?> FitnessByLastName(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        return ctx.Recipients.ToList().ToDictionary(r => r.LastName, r => r.FitnessCategory);
    }

    [Fact]
    public void NoFitnessColumn_CategoryIsTakenFromMedicalBoardConclusion()
    {
        using var db = new TestDb();
        var intakeId = SeedIntake(db);
        var service = new ImportService(db.Factory, new FakeAuditLog());

        var parsed = service.ParseFile(WriteWorkbook(false,
            ("КОВАЛЕНКО Андрій Іванович", "придатний ", ""),
            ("ТКАЧЕНКО Ірина Олегівна", "обмежено придатна", ""),
            ("БОНДАР Олег Сергійович", "Непридатний", ""),
            ("КРАВЕЦЬ Дмитро Миколайович", "", "")));
        Assert.Equal(ImportTargetField.MedicalBoardConclusion, parsed.Columns[1].MappedField);

        var summary = service.Import(parsed, new ImportTarget(ImportTargetKind.Intake, intakeId));
        Assert.Equal(4, summary.Imported);

        var fitness = FitnessByLastName(db);
        Assert.Equal("придатний", fitness["КОВАЛЕНКО"]);
        Assert.Equal("обмежено придатний", fitness["ТКАЧЕНКО"]);
        Assert.Null(fitness["БОНДАР"]);
        Assert.Null(fitness["КРАВЕЦЬ"]);
    }

    [Fact]
    public void FitnessColumn_WinsOverMedicalBoardConclusion()
    {
        using var db = new TestDb();
        var intakeId = SeedIntake(db);
        var service = new ImportService(db.Factory, new FakeAuditLog());

        var parsed = service.ParseFile(WriteWorkbook(true,
            ("КОВАЛЕНКО Андрій Іванович", "придатний", "обмежено придатний"),
            ("ТКАЧЕНКО Ірина Олегівна", "обмежено придатна", "")));

        service.Import(parsed, new ImportTarget(ImportTargetKind.Intake, intakeId));

        var fitness = FitnessByLastName(db);
        Assert.Equal("обмежено придатний", fitness["КОВАЛЕНКО"]);
        Assert.Equal("обмежено придатний", fitness["ТКАЧЕНКО"]);
    }
}
