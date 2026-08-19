using System.Text.RegularExpressions;
using ClosedXML.Excel;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Templates;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Templates;

// Рядок-шаблон - той, що описує ОДНУ людину і клонується на кожного зі списку.
// Помилка тут дає найгучніший симптом: шапка розмножується на всіх слухачів.
public class ExportTemplateScanRealFilesTests
{
    private static readonly Regex TagRegex = new(@"\{\{[^{}]+\}\}", RegexOptions.Compiled);

    // Лише теги, що реально трапляються в цих трьох XLSX-файлах. Навмисно не
    // включаємо DOCX-side ручні теги ({{дата_прибуття}}, {{звання_підписанта}}
    // тощо) - за ними стежать тести DOCX-скану; надто широкий білий список тут
    // мовчки відкрив би саме ту пастку, проти якої існує цей тест.
    private static readonly HashSet<string> ManualTagWhitelist = new(StringComparer.Ordinal)
    {
        "{{дата_аркуша}}",
        "{{номери_вправ}}", "{{звання_начальника}}", "{{піб_начальника}}",
        "{{опис_підрозділу}}", "{{причина_інструктажу}}",
        "{{калібр}}", "{{кількість_патронів}}", "{{номер_відомості}}"
    };

    private static int? FindRow(string path)
    {
        using var workbook = new XLWorkbook(new MemoryStream(TemplateFixtures.Bytes(path)));
        return ExportTemplateService.FindTemplateRow(workbook.Worksheets.First().RangeUsed()!);
    }

    [Fact]
    public void Dopusk_TemplateRowIsTheDataRow_NotTheHeader()
        => Assert.Equal(7, FindRow(TemplateFixtures.DopuskXlsx));

    // Пін: у справжньому файлі рядком-шаблоном визначається рядок 10 (дані), а не
    // рядок 5/6 з номером в/ч чи описом підрозділу. Це не розрізняє «найбільше
    // тегів людини» від «найбільше тегів загалом» - на цьому файлі обидва правила
    // дають ту саму відповідь; за розрізнення цих двох правил відповідає синтетичний
    // тест FindTemplateRow_RowWithMoreManualTags_LosesToRowWithRecipientTags.
    [Fact]
    public void Zalik_TemplateRowIsTheDataRow_NotTheUnitNumberRow()
        => Assert.Equal(10, FindRow(TemplateFixtures.ZalikXlsx));

    // Пін: у справжньому файлі рядком-шаблоном визначається рядок 9 (дані), хоча в
    // ньому лише ОДИН тег людини ({{піб_ініціали}}). Це не розрізняє «найбільше
    // тегів людини» від «найбільше тегів загалом» - на цьому файлі обидва правила
    // дають ту саму відповідь; за розрізнення цих двох правил відповідає синтетичний
    // тест FindTemplateRow_RowWithMoreManualTags_LosesToRowWithRecipientTags.
    [Fact]
    public void Rozdavalna_TemplateRowIsTheDataRow_DespiteSingleRecipientTag()
        => Assert.Equal(9, FindRow(TemplateFixtures.RozdavalnaXlsx));

    [Fact]
    public void AnketniDani_HasNoPlaceholders_SoItStaysHeaderDriven()
        => Assert.Null(FindRow(TemplateFixtures.AnketniXlsx));

    [Theory]
    [InlineData("dopusk")]
    [InlineData("zalik")]
    [InlineData("rozdavalna")]
    public void EveryTagInTemplate_IsEitherMappedOrExplicitlyManual(string which)
    {
        var path = which switch
        {
            "dopusk" => TemplateFixtures.DopuskXlsx,
            "zalik" => TemplateFixtures.ZalikXlsx,
            _ => TemplateFixtures.RozdavalnaXlsx
        };

        using var workbook = new XLWorkbook(new MemoryStream(TemplateFixtures.Bytes(path)));
        var tags = workbook.Worksheets.First().RangeUsed()!.CellsUsed()
            .SelectMany(c => TagRegex.Matches(c.GetString()).Select(m => m.Value))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var unexpectedManual = tags
            .Where(tag => PlaceholderTagMaps.Classify(tag).SourceType == MappingSourceType.Manual)
            .Where(tag => !ManualTagWhitelist.Contains(tag))
            .ToList();

        Assert.True(unexpectedManual.Count == 0,
            "Ці теги мовчки впали в Manual - додайте їх у PlaceholderTagMaps або в білий список тесту: "
            + string.Join(", ", unexpectedManual));
    }

    // Друга половина тієї самої пастки: у header-driven шаблоні колонка, чий
    // заголовок не розпізнано, тихо отримує ExportFieldKey.Empty.
    [Fact]
    public void AnketniDani_EveryHeaderColumnIsRecognised()
    {
        using var workbook = new XLWorkbook(new MemoryStream(TemplateFixtures.Bytes(TemplateFixtures.AnketniXlsx)));
        var usedRange = workbook.Worksheets.First().RangeUsed()!;
        var headerRow = usedRange.FirstRow();

        var unrecognised = new List<string>();
        for (var c = 1; c <= usedRange.ColumnCount(); c++)
        {
            var header = headerRow.Cell(c).GetString().Trim();
            if (header.Length == 0) continue;

            if (ExportTemplateService.AutoMapHeaderForTests(header) == ExportFieldKey.Empty)
                unrecognised.Add($"колонка {c}: «{header}»");
        }

        Assert.True(unrecognised.Count == 0,
            "Заголовки не розпізнано, колонка буде порожньою: " + string.Join("; ", unrecognised));
    }
}
