using GenDoc.Models;

namespace GenDoc.Services
{
    public interface IUserSettingsService
    {
        // Get-or-create для поточного користувача. Без користувача (CurrentUserId is null)
        // повертає НЕзбережений UserSettings з дефолтами - читачі працюють, запис - no-op.
        Task<UserSettings> GetForCurrentUserAsync();

        // Завантажує (або створює) рядок поточного користувача, застосовує mutate, зберігає.
        // Без користувача - no-op.
        Task UpdateAsync(Action<UserSettings> mutate);
    }
}
