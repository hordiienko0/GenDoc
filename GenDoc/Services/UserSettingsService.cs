using GenDoc.Data;
using GenDoc.Models;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services
{
    public class UserSettingsService : IUserSettingsService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly ICurrentUserContext _currentUser;

        public UserSettingsService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUserContext currentUser)
        {
            _dbFactory = dbFactory;
            _currentUser = currentUser;
        }

        public async Task<UserSettings> GetForCurrentUserAsync()
        {
            if (_currentUser.CurrentUserId is not int userId)
                return new UserSettings();

            using var db = _dbFactory.CreateDbContext();
            var existing = await db.UserSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserProfileId == userId);
            if (existing is not null) return existing;

            var created = new UserSettings { UserProfileId = userId };
            db.UserSettings.Add(created);
            await db.SaveChangesAsync();
            return created;
        }

        public async Task UpdateAsync(Action<UserSettings> mutate)
        {
            if (_currentUser.CurrentUserId is not int userId) return;

            using var db = _dbFactory.CreateDbContext();
            var row = await db.UserSettings.FirstOrDefaultAsync(s => s.UserProfileId == userId);
            if (row is null)
            {
                row = new UserSettings { UserProfileId = userId };
                db.UserSettings.Add(row);
            }
            mutate(row);
            await db.SaveChangesAsync();
        }
    }
}
