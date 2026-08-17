using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Generation;

// Підпис курсового офіцера більше не вибирається сам: оператор обирає людину в
// пікері, а генерація лише будує з неї рядок. Тести стережуть межу — кого
// підписантом ставити МОЖНА, а кого ні. Правило одне: курсовим офіцером може
// бути лише постійний склад (IntakeId == null), бо в наборі люди проходять
// навчання й підписувати документи не можуть.
public class CourseOfficerSignatureTests
{
    [Fact]
    public void BuildFor_ChosenPermanentStaffOfficer_ComposesSignature()
    {
        using var testDb = new TestDb();
        using var db = testDb.NewContext();

        var unit = new Unit { Name = "1 навчальна рота" };
        db.Units.Add(unit);
        db.SaveChanges();

        var officer = new Recipient
        {
            LastName = "Ковальчук",
            FirstName = "Василь",
            MiddleName = "Богданович",
            Rank = "капітан",
            IsCourseOfficer = true,
            IntakeId = null,
            UnitId = unit.Id
        };
        db.Recipients.Add(officer);
        db.SaveChanges();

        var signature = CourseOfficerSignature.BuildFor(db, officer.Id);

        Assert.Equal("Курсовий офіцер 1 навчальна рота капітан Ковальчук В. Б.", signature);
    }

    // Регресія на правило. Пікер показує лише постійний склад, але ідентифікатор
    // приходить ззовні (запам'ятаний вибір, змінена база, підміна в JSON), тож
    // перевірка мусить стояти й на самому побудовнику, а не лише на списку.
    [Fact]
    public void BuildFor_PersonInsideIntake_ReturnsNull()
    {
        using var testDb = new TestDb();
        using var db = testDb.NewContext();

        var intake = new Intake { Number = 15, DisplayNumber = "Набір №15" };
        db.Intakes.Add(intake);
        db.SaveChanges();

        var insider = new Recipient
        {
            LastName = "Ткаченко",
            FirstName = "Олег",
            Rank = "солдат",
            IsCourseOfficer = true,
            IntakeId = intake.Id
        };
        db.Recipients.Add(insider);
        db.SaveChanges();

        var signature = CourseOfficerSignature.BuildFor(db, insider.Id);

        Assert.Null(signature);
    }

    // Рядок підпису дослівно каже «Курсовий офіцер …». Поставити туди людину, з
    // якої цю ознаку зняли, означало б написати в документі неправду — тож
    // побудовник відмовляє, а не мовчки підписує посадою, якої немає.
    [Fact]
    public void BuildFor_PermanentStaffWithoutCourseOfficerFlag_ReturnsNull()
    {
        using var testDb = new TestDb();
        using var db = testDb.NewContext();

        var clerk = new Recipient
        {
            LastName = "Шевченко",
            FirstName = "Ірина",
            Rank = "старший сержант",
            IsCourseOfficer = false,
            IntakeId = null
        };
        db.Recipients.Add(clerk);
        db.SaveChanges();

        var signature = CourseOfficerSignature.BuildFor(db, clerk.Id);

        Assert.Null(signature);
    }
}
