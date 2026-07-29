using GenDoc.Data;
using GenDoc.Models;
using GenDoc.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services.Intakes
{
    public class IntakeService : IIntakeService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly IAuditLogService _auditLogService;

        public IntakeService(IDbContextFactory<AppDbContext> dbFactory, IAuditLogService auditLogService)
        {
            _dbFactory = dbFactory;
            _auditLogService = auditLogService;
        }

        public async Task<Intake?> GetActiveAsync()
        {
            using var db = _dbFactory.CreateDbContext();

            // Автоперехід статусів за датами: Planned → Active → Completed.
            var today = DateOnly.FromDateTime(DateTime.Today);
            await db.Intakes
                .Where(i => i.Status == IntakeStatus.Planned && i.DateStart <= today && i.DateEnd >= today)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, IntakeStatus.Active));
            await db.Intakes
                .Where(i => i.Status == IntakeStatus.Active && i.DateEnd < today)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, IntakeStatus.Completed));

            return await db.Intakes.AsNoTracking()
                .Where(i => i.Status == IntakeStatus.Active)
                .OrderByDescending(i => i.Number)
                .FirstOrDefaultAsync();
        }

        public async Task<Intake?> GetByIdAsync(int id)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.Intakes.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
        }

        public async Task<int> GetNextNumberAsync()
        {
            using var db = _dbFactory.CreateDbContext();
            var max = await db.Intakes.IgnoreQueryFilters().MaxAsync(i => (int?)i.Number) ?? 0;
            return max + 1;
        }

        public async Task<string> GetNumberTemplateAsync()
        {
            using var db = _dbFactory.CreateDbContext();
            var template = await db.AppSettings.Select(s => s.IntakeNumberTemplate).FirstOrDefaultAsync();
            return string.IsNullOrWhiteSpace(template) ? "Набір №{n}" : template;
        }

        public async Task<Intake> CreateAsync(IntakeCreateRequest request)
        {
            using var db = _dbFactory.CreateDbContext();
            var baseNode = await db.OrgNodes.FirstAsync(n => n.Id == request.BaseNodeId);

            var today = DateOnly.FromDateTime(DateTime.Today);
            var intake = new Intake
            {
                Number = await GetNextNumberInternalAsync(db),
                DisplayNumber = request.DisplayNumber.Trim(),
                DateStart = request.DateStart,
                DateEnd = request.DateEnd,
                Status = request.DateStart <= today && today <= request.DateEnd
                    ? IntakeStatus.Active
                    : IntakeStatus.Planned
            };

            async Task ApplyAsync()
            {
                db.Intakes.Add(intake);
                await db.SaveChangesAsync();

                var rootNode = new OrgNode
                {
                    Name = intake.DisplayNumber,
                    ParentId = baseNode.Id,
                    Depth = baseNode.Depth + 1,
                    SortOrder = await db.OrgNodes.Where(n => n.ParentId == baseNode.Id)
                        .Select(n => (int?)n.SortOrder).MaxAsync() ?? -1,
                    IntakeId = intake.Id
                };
                rootNode.SortOrder += 1;
                db.OrgNodes.Add(rootNode);
                await db.SaveChangesAsync();
                rootNode.Path = $"{baseNode.Path}{rootNode.Id}/";

                var sort = 0;
                foreach (var name in request.Subfolders)
                {
                    var sub = new OrgNode
                    {
                        Name = name,
                        ParentId = rootNode.Id,
                        Depth = rootNode.Depth + 1,
                        SortOrder = sort++,
                        IntakeId = intake.Id
                    };
                    db.OrgNodes.Add(sub);
                    await db.SaveChangesAsync();
                    sub.Path = $"{rootNode.Path}{sub.Id}/";
                }

                intake.RootOrgNodeId = rootNode.Id;
                _auditLogService.Log(db, "Створено набір", "Intake", intake.Id, null, intake.DisplayNumber,
                    $"{request.Subfolders.Count + 1} папок, {request.DateStart:dd.MM.yyyy} — {request.DateEnd:dd.MM.yyyy}");
                await db.SaveChangesAsync();
            }

            if (db.Database.CurrentTransaction is null)
            {
                using var tx = await db.Database.BeginTransactionAsync();
                await ApplyAsync();
                await tx.CommitAsync();
            }
            else
            {
                await ApplyAsync();
            }

            return intake;
        }

        private static async Task<int> GetNextNumberInternalAsync(AppDbContext db)
            => (await db.Intakes.IgnoreQueryFilters().MaxAsync(i => (int?)i.Number) ?? 0) + 1;
    }
}
