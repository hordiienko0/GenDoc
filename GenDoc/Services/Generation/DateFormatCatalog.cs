using GenDoc.Services;

namespace GenDoc.Services.Generation
{
    public record DateFormatOption(string Key, string Display);

    // Три формати дати, які можна обрати для дато-полів у мапінгу плейсхолдерів
    // (Views/Templates). Ключ зберігається як TemplateFieldMapping.DateFormat.
    public static class DateFormatCatalog
    {
        public const string DdMmYyyy = "dd.MM.yyyy";
        public const string Long = "long";
        public const string YyyyMmDd = "yyyy-MM-dd";

        public static readonly IReadOnlyList<DateFormatOption> Options = new List<DateFormatOption>
        {
            new(DdMmYyyy, "дд.мм.рррр"),
            new(Long, "дд місяця рррр"),
            new(YyyyMmDd, "рррр-мм-дд"),
        };

        public static string Format(DateOnly date, string? formatKey, string fallbackKey)
        {
            var key = string.IsNullOrEmpty(formatKey) ? fallbackKey : formatKey;
            return key switch
            {
                Long => UkrainianDate.Long(date),
                YyyyMmDd => date.ToString(YyyyMmDd),
                _ => date.ToString(DdMmYyyy)
            };
        }
    }
}
