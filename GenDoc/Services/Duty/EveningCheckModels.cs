using System.Globalization;
using GenDoc.Models;

namespace GenDoc.Services.Duty;

/// <summary>
/// Тексти листа вечірньої перевірки - як у паперовому «СПИСКУ вечірньої перевірки»:
/// заголовки блоків, шапка таблиці, рядки обліку й підписи.
/// </summary>
public static class EveningCheckText
{
    /// <summary>Перший блок - особовий склад набору.</summary>
    public const string CourseTitle = "ОСОБОВИЙ СКЛАД КУРСІВ ПЕРЕПІДГОТОВКИ ТА ПІДВИЩЕННЯ КВАЛІФІКАЦІЇ ХНУПС";

    /// <summary>Другий блок - прикомандировані (своя папка в дереві).</summary>
    public const string SecondedTitle = "ПРИКОМАНДИРОВАНІ";

    /// <summary>Папка прикомандированих у дереві впізнається за назвою: «Прикомандировані», «Командировані».</summary>
    public static bool IsSecondedFolderName(string name) => name.Contains("командиров", StringComparison.OrdinalIgnoreCase);

    /// <summary>Підсумковий блок у кінці кожної сторінки днів (у паперовому списку з друкарськими помилками - тут виправлено).</summary>
    public const string SummaryTitle = "ОБЛІКОВІ ДАНІ ОСОБОВОГО СКЛАДУ КУРСІВ ПЕРЕПІДГОТОВКИ ТА ПІДВИЩЕННЯ КВАЛІФІКАЦІЇ ХНУПС";

    public const string NumberHeader = "№";
    public const string NumberHeaderSecondLine = "п/п";
    public const string RankHeader = "в/зв";
    public const string NameHeader = "Прізвище та ім’я";
    public const string DateHeader = "ДАТА";
    public const string CheckerSignature = "Підпис особи, яка проводила перевірку";

    public const string LimitedPart = "ОБМЕЖЕНО ПРИДАТНІ";
    public const string FitPart = "ПРИДАТНІ";

    /// <summary>Заголовок окремої таблиці за придатністю: «ПРИКОМАНДИРОВАНІ (ОБМЕЖЕНО ПРИДАТНІ)».</summary>
    public static string FitnessTitle(string title, bool limited)
    {
        var part = limited ? LimitedPart : FitPart;
        return title.Length == 0 ? part : $"{title} ({part})";
    }

    /// <summary>Посада підписанта - у списку завжди так, а не повна посада з налаштувань.</summary>
    public const string SignaturePosition = "Начальник курсів П та ПК:";

    /// <summary>Рядки підсумкового блоку - заповнюються від руки щодня.</summary>
    public static readonly IReadOnlyList<string> SummaryRows = new[]
    {
        "За списком", "В строю", "Відсутні", "Наряд", "Лазарет", "Шпиталь",
        "Поза курсом", "Відрядження", "Відпустка", "Звільнення"
    };

    private static readonly CultureInfo Ukrainian = CultureInfo.GetCultureInfo("uk-UA");

    /// <summary>«ПРІЗВИЩЕ Ім'я По батькові» - як у колонці «Прізвище та ім’я».</summary>
    public static string FullName(string lastName, string? firstName, string? middleName)
        => string.Join(' ', new[] { lastName.Trim().ToUpper(Ukrainian), firstName?.Trim(), middleName?.Trim() }
            .Where(p => !string.IsNullOrEmpty(p)));

    /// <summary>
    /// Рядок підпису: «Начальник курсів П та ПК:  підполковник                     Д. ВЕРЕМЕЄНКО».
    /// Звання і ПІБ - з налаштувань частини; без них лишається сама посада.
    /// </summary>
    public static string Signature(OrganizationSettings? org)
    {
        var rank = org?.CommanderRank?.Trim() ?? string.Empty;
        var name = ShortName(org?.CommanderFullName);
        if (rank.Length == 0 && name.Length == 0) return SignaturePosition;

        var tail = string.Join(new string(' ', 21), new[] { rank, name }.Where(p => p.Length > 0));
        return $"{SignaturePosition}  {tail}";
    }

    /// <summary>
    /// «Д. ВЕРЕМЕЄНКО» з ПІБ у налаштуваннях: слово великими літерами - прізвище («Дмитро ВЕРЕМЕЄНКО»),
    /// інакше перше слово - прізвище, друге - ім'я («Веремеєнко Дмитро Іванович»).
    /// </summary>
    public static string ShortName(string? fullName)
    {
        var words = (fullName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return string.Empty;

        var surnameIndex = Math.Max(0, Array.FindIndex(words, IsAllCaps));
        var given = words.Where((_, i) => i != surnameIndex).FirstOrDefault();
        var initial = given is { Length: > 0 } && char.IsLetter(given[0]) ? char.ToUpper(given[0], Ukrainian) + ". " : string.Empty;
        return initial + words[surnameIndex].ToUpper(Ukrainian);
    }

    private static bool IsAllCaps(string word) =>
        word.Length > 1 && word.Any(char.IsLetter) && word == word.ToUpper(Ukrainian);

    /// <summary>Ім'я файлу: «Список вечірньої перевірки 03.11-02.01.2027.xlsx».</summary>
    public static string FileName(DateOnly from, DateOnly to)
        => $"Список вечірньої перевірки {DutyRosterText.PeriodLabel(from, to).Replace(" - ", "-")}.xlsx";
}

/// <summary>Людина в блоку списку: звання і повне ПІБ, у порядку друку.</summary>
public sealed record EveningCheckPerson(string Rank, string FullName, bool IsLimited = false);

/// <summary>Набір на листі: заголовок блоку і люди. Довгий набір друкується кількома блоками по 25 рядків.</summary>
public sealed class EveningCheckGroup
{
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Дві окремі таблиці замість однієї: спершу обмежено придатні, далі придатні - кожна зі своїм заголовком,
    /// блоками по 25 рядків і нумерацією з 1 (<see cref="EveningCheckXlsxExporter.Tables"/>).
    /// </summary>
    public bool SplitByFitness { get; set; }
    public List<EveningCheckPerson> People { get; } = new();
}

/// <summary>Усе для листа: період (сторінки по 31 дню), набори в порядку друку і рядок підпису.</summary>
public sealed class EveningCheckData
{
    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }
    public string Signature { get; set; } = EveningCheckText.SignaturePosition;
    public List<EveningCheckGroup> Groups { get; } = new();

    public int PeopleCount => Groups.Sum(g => g.People.Count);
}

/// <summary>Людина для вибору в блок: з бази, з галочкою в списку.</summary>
public sealed record EveningCheckPersonOption(int RecipientId, string Rank, string FullName, bool IsLimited = false);
