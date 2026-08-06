using GenDoc.Data;
using GenDoc.Models;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services.OrgTree
{
    public class OrgTreeService : IOrgTreeService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly IAuditLogService _auditLogService;
        private readonly ICurrentUserContext _currentUserContext;

        public OrgTreeService(
            IDbContextFactory<AppDbContext> dbFactory,
            IAuditLogService auditLogService,
            ICurrentUserContext currentUserContext)
        {
            _dbFactory = dbFactory;
            _auditLogService = auditLogService;
            _currentUserContext = currentUserContext;
        }

        public async Task<List<OrgNode>> GetAllAsync()
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.OrgNodes.AsNoTracking()
                .OrderBy(n => n.Depth).ThenBy(n => n.SortOrder).ThenBy(n => n.Name)
                .ToListAsync();
        }

        public async Task<OrgNode> CreateAsync(int parentId, string name, string? documentName)
        {
            using var db = _dbFactory.CreateDbContext();
            var parent = await db.OrgNodes.FirstAsync(n => n.Id == parentId);

            var maxSort = await db.OrgNodes
                .Where(n => n.ParentId == parentId)
                .Select(n => (int?)n.SortOrder)
                .MaxAsync() ?? -1;

            var node = new OrgNode
            {
                Name = name.Trim(),
                DocumentName = string.IsNullOrWhiteSpace(documentName) ? null : documentName.Trim(),
                ParentId = parentId,
                Depth = parent.Depth + 1,
                SortOrder = maxSort + 1,
                IntakeId = parent.IntakeId
            };

            if (db.Database.CurrentTransaction is null)
            {
                using var tx = await db.Database.BeginTransactionAsync();
                await SaveNewNodeAsync(db, node, parent);
                await tx.CommitAsync();
            }
            else
            {
                await SaveNewNodeAsync(db, node, parent);
            }

            return node;
        }

        private async Task SaveNewNodeAsync(AppDbContext db, OrgNode node, OrgNode parent)
        {
            db.OrgNodes.Add(node);
            await db.SaveChangesAsync();
            node.Path = $"{parent.Path}{node.Id}/";
            _auditLogService.Log(db, "Створено папку", "OrgNode", node.Id, null, node.Name);
            await db.SaveChangesAsync();
        }

        public async Task RenameAsync(int id, string name, string? documentName)
        {
            using var db = _dbFactory.CreateDbContext();
            var node = await db.OrgNodes.FirstAsync(n => n.Id == id);
            var oldName = node.Name;

            node.Name = name.Trim();
            node.DocumentName = string.IsNullOrWhiteSpace(documentName) ? null : documentName.Trim();

            if (oldName != node.Name)
                _auditLogService.Log(db, "Перейменовано папку", "OrgNode", node.Id, oldName, node.Name);

            await db.SaveChangesAsync();
        }

        public async Task MoveAsync(int id, int newParentId)
        {
            using var db = _dbFactory.CreateDbContext();
            var node = await db.OrgNodes.FirstAsync(n => n.Id == id);
            var newParent = await db.OrgNodes.FirstAsync(n => n.Id == newParentId);

            if (newParent.Path.StartsWith(node.Path, StringComparison.Ordinal))
                throw new InvalidOperationException("Не можна перемістити папку в її ж гілку.");
            if (node.IntakeId is not null && newParent.IntakeId is not null && node.IntakeId != newParent.IntakeId)
                throw new InvalidOperationException("Папка належить іншому набору.");

            var oldPathNames = await BuildNamePathAsync(db, node);
            var oldPrefix = node.Path;
            var subtree = await db.OrgNodes
                .Where(n => n.Path.StartsWith(oldPrefix) && n.Id != node.Id)
                .ToListAsync();

            var depthDelta = newParent.Depth + 1 - node.Depth;
            var newPrefix = $"{newParent.Path}{node.Id}/";

            async Task ApplyAsync()
            {
                node.ParentId = newParentId;
                node.Path = newPrefix;
                node.Depth = newParent.Depth + 1;
                node.IntakeId = newParent.IntakeId ?? node.IntakeId;

                foreach (var child in subtree)
                {
                    child.Path = string.Concat(newPrefix, child.Path.AsSpan(oldPrefix.Length));
                    child.Depth += depthDelta;
                    if (newParent.IntakeId is not null) child.IntakeId = newParent.IntakeId;
                }

                if (newParent.IntakeId is not null)
                {
                    var nodeIds = subtree.Select(c => c.Id).Append(node.Id).ToList();
                    await db.Recipients
                        .Where(r => r.OrgNodeId != null && nodeIds.Contains(r.OrgNodeId.Value))
                        .ExecuteUpdateAsync(s => s.SetProperty(r => r.IntakeId, newParent.IntakeId));
                }

                await db.SaveChangesAsync();

                var newPathNames = await BuildNamePathAsync(db, node);
                _auditLogService.Log(db, "Переміщено папку", "OrgNode", node.Id, oldPathNames, newPathNames);
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
        }

        public async Task<int> CountPeopleInBranchAsync(int nodeId)
        {
            using var db = _dbFactory.CreateDbContext();
            var path = await db.OrgNodes.Where(n => n.Id == nodeId).Select(n => n.Path).FirstAsync();
            return await db.Recipients
                .Where(r => r.OrgNode != null && r.OrgNode.Path.StartsWith(path))
                .CountAsync();
        }

        public async Task DeleteAsync(int id)
        {
            using var db = _dbFactory.CreateDbContext();
            var node = await db.OrgNodes.FirstAsync(n => n.Id == id);

            var peopleCount = await db.Recipients
                .Where(r => r.OrgNode != null && r.OrgNode.Path.StartsWith(node.Path))
                .CountAsync();
            if (peopleCount > 0)
                throw new InvalidOperationException($"У гілці {peopleCount} осіб. Спершу перемістіть їх.");

            var subtree = await db.OrgNodes.Where(n => n.Path.StartsWith(node.Path)).ToListAsync();
            var now = DateTime.Now;
            var user = _currentUserContext.CurrentUserFullName;

            foreach (var n in subtree)
            {
                n.DeletedAt = now;
                n.DeletedBy = user;
            }

            // Якщо видалена папка — корінь набору (RootOrgNodeId), сам запис Intake
            // теж іде в кошик разом з нею — інакше набір лишиться «висіти» без папки.
            var intake = await db.Intakes.FirstOrDefaultAsync(i => i.RootOrgNodeId == node.Id);
            if (intake is not null)
            {
                intake.DeletedAt = now;
                intake.DeletedBy = user;
            }

            _auditLogService.Log(db, "Видалено папку", "OrgNode", node.Id, node.Name,
                details: intake is not null
                    ? $"разом із набором «{intake.DisplayNumber}»" + (subtree.Count > 1 ? $" та {subtree.Count - 1} вкладеними" : "")
                    : subtree.Count > 1 ? $"разом із {subtree.Count - 1} вкладеними" : null);
            await db.SaveChangesAsync();
        }

        public async Task<List<DeletedFolderInfo>> GetDeletedFoldersAsync()
        {
            using var db = _dbFactory.CreateDbContext();
            var deleted = await db.OrgNodes.IgnoreQueryFilters()
                .Where(n => n.DeletedAt != null)
                .Select(n => new { n.Id, n.Name, n.DeletedAt, n.DeletedBy, n.ParentId })
                .ToListAsync();

            var deletedById = deleted.ToDictionary(n => n.Id);
            var result = new List<DeletedFolderInfo>();

            foreach (var n in deleted)
            {
                // Корінь видаленої гілки: батько живий, відсутній, або видалений окремою операцією.
                var partOfSameCascade = n.ParentId is int pid
                    && deletedById.TryGetValue(pid, out var parent)
                    && parent.DeletedAt == n.DeletedAt;
                if (partOfSameCascade) continue;

                var parentAlive = n.ParentId is int parentId
                    && await db.OrgNodes.AnyAsync(x => x.Id == parentId);

                result.Add(new DeletedFolderInfo(n.Id, n.Name, n.DeletedAt!.Value, n.DeletedBy, parentAlive));
            }

            return result.OrderByDescending(f => f.DeletedAt).ToList();
        }

        public async Task RestoreAsync(int id, int? newParentId)
        {
            using var db = _dbFactory.CreateDbContext();
            var node = await db.OrgNodes.IgnoreQueryFilters().FirstAsync(n => n.Id == id);
            var cascadeStamp = node.DeletedAt;

            var subtree = await db.OrgNodes.IgnoreQueryFilters()
                .Where(n => n.Path.StartsWith(node.Path) && n.DeletedAt == cascadeStamp)
                .ToListAsync();

            async Task ApplyAsync()
            {
                foreach (var n in subtree)
                {
                    n.DeletedAt = null;
                    n.DeletedBy = null;
                }
                await db.SaveChangesAsync();

                if (newParentId is int targetId && targetId != node.ParentId)
                {
                    var newParent = await db.OrgNodes.FirstAsync(n => n.Id == targetId);
                    var oldPrefix = node.Path;
                    var newPrefix = $"{newParent.Path}{node.Id}/";
                    var depthDelta = newParent.Depth + 1 - node.Depth;

                    node.ParentId = targetId;
                    node.Path = newPrefix;
                    node.Depth = newParent.Depth + 1;
                    node.IntakeId = newParent.IntakeId;

                    foreach (var child in subtree.Where(c => c.Id != node.Id))
                    {
                        child.Path = string.Concat(newPrefix, child.Path.AsSpan(oldPrefix.Length));
                        child.Depth += depthDelta;
                        child.IntakeId = newParent.IntakeId ?? child.IntakeId;
                    }
                    await db.SaveChangesAsync();
                }

                _auditLogService.Log(db, "Відновлено папку", "OrgNode", node.Id, null, node.Name);
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
        }

        private static async Task<string> BuildNamePathAsync(AppDbContext db, OrgNode node)
        {
            var ids = node.Path.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(int.Parse).ToList();
            var names = await db.OrgNodes.IgnoreQueryFilters()
                .Where(n => ids.Contains(n.Id))
                .Select(n => new { n.Id, n.Name })
                .ToListAsync();
            var byId = names.ToDictionary(n => n.Id, n => n.Name);
            return string.Join(" › ", ids.Where(byId.ContainsKey).Select(i => byId[i]));
        }
    }
}
