using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.OrgTree;
using GenDoc.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Tests.Services;

/// <summary>
/// DeleteAsync свідомо кладе в кошик і папку, і сам запис Intake - з коментарем
/// «інакше набір лишиться висіти без папки». А RestoreAsync знімав DeletedAt
/// ЛИШЕ з OrgNodes, тож після відновлення папки поверталися, а набір - ні.
///
/// Наслідок був незворотний з UI: IntakeService.GetByIdAsync (глобальний фільтр
/// м'якого видалення) віддавав null, гілка переставала бути набором - зникала з
/// екрана «Набори», ігнорувала фільтри Активні/Завершені, ставала
/// перейменовуваною, а люди з цим IntakeId не потрапляли ні в набір, ні в
/// постійний склад (аудит 2026-08-28).
/// </summary>
public class RestoreIntakeFolderTests
{
    private static OrgTreeService Service(TestDb db) =>
        new(db.Factory, new FakeAuditLog(), new FakeCurrentUser());

    private static (int RootNodeId, int IntakeId) SeedIntakeWithRootFolder(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        var root = new OrgNode { Name = "Корінь", Path = "/", Depth = 0 };
        ctx.OrgNodes.Add(root);
        ctx.SaveChanges();

        var intakeRoot = new OrgNode { Name = "Набір №7", ParentId = root.Id, Depth = 1 };
        ctx.OrgNodes.Add(intakeRoot);
        ctx.SaveChanges();
        intakeRoot.Path = $"/{intakeRoot.Id}/";
        ctx.SaveChanges();

        var intake = new Intake
        {
            Number = 7, DisplayNumber = "Набір №7", RootOrgNodeId = intakeRoot.Id,
            Status = IntakeStatus.Active,
            DateStart = new DateOnly(2026, 8, 1), DateEnd = new DateOnly(2026, 10, 1)
        };
        ctx.Intakes.Add(intake);
        ctx.SaveChanges();

        return (intakeRoot.Id, intake.Id);
    }

    [Fact]
    public async Task Delete_ThenRestore_BringsTheIntakeBackToo()
    {
        using var db = new TestDb();
        var (rootNodeId, intakeId) = SeedIntakeWithRootFolder(db);
        var service = Service(db);

        await service.DeleteAsync(rootNodeId);

        using (var ctx = db.Factory.CreateDbContext())
            Assert.Null(await ctx.Intakes.FirstOrDefaultAsync(i => i.Id == intakeId));

        await service.RestoreAsync(rootNodeId, newParentId: null);

        using (var ctx = db.Factory.CreateDbContext())
        {
            Assert.NotNull(await ctx.OrgNodes.FirstOrDefaultAsync(n => n.Id == rootNodeId));
            Assert.NotNull(await ctx.Intakes.FirstOrDefaultAsync(i => i.Id == intakeId));
        }
    }

    // Набір, видалений ОКРЕМОЮ операцією (інша мітка часу), відновлення папки
    // чіпати не повинно - інакше кошик почав би воскрешати чуже.
    [Fact]
    public async Task Restore_DoesNotTouchIntakeDeletedInAnotherOperation()
    {
        using var db = new TestDb();
        var (rootNodeId, intakeId) = SeedIntakeWithRootFolder(db);
        var service = Service(db);

        using (var ctx = db.Factory.CreateDbContext())
        {
            var intake = await ctx.Intakes.FirstAsync(i => i.Id == intakeId);
            intake.DeletedAt = new DateTime(2020, 1, 1);
            intake.DeletedBy = "Хтось інший";
            await ctx.SaveChangesAsync();
        }

        await service.DeleteAsync(rootNodeId);
        await service.RestoreAsync(rootNodeId, newParentId: null);

        using (var ctx = db.Factory.CreateDbContext())
        {
            Assert.NotNull(await ctx.OrgNodes.FirstOrDefaultAsync(n => n.Id == rootNodeId));
            Assert.Null(await ctx.Intakes.FirstOrDefaultAsync(i => i.Id == intakeId));
        }
    }
}
