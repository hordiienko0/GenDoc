using GenDoc.Data;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services.OrgTree
{
    public static class OrgPathBuilder
    {
        public static async Task<string?> BuildAsync(AppDbContext db, int? orgNodeId)
        {
            if (orgNodeId is not int id) return null;
            var nodes = await db.OrgNodes.IgnoreQueryFilters()
                .Select(o => new { o.Id, o.Name, o.ParentId }).ToListAsync();
            var byId = nodes.ToDictionary(n => n.Id, n => (n.Name, n.ParentId));

            var names = new List<string>();
            int? current = id;
            while (current is int cid && byId.TryGetValue(cid, out var node))
            {
                names.Insert(0, node.Name);
                current = node.ParentId;
            }
            return names.Count == 0 ? null : string.Join(" / ", names);
        }
    }
}
