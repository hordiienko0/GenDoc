using GenDoc.Data;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services
{
    // Підпис «особи, яка проводила інструктаж» для xlsx-відомостей ({{курсовий_офіцер}}).
    // Був продубльований у GenerationService і ExportService — тепер одна точка,
    // щоб обидва шляхи генерації не розходились у поведінці.
    public static class CourseOfficerSignature
    {
        // ЛИШЕ постійний склад (IntakeId == null). Раніше тут був запасний прохід
        // без фільтра за набором — щоб тег не лишався мовчки порожнім. Він
        // прибраний: у наборі люди ПРОХОДЯТЬ навчання, курсовим офіцером ніхто з
        // них бути не може, тож той прохід прикривав випадок, якого за моделлю не
        // існує, і натомість дозволяв підписати документ людині з набору.
        //
        // null тепер означає рівно одне: підписанта немає. Викликач мусить
        // попередити й НЕ генерувати — документ із порожнім місцем підпису гірший
        // за явну зупинку, бо його ніхто не помітить.
        public static string? Build(AppDbContext db)
        {
            var courseOfficer = FindCourseOfficer(db);

            if (courseOfficer is null) return null;

            var unitName = courseOfficer.Unit?.Name;
            var parts = new[]
            {
                "Курсовий офіцер",
                unitName,
                courseOfficer.Rank,
                NameFormatter.ShortName(courseOfficer.LastName, courseOfficer.FirstName, courseOfficer.MiddleName)
            };

            return string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        /// <summary>Курсовий офіцер серед постійного складу. Прапорця
        /// «шукати будь-де» тут навмисно немає: людина з набору підписантом бути
        /// не може, і можливість це обійти не мусить існувати в коді.</summary>
        internal static Models.Recipient? FindCourseOfficer(AppDbContext db)
        {
            return db.Recipients
                .Include(r => r.Unit)
                .Where(r => r.IsCourseOfficer && r.IntakeId == null)
                .OrderBy(r => r.Id)
                .FirstOrDefault();
        }
    }
}
