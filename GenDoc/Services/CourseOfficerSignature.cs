using GenDoc.Data;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services
{
    public static class CourseOfficerSignature
    {
        public static string? Build(AppDbContext db)
        {
            var courseOfficer = FindCourseOfficer(db);

            return courseOfficer is null ? null : Compose(courseOfficer);
        }

        public static string? BuildFor(AppDbContext db, int recipientId)
        {
            var courseOfficer = db.Recipients
                .Include(r => r.Unit)
                .FirstOrDefault(r => r.Id == recipientId && r.IsCourseOfficer && r.IntakeId == null);

            return courseOfficer is null ? null : Compose(courseOfficer);
        }

        private static string Compose(Models.Recipient courseOfficer)
        {
            var parts = new[]
            {
                "Курсовий офіцер",
                courseOfficer.Unit?.Name,
                courseOfficer.Rank,
                NameFormatter.ShortName(courseOfficer.LastName, courseOfficer.FirstName, courseOfficer.MiddleName)
            };

            return string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

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
