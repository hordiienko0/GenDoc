using GenDoc.Data;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services.OrgTree
{
    public class CountService : ICountService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public CountService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<Dictionary<int, int>> GetTreeCountsAsync()
        {
            using var db = _dbFactory.CreateDbContext();
            var groups = await db.Recipients
                .Where(r => r.OrgNodeId != null)
                .GroupBy(r => r.OrgNodeId!.Value)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToListAsync();

            return groups.ToDictionary(g => g.Key, g => g.Count);
        }
    }
}
