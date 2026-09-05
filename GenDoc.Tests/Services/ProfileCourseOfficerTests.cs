using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Services;

public class ProfileCourseOfficerTests
{
    private static UserProfileService NewService(TestDb db)
        => new(db.Factory, new FakeCurrentUser(), new FakeAuditLog());

    private static Recipient? StaffMember(TestDb db, string lastName)
    {
        using var ctx = db.Factory.CreateDbContext();
        return ctx.Recipients
            .Where(r => r.IntakeId == null)
            .AsEnumerable()
            .FirstOrDefault(r => r.LastName == lastName);
    }

    [Fact]
    public void ANewProfileAppearsInThePermanentStaff()
    {
        using var db = new TestDb();

        Assert.True(NewService(db).TryCreateProfile(
            "ПЕТРЕНКО Іван Іванович", "тест-пароль", out var error));
        Assert.Null(error);

        var person = StaffMember(db, "ПЕТРЕНКО");
        Assert.NotNull(person);
        Assert.Equal("Іван", person!.FirstName);
        Assert.Equal("Іванович", person.MiddleName);
        Assert.Null(person.IntakeId);
    }

    [Fact]
    public void ANewProfileIsMarkedAsACourseOfficer()
    {
        using var db = new TestDb();
        NewService(db).TryCreateProfile("ПЕТРЕНКО Іван Іванович", "тест-пароль", out _);

        Assert.True(StaffMember(db, "ПЕТРЕНКО")!.IsCourseOfficer);
    }

    [Fact]
    public void ATwoWordNameStillWorks()
    {
        using var db = new TestDb();
        NewService(db).TryCreateProfile("ШЕВЧЕНКО Тарас", "тест-пароль", out _);

        var person = StaffMember(db, "ШЕВЧЕНКО");
        Assert.NotNull(person);
        Assert.Equal("Тарас", person!.FirstName);
        Assert.Null(person.MiddleName);
    }

    [Fact]
    public void AnExistingStaffMemberIsFlagged_NotDuplicated()
    {
        using var db = new TestDb();
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Recipients.Add(new Recipient
            {
                LastName = "ПЕТРЕНКО",
                FirstName = "Іван",
                MiddleName = "Іванович",
                Rank = "майор",
                Position = "курсовий офіцер",
                ServiceNumber = "АА-123456"
            });
            ctx.SaveChanges();
        }

        NewService(db).TryCreateProfile("ПЕТРЕНКО Іван Іванович", "тест-пароль", out _);

        using var check = db.Factory.CreateDbContext();
        var people = check.Recipients.Where(r => r.IntakeId == null).ToList();
        var person = Assert.Single(people);
        Assert.True(person.IsCourseOfficer);
        Assert.Equal("майор", person.Rank);
        Assert.Equal("АА-123456", person.ServiceNumber);
    }

    [Fact]
    public void TheNameMatchIgnoresCase()
    {
        using var db = new TestDb();
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Recipients.Add(new Recipient
            {
                LastName = "Петренко", FirstName = "Іван", MiddleName = "Іванович",
                Rank = "майор", Position = "курсовий офіцер", ServiceNumber = "АА-1"
            });
            ctx.SaveChanges();
        }

        NewService(db).TryCreateProfile("ПЕТРЕНКО ІВАН ІВАНОВИЧ", "тест-пароль", out _);

        using var check = db.Factory.CreateDbContext();
        Assert.Single(check.Recipients.Where(r => r.IntakeId == null).ToList());
    }

    [Fact]
    public void ADifferentPersonWithTheSameSurnameGetsTheirOwnCard()
    {
        using var db = new TestDb();
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Recipients.Add(new Recipient
            {
                LastName = "ПЕТРЕНКО", FirstName = "Петро", MiddleName = "Петрович",
                Rank = "капітан", Position = "викладач", ServiceNumber = "АА-2"
            });
            ctx.SaveChanges();
        }

        NewService(db).TryCreateProfile("ПЕТРЕНКО Іван Іванович", "тест-пароль", out _);

        using var check = db.Factory.CreateDbContext();
        Assert.Equal(2, check.Recipients.Count(r => r.IntakeId == null));
    }

    [Fact]
    public void ARejectedProfileLeavesNoStaffCard()
    {
        using var db = new TestDb();

        Assert.False(NewService(db).TryCreateProfile("ПЕТРЕНКО Іван", "123", out var error));
        Assert.NotNull(error);

        using var check = db.Factory.CreateDbContext();
        Assert.Empty(check.Recipients.ToList());
    }
}

public class FullNameParserTests
{
    [Fact]
    public void ThreeWordsGiveSurnameNameAndPatronymic()
    {
        var parts = FullNameParser.Split("ПЕТРЕНКО Іван Іванович");

        Assert.Equal("ПЕТРЕНКО", parts.LastName);
        Assert.Equal("Іван", parts.FirstName);
        Assert.Equal("Іванович", parts.MiddleName);
        Assert.False(parts.IsIncomplete);
    }

    [Fact]
    public void TwoWordsLeaveThePatronymicEmpty()
    {
        var parts = FullNameParser.Split("ШЕВЧЕНКО Тарас");

        Assert.Equal("ШЕВЧЕНКО", parts.LastName);
        Assert.Equal("Тарас", parts.FirstName);
        Assert.Null(parts.MiddleName);
    }

    [Fact]
    public void ExtraWordsAllGoIntoThePatronymic()
    {
        var parts = FullNameParser.Split("ПЕТРЕНКО Іван Іванович Молодший");

        Assert.Equal("Іванович Молодший", parts.MiddleName);
    }

    [Fact]
    public void OneWordIsIncomplete()
    {
        var parts = FullNameParser.Split("ПЕТРЕНКО");

        Assert.Equal("ПЕТРЕНКО", parts.LastName);
        Assert.Equal(string.Empty, parts.FirstName);
        Assert.True(parts.IsIncomplete);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public void BlankInputGivesEmptyParts(string? input)
    {
        var parts = FullNameParser.Split(input);

        Assert.Equal(string.Empty, parts.LastName);
        Assert.True(parts.IsIncomplete);
    }

    [Fact]
    public void RunsOfWhitespaceDoNotCreateEmptyParts()
    {
        var parts = FullNameParser.Split("  ПЕТРЕНКО   Іван\tІванович  ");

        Assert.Equal("ПЕТРЕНКО", parts.LastName);
        Assert.Equal("Іван", parts.FirstName);
        Assert.Equal("Іванович", parts.MiddleName);
    }
}
