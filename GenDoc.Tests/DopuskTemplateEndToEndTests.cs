using System.IO;
using ClosedXML.Excel;
using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Services.Templates;

namespace GenDoc.Tests;

public class DopuskTemplateEndToEndTests
{
    private static string TemplatePath =>
        Path.Combine(RepoRoot(), "шаблони", "Шаблон_Допуск_Додаток_5.xlsx");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "шаблони")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Не знайдено теку «шаблони»");
    }

    private static Recipient Person(int id, string lastName, string firstName, string rank) => new()
    {
        Id = id,
        LastName = lastName,
        FirstName = firstName,
        Rank = rank,
        Position = "слухач"
    };

    [Fact]
    public void RealDopuskTemplate_FillsOneRowPerPerson_WithoutCloningTheHeader()
    {
        Assert.True(File.Exists(TemplatePath), $"Немає файлу шаблону: {TemplatePath}");
        var content = File.ReadAllBytes(TemplatePath);

        int templateRowIndex;
        var mappings = new List<ExportTemplateColumnMapping>();
        using (var probe = new XLWorkbook(new MemoryStream(content)))
        {
            var range = probe.Worksheets.First().RangeUsed()!;
            templateRowIndex = ExportTemplateService.FindTemplateRow(range)
                ?? throw new InvalidOperationException("Рядок-шаблон не знайдено");

            foreach (var cell in range.CellsUsed())
            {
                if (cell.Address.RowNumber != templateRowIndex) continue;
                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(cell.GetString(), @"\{\{[^{}]+\}\}"))
                {
                    var (sourceType, fieldName) = PlaceholderTagMaps.Classify(m.Value);
                    mappings.Add(new ExportTemplateColumnMapping
                    {
                        ColumnIndex = cell.Address.ColumnNumber,
                        HeaderText = string.Empty,
                        FieldKey = fieldName ?? string.Empty,
                        PlaceholderTag = m.Value,
                        SourceType = sourceType
                    });
                }
            }
        }

        Assert.Equal(7, templateRowIndex);

        var roster = new[]
        {
            Person(1, "КУЧЕРЕНКО", "Василь", "майор"),
            Person(2, "ШЕВЧЕНКО", "Тарас", "капітан"),
            Person(3, "ЛИСЕНКО", "Микола", "старший лейтенант")
        };

        var result = new XlsxGenerationService().Generate(
            content, templateRowIndex, usesPlaceholders: true, mappings, roster,
            org: new OrganizationSettings { UnitNumber = "Хнупс" },
            manualValues: new Dictionary<string, string>());

        Assert.True(result.Success, result.ErrorMessage);

        using var produced = new XLWorkbook(new MemoryStream(result.Content!));
        var sheet = produced.Worksheets.First();
        var cells = sheet.RangeUsed()!.CellsUsed().Select(c => c.GetString()).ToList();

        var headerCount = cells.Count(t => t.Contains("результатів контрольного заняття"));
        Assert.Equal(1, headerCount);

        foreach (var person in roster)
            Assert.Contains(cells, t => t.Contains(person.LastName));

        Assert.Contains(cells, t => t.Contains("майор"));
        Assert.DoesNotContain(cells, t => t.Contains("{{піб}}") || t.Contains("{{звання}}") || t.Contains("{{№}}"));
    }
}
