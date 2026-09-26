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
        private const string ShootingDatesTag = "дати_стрільб";
        private const string DayMonthYear = "dd.MM.yyyy";

        public static string Normalize(string tag) => tag.Trim().Trim('{', '}').Trim();

        private static readonly string PeriodTag = Normalize(XlsxGenerationService.PeriodTag);

        public static ManualTagKind Classify(string tag) => Normalize(tag) switch
        {
            ArrivalDateTag or EnrollmentDateTag or ReportDateTag or PlainDateTag or SheetDateTag => ManualTagKind.Date,
            var t when t == PeriodTag => ManualTagKind.Period,
            ShootingDatesTag => ManualTagKind.Period,
            _ => ManualTagKind.Text
        };

        /// <summary>{{дати_стрільб}} - той самий перелік днів, що й {{період}}, але словами для тексту наказу.</summary>
        public static bool IsShootingDates(string tag) => Normalize(tag) == ShootingDatesTag;

        /// <summary>
        /// Значення для тега періоду з одного введеного переліку днів: {{період}} отримує дати як є
        /// («04.08.2026-06.08.2026, 10.08.2026» - з нього відомість розмножує аркуші), {{дати_стрільб}} -
        /// словами («04, 05, 06, 10 серпня 2026 року»). Якщо перелік не розпізнано, текст іде як є.
        /// </summary>
        public static string FormatPeriodValue(string tag, string raw)
        {
            if (!IsShootingDates(tag) || string.IsNullOrWhiteSpace(raw)) return raw;

            try
            {
                return UkrainianDate.DayList(XlsxGenerationService.ParsePeriodDates(raw));
            }
            catch (FormatException)
            {
                return raw;
            }
        }

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
