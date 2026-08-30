using GenDoc.Models;
using GenDoc.Services.Audit;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Services;

/// <summary>
/// Журнал сортувався лише за OccurredAt, без вторинного ключа. AuditLogService
/// пише DateTime.Now, роздільність системного годинника Windows ~15 мс, тож усі
/// записи одного SaveChanges зазвичай мають ОДНАКОВУ мітку часу. SQLite не
/// гарантує сталого порядку між запитами для однакових ключів, тож між першою
/// сторінкою і «Завантажити ще» частина рядків дублювалась, а частина зникала
/// (аудит 2026-08-28).
///
/// Заразом перші тести на цей сервіс узагалі: він був без покриття.
/// </summary>
public class AuditLogPagingTests
{
    private const int Total = 25;

    private static AuditLogQueryService Seed(TestDb db)
    {
        using (var ctx = db.Factory.CreateDbContext())
        {
            // Усі записи з ОДНІЄЮ міткою часу - саме те, що дає один SaveChanges.
            var stamp = new DateTime(2026, 8, 20, 10, 38, 0);
            for (var i = 1; i <= Total; i++)
            {
                ctx.AuditLog.Add(new AuditLogEntry
                {
                    OccurredAt = stamp,
                    Action = "Створено",
                    EntityName = "Recipient",
                    EntityId = i,
                    UserProfileName = "Курсовий"
                });
            }
            ctx.SaveChanges();
        }

        return new AuditLogQueryService(db.Factory);
    }

    [Fact]
    public void Paging_OverEqualTimestamps_LosesNoRowsAndRepeatsNone()
    {
        using var db = new TestDb();
        var service = Seed(db);

        var seen = new List<string>();
        for (var skip = 0; skip < Total; skip += 10)
            seen.AddRange(service.Query(new AuditLogFilter(null, null, null, null, skip, 10)).Select(r => r.ObjectDisplay));

        Assert.Equal(Total, seen.Count);
        Assert.Equal(Total, seen.Distinct().Count());
    }

    [Fact]
    public void Paging_IsStableBetweenIdenticalRequests()
    {
        using var db = new TestDb();
        var service = Seed(db);

        var first = service.Query(new AuditLogFilter(null, null, null, null, 0, 10)).Select(r => r.ObjectDisplay).ToList();
        var again = service.Query(new AuditLogFilter(null, null, null, null, 0, 10)).Select(r => r.ObjectDisplay).ToList();

        Assert.Equal(first, again);
    }

    // Найновіші - зверху; за однакової мітки часу новішим вважається запис із
    // більшим Id, бо він доданий пізніше в межах того самого SaveChanges.
    [Fact]
    public void Newest_ComesFirst_EvenWhenTimestampsMatch()
    {
        using var db = new TestDb();
        var service = Seed(db);

        var page = service.Query(new AuditLogFilter(null, null, null, null, 0, 3)).Select(r => r.ObjectDisplay).ToList();

        Assert.Equal(
            new[] { $"Запис особового складу, №{Total}", $"Запис особового складу, №{Total - 1}", $"Запис особового складу, №{Total - 2}" },
            page);
    }
}
