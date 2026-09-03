using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Generation;

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
