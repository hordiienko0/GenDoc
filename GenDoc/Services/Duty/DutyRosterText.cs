using System.Globalization;

namespace GenDoc.Services.Duty;

/// <summary>Тексти листа графіка: рядок періоду в шапці і ПІБ у форматі «ПРІЗВИЩЕ І.Б.».</summary>
public static class DutyRosterText
{
    public const string DefaultHeading =
        "Графік чергувань особового складу\nкурсів перепідготовки та підвищення кваліфікації ХНУПС";

    private static readonly string[] Months =
    {
        "січень", "лютий", "березень", "квітень", "травень", "червень",
        "липень", "серпень", "вересень", "жовтень", "листопад", "грудень"
    };

    private static readonly CultureInfo Ukrainian = CultureInfo.GetCultureInfo("uk-UA");

    /// <summary>«на жовтень 2026 р.», «на вересень-жовтень 2026 р.», «на грудень 2026 - січень 2027 р.».</summary>
    public static string PeriodLine(DateOnly from, DateOnly to)
    {
        var first = Months[from.Month - 1];
        var last = Months[to.Month - 1];

        if (from.Year != to.Year)
            return $"на {first} {from.Year} - {last} {to.Year} р.";

        return from.Month == to.Month
            ? $"на {first} {from.Year} р."
            : $"на {first}-{last} {from.Year} р.";
    }

    public static IReadOnlyList<string> TitleLines(string heading, DateOnly from, DateOnly to)
        => heading.Replace("\r", string.Empty)
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Append(PeriodLine(from, to))
            .ToList();

    public static string DisplayName(string lastName, string? firstName, string? middleName)
    {
        var initials = string.Concat(new[] { firstName, middleName }
            .Where(p => !string.IsNullOrEmpty(p) && char.IsLetter(p[0]))
            .Select(p => char.ToUpper(p![0], Ukrainian) + "."));

        var surname = lastName.Trim().ToUpper(Ukrainian);
        return initials.Length == 0 ? surname : $"{surname} {initials}";
    }

    public static string PeriodLabel(DateOnly from, DateOnly to)
        => from.Year == to.Year
            ? $"{from:dd.MM} - {to:dd.MM.yyyy}"
            : $"{from:dd.MM.yyyy} - {to:dd.MM.yyyy}";
}
