using System.Collections;
using System.Windows.Data;
using GenDoc.Models.Enums;

namespace GenDoc.ViewModels.Templates
{
    /// <summary>
    /// Дві групи переліку шаблонів Word - для наборів і для постійного складу.
    ///
    /// Це ВИДИ над однією й тією самою колекцією, а не дві окремі копії:
    /// елементи лишаються тими самими об'єктами, тож вибір рядка, завантажений
    /// мапінг і перемикач аудиторії працюють однаково в обох групах. Дві копії
    /// довелося б синхронізувати, і рядок губив би стан при переході.
    ///
    /// Винесено окремо від в'ю-моделі саме заради тестів: сама в'ю-модель тягне
    /// за собою півдесятка сервісів, а правило поділу перевіряється й без них.
    /// </summary>
    public static class TemplateAudienceGroups
    {
        public static ListCollectionView Intake(IList source) => new(source)
        {
            Filter = o => AudienceOf(o) != TemplateAudience.PermanentStaff
        };

        public static ListCollectionView PermanentStaff(IList source) => new(source)
        {
            Filter = o => AudienceOf(o) == TemplateAudience.PermanentStaff
        };

        /// <summary>Невідомий аудиторії елемент лишається в наборах: замовчування
        /// схеми - Intake, і губити рядок через нерозпізнаний тип не можна.</summary>
        private static TemplateAudience AudienceOf(object? item) =>
            item is DocxTemplateListItemViewModel t ? t.Audience : TemplateAudience.Intake;
    }
}
