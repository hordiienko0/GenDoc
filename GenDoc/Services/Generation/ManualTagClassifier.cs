using GenDoc.ViewModels.Generation;

namespace GenDoc.Services.Generation
{
    // Чиста класифікація тегів для ManualTagFormBuilder — винесена окремо, щоб
    // тестуватися без БД (DbContextFactory/IStaffService потрібні лише для
    // фактичного наповнення значень, не для визначення типу тега).
    public static class ManualTagClassifier
    {
        private const string ArrivalDateTag = "дата_прибуття";
        private const string EnrollmentDateTag = "дата_зарахування";
        private const string ReportDateTag = "дата_рапорту";

        public static ManualTagKind Classify(string tag) => tag switch
        {
            ArrivalDateTag or EnrollmentDateTag or ReportDateTag => ManualTagKind.Date,
            _ => ManualTagKind.Text
        };

        public static bool HasSignerPair(IReadOnlyList<string> tags) =>
            tags.Contains(ManualTagFormViewModel.SignerRankTag) && tags.Contains(ManualTagFormViewModel.SignerNameTag);

        // Префіл: дата прибуття з активного набору, зарахування — наступного дня,
        // дата рапорту — сьогодні. Кидає для нерозпізнаного тега — викликач фільтрує Kind == Date заздалегідь.
        public static DateOnly PrefillDate(string tag, DateOnly intakeArrivalDate) => tag switch
        {
            ArrivalDateTag => intakeArrivalDate,
            EnrollmentDateTag => intakeArrivalDate.AddDays(1),
            ReportDateTag => DateOnly.FromDateTime(DateTime.Today),
            _ => throw new ArgumentException($"Тег «{tag}» не є датою.", nameof(tag))
        };

        // Дата рапорту — коротка форма dd.MM.yyyy (це підпис); прибуття/зарахування — довга українська форма (текст рапорту).
        public static string FormatDate(string tag, DateOnly date) =>
            tag == ReportDateTag ? date.ToString("dd.MM.yyyy") : UkrainianDate.Long(date);
    }
}
