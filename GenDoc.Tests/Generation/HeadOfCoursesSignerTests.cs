using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Generation;

public class HeadOfCoursesSignerTests
{
    private static readonly string[] SignerTags = { "{{звання_підписанта}}", "{{піб_підписанта}}" };

    private static ManualTagFormBuilder Build(TestDb db, string currentUser = "Тест Тестович") => TestServices.ManualTagForm(
        db, TestServices.Staff(db), new FakeCurrentUser(fullName: currentUser), new FakeIntakeAccessor());

    private static void Seed(TestDb db, bool withHead = true)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.AppSettings.Add(new AppSettings());
        if (withHead)
        {
            ctx.OrganizationSettings.Add(new OrganizationSettings
            {
                CommanderRank = "полковник",
                CommanderFullName = "Іван ПЕТРЕНКО",
                CommanderPosition = "Начальник курсів підвищення кваліфікації"
            });
        }
        ctx.Recipients.AddRange(
            new Recipient { LastName = "Мельник", FirstName = "Петро", Rank = "майор", IsCourseOfficer = true },
            new Recipient { LastName = "Ковальчук", FirstName = "Василь", Rank = "капітан" });
        ctx.SaveChanges();
    }

    [Fact]
    public async Task Signer_DefaultsToHeadOfCoursesFromSettings_EvenWhenCurrentUserIsStaff()
    {
        using var db = new TestDb();
        Seed(db);

        var form = await Build(db, currentUser: "Мельник Петро").BuildAsync(SignerTags, "pkg:1");

        Assert.NotNull(form.Signer?.Selected);
        Assert.Equal(HeadOfCourses.PickerId, form.Signer!.Selected!.RecipientId);

        var values = form.GetValues();
        Assert.Equal("полковник", values["{{звання_підписанта}}"]);
        Assert.Equal("Іван ПЕТРЕНКО", values["{{піб_підписанта}}"]);
    }

    [Fact]
    public async Task Signer_OffersHeadFirstThenCourseOfficersOnly()
    {
        using var db = new TestDb();
        Seed(db);

        var form = await Build(db).BuildAsync(SignerTags, "pkg:1");

        var labels = form.Signer!.Options.Select(o => o.DisplayLabel).ToList();
        Assert.Equal(2, labels.Count);
        Assert.Contains("ПЕТРЕНКО", labels[0], StringComparison.Ordinal);
        Assert.Contains("МЕЛЬНИК", labels[1], StringComparison.Ordinal);
        Assert.DoesNotContain(labels, l => l.Contains("КОВАЛЬЧУК", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Signer_ChosenCourseOfficerIsWrittenIntoSignerTags()
    {
        using var db = new TestDb();
        Seed(db);

        var form = await Build(db).BuildAsync(SignerTags, "pkg:1");
        form.Signer!.Selected = form.Signer.Options.Single(o => o.RecipientId != HeadOfCourses.PickerId);

        var values = form.GetValues();
        Assert.Equal("майор", values["{{звання_підписанта}}"]);
        Assert.Equal("Петро МЕЛЬНИК", values["{{піб_підписанта}}"]);
    }

    [Fact]
    public async Task Signer_WithoutHeadAndCourseOfficers_FallsBackToWholePermanentStaff()
    {
        using var db = new TestDb();
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.AppSettings.Add(new AppSettings());
            ctx.Recipients.Add(new Recipient { LastName = "Ковальчук", FirstName = "Василь", Rank = "капітан" });
            ctx.SaveChanges();
        }

        var form = await Build(db).BuildAsync(SignerTags, "pkg:1");

        Assert.Single(form.Signer!.Options);
        Assert.NotNull(form.Signer.Selected);
    }

    [Fact]
    public async Task CourseOfficerPicker_IncludesHeadOfCourses()
    {
        using var db = new TestDb();
        Seed(db);

        var form = await Build(db).BuildAsync(Array.Empty<string>(), "pkg:1", needsCourseOfficer: true);

        var head = Assert.Single(form.CourseOfficer!.Options, o => o.RecipientId == HeadOfCourses.PickerId);
        Assert.Equal("Начальник курсів підвищення кваліфікації полковник Іван ПЕТРЕНКО", head.SignaturePreview);
        Assert.Contains(form.CourseOfficer.Options, o => o.DisplayLabel.Contains("МЕЛЬНИК", StringComparison.Ordinal));
    }

    [Fact]
    public void Signature_ForHeadPickerId_ComposedFromSettings()
    {
        using var testDb = new TestDb();
        Seed(testDb);
        using var db = testDb.NewContext();

        Assert.Equal("Начальник курсів підвищення кваліфікації полковник Іван ПЕТРЕНКО",
            CourseOfficerSignature.BuildFor(db, HeadOfCourses.PickerId));
    }

    [Fact]
    public void Signature_WithoutCourseOfficers_FallsBackToHead()
    {
        using var testDb = new TestDb();
        using (var ctx = testDb.Factory.CreateDbContext())
        {
            ctx.OrganizationSettings.Add(new OrganizationSettings
            {
                CommanderRank = "полковник", CommanderFullName = "Іван ПЕТРЕНКО", CommanderPosition = ""
            });
            ctx.SaveChanges();
        }
        using var db = testDb.NewContext();

        Assert.Equal("Начальник курсів полковник Іван ПЕТРЕНКО", CourseOfficerSignature.Build(db));
    }
}
