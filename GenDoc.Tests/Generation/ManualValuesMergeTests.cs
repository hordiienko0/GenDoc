using System.Text.Json;
using GenDoc.Models;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Generation;

/// <summary>
/// Пер-профільні ручні значення (v26) мусять ЗЛИВАТИСЬ із глобальними по
/// ключах, а не підмінювати їх блобом.
///
/// Регресія, знайдена аудитом 2026-08-28: читання відкочувалось на
/// AppSettings лише тоді, коли профільний JSON порожній ЦІЛКОМ. Тобто перший
/// же збережений тег («Дата наказу») назавжди затіняв усе, що оператор
/// накопичив до переходу на v26 - решта полів поверталась порожньою, а
/// передвибір підписанта злітав для всіх шаблонів, крім останнього.
/// </summary>
public class ManualValuesMergeTests
{
    private const int UserId = 1;

    private static ManualTagFormBuilder Build(TestDb db) => TestServices.ManualTagForm(
        db, TestServices.Staff(db, UserId), new FakeCurrentUser(), new FakeIntakeAccessor(), UserId);

    private static void Seed(TestDb db, string? globalJson, string? userJson,
        string? globalSigners = null, string? userSigners = null)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Курсовий", PasswordHash = "x", CreatedAt = DateTime.Now });
        ctx.AppSettings.Add(new AppSettings
        {
            LastManualValuesJson = globalJson,
            LastSignerByTemplateJson = globalSigners
        });
        ctx.SaveChanges();

        ctx.UserSettings.Add(new UserSettings
        {
            UserProfileId = UserId,
            LastManualValuesJson = userJson,
            LastSignerByTemplateJson = userSigners
        });
        ctx.SaveChanges();
    }

    [Fact]
    public async Task Prefill_UserHasOneKey_GlobalKeysAreStillOffered()
    {
        using var db = new TestDb();
        Seed(db,
            globalJson: JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["{{номер_наказу}}"] = "77",
                ["{{місто}}"] = "Харків"
            }),
            userJson: JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["{{підстава}}"] = "рапорт"
            }));

        var form = await Build(db).BuildAsync(
            new[] { "{{номер_наказу}}", "{{місто}}", "{{підстава}}" }, "pkg:1");

        Assert.Equal("77", form.Rows.Single(r => r.Tag == "{{номер_наказу}}").Value);
        Assert.Equal("Харків", form.Rows.Single(r => r.Tag == "{{місто}}").Value);
        Assert.Equal("рапорт", form.Rows.Single(r => r.Tag == "{{підстава}}").Value);
    }

    [Fact]
    public async Task Prefill_UserValueWinsOverGlobalForTheSameKey()
    {
        using var db = new TestDb();
        Seed(db,
            globalJson: JsonSerializer.Serialize(new Dictionary<string, string> { ["{{місто}}"] = "Харків" }),
            userJson: JsonSerializer.Serialize(new Dictionary<string, string> { ["{{місто}}"] = "Полтава" }));

        var form = await Build(db).BuildAsync(new[] { "{{місто}}" }, "pkg:1");

        Assert.Equal("Полтава", Assert.Single(form.Rows).Value);
    }

    // Той самий розрив для підписантів: збереження вибору для одного шаблону
    // скидало передвибір для всіх інших.
    [Fact]
    public async Task Signer_UserChoiceForOneTemplate_KeepsGlobalChoiceForAnother()
    {
        using var db = new TestDb();
        Seed(db,
            globalJson: null, userJson: null,
            // Курсовий офіцер пам'ятається під власним ключем «<контекст>#курсовий»,
            // щоб вибір цієї ролі не перебивав підписанта документа.
            globalSigners: JsonSerializer.Serialize(new Dictionary<string, int> { ["tpl:5#курсовий"] = 2 }),
            userSigners: JsonSerializer.Serialize(new Dictionary<string, int> { ["tpl:9#курсовий"] = 1 }));

        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Recipients.AddRange(
                new Recipient { LastName = "Ковальчук", FirstName = "Василь", Rank = "капітан", IsCourseOfficer = true },
                new Recipient { LastName = "Мельник", FirstName = "Петро", Rank = "майор", IsCourseOfficer = true });
            ctx.SaveChanges();
        }

        var form = await Build(db).BuildAsync(Array.Empty<string>(), "tpl:5", needsCourseOfficer: true);

        Assert.NotNull(form.CourseOfficer?.Selected);
        Assert.Equal(2, form.CourseOfficer!.Selected!.RecipientId);
    }
}
