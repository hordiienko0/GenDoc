using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Generation;

namespace GenDoc.Tests.Generation;

public class ManualDatesFromDocumentIntakeTests
{
    private const int UserId = 1;
    private const string ArrivalTag = "{{дата_прибуття}}";
    private const string EnrollmentTag = "{{дата_зарахування}}";

    private sealed record Seeded(Intake Active, Intake Finished, Intake Trashed);

    private static Seeded Seed(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Курсовий", PasswordHash = "x", CreatedAt = DateTime.Now });
        var active = new Intake
        {
            Number = 2, DisplayNumber = "Набір №2", Status = IntakeStatus.Active,
            DateStart = new DateOnly(2026, 1, 10), DateEnd = new DateOnly(2026, 4, 10)
        };
        var finished = new Intake
        {
            Number = 1, DisplayNumber = "Набір №1", Status = IntakeStatus.Completed,
            DateStart = new DateOnly(2025, 9, 1), DateEnd = new DateOnly(2025, 12, 1)
        };
        var trashed = new Intake
        {
            Number = 0, DisplayNumber = "Набір №0", Status = IntakeStatus.Completed,
            DateStart = new DateOnly(2025, 3, 5), DateEnd = new DateOnly(2025, 6, 5),
            DeletedAt = DateTime.Now
        };
        ctx.Intakes.AddRange(active, finished, trashed);
        ctx.SaveChanges();
        return new Seeded(active, finished, trashed);
    }

    private static ManualTagFormBuilder Build(TestDb db, Intake active) => TestServices.ManualTagForm(
        db, TestServices.Staff(db, UserId), new FakeCurrentUser(), new FakeIntakeAccessor(active), UserId);

    private static DateTime? Date(ManualTagFormViewModel form, string tag) =>
        form.Rows.Single(r => r.Tag == tag).DateValue;

    [Fact]
    public async Task BuildAsync_WithDocumentIntake_PrefillsDatesFromThatIntake()
    {
        using var db = new TestDb();
        var s = Seed(db);

        var form = await Build(db, s.Active).BuildAsync(
            new[] { ArrivalTag, EnrollmentTag }, "pkg:1", intakeId: s.Finished.Id);

        Assert.Equal(new DateTime(2025, 9, 1), Date(form, ArrivalTag));
        Assert.Equal(new DateTime(2025, 9, 2), Date(form, EnrollmentTag));
        Assert.Equal("1 вересня 2025 року", form.GetValues()[ArrivalTag]);
    }

    [Fact]
    public async Task BuildAsync_WithoutIntake_KeepsActiveIntakeDates()
    {
        using var db = new TestDb();
        var s = Seed(db);

        var form = await Build(db, s.Active).BuildAsync(new[] { ArrivalTag, EnrollmentTag }, "pkg:1");

        Assert.Equal(new DateTime(2026, 1, 10), Date(form, ArrivalTag));
        Assert.Equal(new DateTime(2026, 1, 11), Date(form, EnrollmentTag));
    }

    [Fact]
    public async Task BuildAsync_IntakeInTrash_StillUsesItsDates()
    {
        using var db = new TestDb();
        var s = Seed(db);

        var form = await Build(db, s.Active).BuildAsync(new[] { ArrivalTag }, "pkg:1", intakeId: s.Trashed.Id);

        Assert.Equal(new DateTime(2025, 3, 5), Date(form, ArrivalTag));
    }

    [Fact]
    public async Task BuildAsync_UnknownIntake_FallsBackToActiveIntake()
    {
        using var db = new TestDb();
        var s = Seed(db);

        var form = await Build(db, s.Active).BuildAsync(new[] { ArrivalTag }, "pkg:1", intakeId: 999);

        Assert.Equal(new DateTime(2026, 1, 10), Date(form, ArrivalTag));
    }
}
