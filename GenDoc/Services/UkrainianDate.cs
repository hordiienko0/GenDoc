namespace GenDoc.Services
{
    public static class UkrainianDate
    {
        private static readonly string[] GenitiveMonths =
        {
            "січня", "лютого", "березня", "квітня", "травня", "червня",
            "липня", "серпня", "вересня", "жовтня", "листопада", "грудня"
        };

        public static string Long(DateOnly date)
            => $"{date.Day} {GenitiveMonths[date.Month - 1]} {date.Year} року";

        /// <summary>
        /// Перелік днів, як у наказах на стрільби: «04, 05, 06, 10 серпня 2026 року»; на межі місяця -
        /// «30, 31 липня, 01, 02 серпня 2026 року», на межі року рік ставиться після кожного року.
        /// </summary>
        public static string DayList(IEnumerable<DateOnly> dates)
        {
            var ordered = dates.Distinct().OrderBy(d => d).ToList();
            if (ordered.Count == 0) return string.Empty;

            var years = ordered.GroupBy(d => d.Year).Select(year =>
            {
                var months = year.GroupBy(d => d.Month).Select(month =>
                    $"{string.Join(", ", month.Select(d => d.Day.ToString("00")))} {GenitiveMonths[month.Key - 1]}");
                return $"{string.Join(", ", months)} {year.Key} року";
            });

            return string.Join(", ", years);
        }
    }
}
