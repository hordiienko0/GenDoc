using GenDoc.Models;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Generation;

// Список для дроплиста курсового офіцера. Пікер підписанта поруч показує ВЕСЬ
// постійний склад - тут вужче: лише ті, у кого стоїть ознака IsCourseOfficer.
// Показати оператору всіх означало б знову дозволити випадковий вибір, від
// якого й тікаємо.
public class CourseOfficerPickerTests
{
    [Fact]
    public async Task GetCourseOfficersForPickerAsync_ReturnsOnlyFlaggedPermanentStaff()
    {
        using var testDb = new TestDb();

        using (var db = testDb.NewContext())
        {
            var intake = new Intake { Number = 15, DisplayNumber = "Набір №15" };
            db.Intakes.Add(intake);
            db.SaveChanges();

            db.Recipients.AddRange(
                new Recipient { LastName = "Ковальчук", FirstName = "Василь", Rank = "капітан", IsCourseOfficer = true, IntakeId = null },
                new Recipient { LastName = "Шевченко", FirstName = "Ірина", Rank = "сержант", IsCourseOfficer = false, IntakeId = null },
                new Recipient { LastName = "Ткаченко", FirstName = "Олег", Rank = "солдат", IsCourseOfficer = true, IntakeId = intake.Id });
            db.SaveChanges();
        }

        var officers = await TestServices.Staff(testDb).GetCourseOfficersForPickerAsync();

        var officer = Assert.Single(officers);
        Assert.Equal("Ковальчук", officer.LastName);
    }
}
