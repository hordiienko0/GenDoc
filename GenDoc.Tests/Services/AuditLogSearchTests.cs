using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Services.Audit;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Services;

public class AuditLogSearchTests
{
    private static AuditLogQueryService Seed(TestDb db)
    {
        using (var ctx = db.Factory.CreateDbContext())
        {
            var stamp = new DateTime(2026, 8, 20, 10, 38, 0);
            ctx.AuditLog.AddRange(
                new AuditLogEntry
                {
                    OccurredAt = stamp, Action = "Перегенеровано", EntityName = "GeneratedDocument", EntityId = 57,
                    UserProfileName = "Курсовий", Details = "КОВАЛЬЧУК · Рапорт · в.2"
                },
                new AuditLogEntry
                {
                    OccurredAt = stamp.AddMinutes(1), Action = "Видалено документ", EntityName = "GeneratedDocument", EntityId = 58,
                    UserProfileName = "Курсовий", OldValue = "ШЕВЧЕНКО · Рапорт · в.1"
                },
                new AuditLogEntry
                {
                    OccurredAt = stamp.AddMinutes(2), Action = "Створено набір", EntityName = "Intake", EntityId = 1,
                    UserProfileName = "Курсовий", NewValue = "Набір №4", Details = "5 папок"
                },
                new AuditLogEntry
                {
                    OccurredAt = stamp.AddMinutes(3), Action = "Надруковано документ", EntityName = "GeneratedGroupDocument", EntityId = 3,
                    UserProfileName = "Курсовий", NewValue = "zalik.xlsx"
                });
            ctx.SaveChanges();
        }

        return new AuditLogQueryService(db.Factory);
    }

    private static AuditLogFilter Filter(string? search = null) => new(null, null, null, null, 0, 50, search);

    [Fact]
    public void EntityNames_AreHumanReadable()
    {
        using var db = new TestDb();
        var rows = Seed(db).Query(Filter());

        Assert.Contains(rows, r => r.ObjectDisplay == "Документ, №57");
        Assert.Contains(rows, r => r.ObjectDisplay == "Груповий документ, №3");
        Assert.Contains(rows, r => r.ObjectDisplay == "Набір, №1");
        Assert.DoesNotContain(rows, r => r.ObjectDisplay.Contains("GeneratedDocument"));
    }

    [Fact]
    public void Search_LooksIntoDetailsOldAndNewValues()
    {
        using var db = new TestDb();
        var service = Seed(db);

        Assert.Single(service.Query(Filter("ковальчук")));
        Assert.Single(service.Query(Filter("шевченко")));
        Assert.Single(service.Query(Filter("zalik")));
        Assert.Equal(2, service.Query(Filter("рапорт")).Count);
        Assert.Empty(service.Query(Filter("франко")));
    }

    [Fact]
    public void ActionKind_IsDerivedByPrefix()
    {
        using var db = new TestDb();
        var rows = Seed(db).Query(Filter());

        Assert.Equal("delete", rows.Single(r => r.Action == "Видалено документ").ActionKind);
        Assert.Equal("create", rows.Single(r => r.Action == "Створено набір").ActionKind);
        Assert.Equal("generate", rows.Single(r => r.Action == "Перегенеровано").ActionKind);
        Assert.Equal("export", rows.Single(r => r.Action == "Надруковано документ").ActionKind);
    }

    [Fact]
    public void IntakeCreation_ShowsTheNumberTogetherWithDetails()
    {
        using var db = new TestDb();
        var row = Seed(db).Query(Filter()).Single(r => r.Action == "Створено набір");

        Assert.Equal("Набір №4 · 5 папок", row.ChangeDisplay);
    }

    [Fact]
    public void ProfileCreation_IsAudited()
    {
        using var db = new TestDb();
        var audit = new FakeAuditLog();
        var service = new UserProfileService(db.Factory, new FakeCurrentUser(), audit);

        Assert.True(service.TryCreateProfile("Петренко Петро Петрович", "1234", out _));

        Assert.Contains(audit.Entries, e => e.StartsWith("Створено профіль:UserProfile:"));
    }
}
