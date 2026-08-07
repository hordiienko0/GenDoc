using System.IO;
using GenDoc.Models;

namespace GenDoc.Tests.Infrastructure;

// Шляхи до справжніх шаблонів з теки «шаблони» в корені репозиторію.
// Свідомо БЕЗ копіювання у bin: якщо шаблон перейменували чи прибрали,
// тест має впасти одразу, а не мовчки працювати зі старою копією.
public static class TemplateFixtures
{
    public static string DopuskXlsx => Path(@"Шаблон_Допуск_Додаток_5.xlsx");
    public static string ZalikXlsx => Path(@"Шаблон_Залік_Додаток_8.xlsx");
    public static string RozdavalnaXlsx => Path(@"Шаблон_Роздавальна_відомість.xlsx");
    public static string AnketniXlsx => Path(@"АНКЕТНІ_ДАНІ_29_набір_з_кімнатами_та_зброєю (1).xlsx");
    public static string RaportGroupDocx => Path(@"Шаблон_Рапорт_котлове_ГРУПОВИЙ (3).docx");
    public static string RaportIndividualDocx => Path(@"Шаблон_Рапорт_котлове_ІНДИВІДУАЛЬНИЙ.docx");

    public static byte[] Bytes(string path)
    {
        Assert.True(File.Exists(path), $"Немає файлу шаблону: {path}");
        return File.ReadAllBytes(path);
    }

    private static string Path(string fileName)
        => System.IO.Path.Combine(RepoRoot(), "шаблони", fileName);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(System.IO.Path.Combine(dir.FullName, "шаблони")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Не знайдено теку «шаблони»");
    }

    public static Recipient Person(int id, string lastName, string firstName, string rank = "майор") => new()
    {
        Id = id,
        LastName = lastName,
        FirstName = firstName,
        MiddleName = "Петрович",
        Rank = rank,
        Position = "слухач",
        ServiceNumber = $"СН{id:0000}"
    };

    // Детермінований ростер: прізвища за абеткою, звання чергуються.
    public static List<Recipient> Roster(int count)
    {
        var ranks = new[] { "полковник", "майор", "капітан", "старший лейтенант" };
        return Enumerable.Range(1, count)
            .Select(i => Person(i, $"ПРІЗВИЩЕ{i:00}", $"Ім'я{i:00}", ranks[(i - 1) % ranks.Length]))
            .ToList();
    }
}
