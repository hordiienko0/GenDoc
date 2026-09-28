using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Generation;

/// <summary>
/// Стройова записка курсів: КППК - постійний склад, БЗВП - прикомандировані люди набору; відсутні - за подіями
/// постійного складу, відкритими на «Дату документа».
/// </summary>
public class StrengthReportTests
{
    private static readonly DateOnly ReportDate = new(2026, 8, 5);

    private static (TestDb Db, List<Recipient> Intake) Seed()
    {
        var db = new TestDb();
        using var ctx = db.Factory.CreateDbContext();
        var intakeEntity = new Intake { Number = 30, DisplayNumber = "Набір №30", Status = IntakeStatus.Active,
            DateStart = new DateOnly(2026, 7, 15), DateEnd = new DateOnly(2026, 9, 30) };
        ctx.Intakes.Add(intakeEntity);
        ctx.SaveChanges();

        Recipient Staff(string last, string first, string rank) => new()
        {
            LastName = last, FirstName = first, MiddleName = "Петрович", Rank = rank, Position = "курсовий офіцер", IsCourseOfficer = true
        };

        var onTrip = Staff("КОВАЛЬЧУК", "Василь", "капітан");
        var sick = Staff("ІВАНЕНКО", "Іван", "майор");
        var onLeave = Staff("ГОНЧАР", "Юрій", "капітан");
        var present = Staff("ДУБ", "Олег", "майор");
        var leaveEnded = Staff("ЛИС", "Петро", "лейтенант");
        ctx.Recipients.AddRange(onTrip, sick, onLeave, present, leaveEnded);

        var intake = Enumerable.Range(1, 4).Select(i => new Recipient
        {
            LastName = $"СЛУХАЧ{i}", FirstName = "Андрій", Rank = "лейтенант", Position = "слухач", IntakeId = intakeEntity.Id
        }).ToList();
        ctx.Recipients.AddRange(intake);
        ctx.SaveChanges();

        StaffEvent Event(Recipient r, StaffEventKind kind, DateOnly from, DateOnly to) =>
            new() { RecipientId = r.Id, Kind = kind, DateStart = from, DateEnd = to, CreatedAt = DateTime.Now };

        ctx.StaffEvents.AddRange(
            Event(onTrip, StaffEventKind.BusinessTrip, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 10)),
            Event(sick, StaffEventKind.Sick, new DateOnly(2026, 8, 5), new DateOnly(2026, 8, 5)),
            Event(onLeave, StaffEventKind.Leave, new DateOnly(2026, 8, 4), new DateOnly(2026, 8, 20)),
            Event(leaveEnded, StaffEventKind.Leave, new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 4)));
        ctx.SaveChanges();

        return (db, intake);
    }

    [Fact]
    public void Build_CountsStaffAsKppk_IntakeAsBzvp_AndAbsencesOnTheDocumentDate()
    {
        var (db, intake) = Seed();
        using (db)
        using (var ctx = db.Factory.CreateDbContext())
        {
            var values = StrengthReport.Build(ctx, intake, ReportDate);

            Assert.Equal("5", values["{{за_списком_кппк}}"]);
            Assert.Equal("4", values["{{за_списком_бзвп}}"]);
            Assert.Equal("9", values["{{за_списком_загалом}}"]);

            Assert.Equal("1", values["{{відрядження_кппк}}"]);
            Assert.Equal("1", values["{{шпиталь_кппк}}"]);
            Assert.Equal("1", values["{{відпустка_кппк}}"]);
            Assert.Equal("2", values["{{у_строю_кппк}}"]);
            Assert.Equal("4", values["{{у_строю_бзвп}}"]);
            Assert.Equal("6", values["{{у_строю_загалом}}"]);
            Assert.Equal("3", values["{{відсутні_загалом}}"]);

            Assert.Equal("капітан КОВАЛЬЧУК В. П.", values["{{відрядження}}"]);
            Assert.Equal("майор ІВАНЕНКО І. П.", values["{{шпиталь}}"]);
            Assert.Equal("капітан ГОНЧАР Ю. П.", values["{{відпустка}}"]);
        }
    }

    [Fact]
    public void Build_NobodyAbsent_ListsSayNobody()
    {
        using var db = new TestDb();
        using var ctx = db.Factory.CreateDbContext();

        var values = StrengthReport.Build(ctx, Array.Empty<Recipient>(), ReportDate);

        Assert.Equal("0", values["{{за_списком_загалом}}"]);
        Assert.Equal("немає", values["{{відрядження}}"]);
    }

    [Fact]
    public void DocumentDate_ComesFromTheManualValues()
    {
        var values = new Dictionary<string, string> { ["{{дата}}"] = "05.08.2026" };

        Assert.Equal(ReportDate, GenerationService.DocumentDate(values));
    }
}
