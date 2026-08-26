using GenDoc.Models;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Generation;

// Дропліст мусить стартувати з КИМОСЬ обраним. Порожній вибір поруч із
// запасним авто-вибором у генерації означав би, що підписанта знову визначає
// база, а не оператор - тобто саме та поведінка, яку прибирали.
public class CourseOfficerPreselectionTests
{
    private static ManualTagFormBuilder Build(TestDb db) => TestServices.ManualTagForm(
        db, TestServices.Staff(db), new FakeCurrentUser(), new FakeIntakeAccessor());

    [Fact]
    public async Task BuildAsync_SeveralCourseOfficersAndNoHistory_PreselectsFirst()
    {
        using var db = new TestDb();

        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.AppSettings.Add(new GenDoc.Models.AppSettings());
            ctx.Recipients.AddRange(
                new Recipient { LastName = "Мельник", FirstName = "Петро", Rank = "майор", IsCourseOfficer = true },
                new Recipient { LastName = "Ковальчук", FirstName = "Василь", Rank = "капітан", IsCourseOfficer = true });
            ctx.SaveChanges();
        }

        var form = await Build(db).BuildAsync(
            Array.Empty<string>(), "pkg:1", needsCourseOfficer: true);

        Assert.NotNull(form.CourseOfficer);
        Assert.Equal(2, form.CourseOfficer!.Options.Count);

        // Перелік упорядкований за прізвищем, тож першим стоїть Ковальчук.
        // SignatureName пише прізвище великими - «Василь КОВАЛЬЧУК».
        Assert.NotNull(form.CourseOfficer.Selected);
        Assert.Contains("КОВАЛЬЧУК", form.CourseOfficer.Selected!.DisplayLabel, StringComparison.Ordinal);
    }

    // Те саме правило для підписанта документа. Порожній пікер не давав жодного
    // значення тегам, і документ виходив із порожнім місцем підпису - рівно та
    // вада, проти якої заводився дропліст курсового офіцера.
    [Fact]
    public async Task BuildAsync_SignerPickerIsNeverLeftEmptyWhenStaffExists()
    {
        using var db = new TestDb();

        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.AppSettings.Add(new GenDoc.Models.AppSettings());
            ctx.Recipients.AddRange(
                new Recipient { LastName = "Мельник", FirstName = "Петро", Rank = "майор" },
                new Recipient { LastName = "Ковальчук", FirstName = "Василь", Rank = "капітан" });
            ctx.SaveChanges();
        }

        var form = await Build(db).BuildAsync(
            new[] { "{{звання_підписанта}}", "{{піб_підписанта}}" }, "pkg:1");

        Assert.NotNull(form.Signer);
        Assert.NotNull(form.Signer!.Selected);

        // І значення мусить лягти під ключем із дужками - саме його шукає генерація.
        var values = form.GetValues();
        Assert.True(values.ContainsKey("{{звання_підписанта}}"));
        Assert.True(values.ContainsKey("{{піб_підписанта}}"));
    }
}
