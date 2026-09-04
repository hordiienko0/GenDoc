using GenDoc.ViewModels.Generation;

namespace GenDoc.Services.Generation
{
    public static class ManualTagClassifier
    {
        private const string ArrivalDateTag = "дата_прибуття";
        private const string EnrollmentDateTag = "дата_зарахування";
        private const string ReportDateTag = "дата_рапорту";
        private const string PlainDateTag = "дата";
        private const string SheetDateTag = "дата_аркуша";
        private const string DayMonthYear = "dd.MM.yyyy";

        public static string Normalize(string tag) => tag.Trim().Trim('{', '}').Trim();

        private static readonly string PeriodTag = Normalize(XlsxGenerationService.PeriodTag);

        public static ManualTagKind Classify(string tag) => Normalize(tag) switch
        {
            ArrivalDateTag or EnrollmentDateTag or ReportDateTag or PlainDateTag or SheetDateTag => ManualTagKind.Date,
            var t when t == PeriodTag => ManualTagKind.Period,
            _ => ManualTagKind.Text
        };

        public static bool IsDocumentDate(string tag) =>
            Normalize(tag) is PlainDateTag or ReportDateTag or SheetDateTag;

        public static bool IsSignerRank(string tag) => Normalize(tag) == ManualTagFormViewModel.SignerRankTag;

        public static bool IsSignerName(string tag) => Normalize(tag) == ManualTagFormViewModel.SignerNameTag;

        public static bool HasSignerPair(IReadOnlyList<string> tags) =>
            tags.Any(IsSignerRank) && tags.Any(IsSignerName);

        public static DateOnly PrefillDate(string tag, DateOnly intakeArrivalDate) => Normalize(tag) switch
        {
            ArrivalDateTag => intakeArrivalDate,
            EnrollmentDateTag => intakeArrivalDate.AddDays(1),
            ReportDateTag or PlainDateTag or SheetDateTag => DateOnly.FromDateTime(DateTime.Today),
            _ => throw new ArgumentException($"Тег «{tag}» не є датою.", nameof(tag))
        };

        public static string FormatDate(string tag, DateOnly date) =>
            IsDocumentDate(tag) ? date.ToString(DayMonthYear) : UkrainianDate.Long(date);
    }
}
