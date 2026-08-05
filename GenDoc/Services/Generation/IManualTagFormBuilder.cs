using GenDoc.ViewModels.Generation;

namespace GenDoc.Services.Generation
{
    // Будує форму ручних міток для групового запуску й ad-hoc-діалогу: класифікує
    // теги (дата/підписант/звичайний текст), префілить дати з активного набору,
    // підбирає підписанта (збіг з поточним користувачем → останній використаний).
    public interface IManualTagFormBuilder
    {
        Task<ManualTagFormViewModel> BuildAsync(IReadOnlyList<string> tags, string contextKey);

        // Викликати після успішної генерації — запам'ятовує звичайні значення й підписанта.
        Task SaveAsync(string contextKey, ManualTagFormViewModel form);
    }
}
