using GenDoc.ViewModels.Generation;

namespace GenDoc.Services.Generation
{
    // Будує форму ручних міток для групового запуску й ad-hoc-діалогу: класифікує
    // теги (дата/підписант/звичайний текст), префілить дати з активного набору,
    // підбирає підписанта (збіг з поточним користувачем → останній використаний).
    public interface IManualTagFormBuilder
    {
        // needsCourseOfficer додає окремий дропліст курсового офіцера. Він не
        // виводиться з tags: {{курсовий_офіцер}} - не ручна мітка, а поле
        // відомості (PlaceholderTagMaps → CourseOfficerSignature), тож до цього
        // переліку не потрапляє взагалі. Питати про потребу мусить викликач.
        Task<ManualTagFormViewModel> BuildAsync(
            IReadOnlyList<string> tags, string contextKey, bool needsCourseOfficer = false);

        // Викликати після успішної генерації - запам'ятовує звичайні значення й підписанта.
        Task SaveAsync(string contextKey, ManualTagFormViewModel form);
    }
}
