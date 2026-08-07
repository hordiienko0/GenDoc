using GenDoc.Data;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services
{
    // Підпис «особи, яка проводила інструктаж» для xlsx-відомостей ({{курсовий_офіцер}}).
    // Був продубльований у GenerationService і ExportService — тепер одна точка,
    // щоб обидва шляхи генерації не розходились у поведінці.
    public static class CourseOfficerSignature
    {
        // Пріоритет — постійний склад (IntakeId == null): курсовий офіцер за
        // визначенням не належить набору. Але якщо позначену людину завели
        // всередині набору, підпис усе одно мусить заповнитись — інакше тег
        // мовчки лишається порожнім, і незрозуміло, чому. Тому запасний прохід
        // без фільтра за набором.
        public static string? Build(AppDbContext db)
        {
            var courseOfficer =
                FindFirst(db, permanentStaffOnly: true) ?? FindFirst(db, permanentStaffOnly: false);

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

        private static Models.Recipient? FindFirst(AppDbContext db, bool permanentStaffOnly)
        {
            var query = db.Recipients.Include(r => r.Unit).Where(r => r.IsCourseOfficer);
            if (permanentStaffOnly) query = query.Where(r => r.IntakeId == null);
            return query.OrderBy(r => r.Id).FirstOrDefault();
        }
    }
}
