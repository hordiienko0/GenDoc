using GenDoc.Models;

namespace GenDoc.Services
{
    public interface IUserSettingsService
    {
        Task<UserSettings> GetForCurrentUserAsync();

        Task UpdateAsync(Action<UserSettings> mutate);
    }
}
