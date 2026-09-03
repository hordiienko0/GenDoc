using System.Text.RegularExpressions;
using ClosedXML.Excel;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Templates;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Templates;

public class ExportTemplateScanRealFilesTests
{
    private static readonly Regex TagRegex = new(@"\{\{[^{}]+\}\}", RegexOptions.Compiled);

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

    [Fact]
    public void Zalik_TemplateRowIsTheDataRow_NotTheUnitNumberRow()
        => Assert.Equal(10, FindRow(TemplateFixtures.ZalikXlsx));

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
