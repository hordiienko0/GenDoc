namespace GenDoc.Services
{
    // «15 липня 2026 року» - форма для тексту рапортів. Місяці в родовому відмінку
    // захардкоджені (не через CultureInfo), щоб не залежати від локалі машини оператора.
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
