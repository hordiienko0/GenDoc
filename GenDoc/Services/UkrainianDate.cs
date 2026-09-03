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
    }
}
