using System.Globalization;

namespace GenDoc.ViewModels.Common;

/// <summary>
/// Дата, яку офіцер вписує руками. Календарів у документних формах немає: швидше набрати
/// «01.08.2026», ніж гортати місяці. Приймаємо й скорочені записи, віддаємо завжди дд.мм.рррр.
/// </summary>
public static class DateText
{
    public const string Mask = "дд.мм.рррр";

    private static readonly string[] Formats =
    {
        "dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy", "d.M.yy",
        "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy",
        "ddMMyyyy"
    };

    public static string Format(DateTime? date) => date?.ToString("dd.MM.yyyy") ?? string.Empty;

    public static string Format(DateOnly? date) => date?.ToString("dd.MM.yyyy") ?? string.Empty;

    public static DateTime? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var trimmed = text.Trim();
        return DateTime.TryParseExact(trimmed, Formats, CultureInfo.InvariantCulture,
                   DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>Текст або порожній, або розбірлива дата — «щось незрозуміле» не пропускаємо далі.</summary>
    public static bool IsValidOrEmpty(string? text) =>
        string.IsNullOrWhiteSpace(text) || Parse(text) is not null;
}
