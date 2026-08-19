using System.IO;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Services.Templates;

namespace GenDoc.Tests.Infrastructure;

// Повторює те, що ExportTemplateService.UploadTemplate/BuildPlaceholderMappings робить
// при завантаженні шаблону, - без запису в базу. Тести генерації беруть звідси
// (Row, Mappings) для справжніх файлів з теки «шаблони», не дублюючи цю логіку
// в кожному тестовому файлі.
public static class XlsxTemplateScan
{
    private static readonly Regex PlaceholderRegex = new(@"\{\{[^{}]+\}\}", RegexOptions.Compiled);

    public static (int Row, List<ExportTemplateColumnMapping> Mappings) ForGeneration(string templatePath)
    {
        var content = TemplateFixtures.Bytes(templatePath);

        using var workbook = new XLWorkbook(new MemoryStream(content));
        var usedRange = workbook.Worksheets.First().RangeUsed()
            ?? throw new InvalidOperationException($"Порожній аркуш у шаблоні: {templatePath}");

        var templateRow = ExportTemplateService.FindTemplateRow(usedRange)
            ?? throw new InvalidOperationException($"Рядок-шаблон не знайдено: {templatePath}");

        var mappings = new List<ExportTemplateColumnMapping>();
        var outsideTags = new HashSet<string>(StringComparer.Ordinal);

        foreach (var cell in usedRange.CellsUsed())
        {
            var text = cell.GetString();
            if (!text.Contains("{{")) continue;

            var row = cell.Address.RowNumber;

            foreach (Match match in PlaceholderRegex.Matches(text))
            {
                var tag = match.Value;

                if (row == templateRow)
                {
                    var (sourceType, fieldName) = PlaceholderTagMaps.Classify(tag);
                    mappings.Add(new ExportTemplateColumnMapping
                    {
                        ColumnIndex = cell.Address.ColumnNumber,
                        HeaderText = string.Empty,
                        FieldKey = fieldName ?? string.Empty,
                        PlaceholderTag = tag,
                        SourceType = sourceType
                    });
                }
                else
                {
                    outsideTags.Add(tag);
                }
            }
        }

        foreach (var tag in outsideTags)
        {
            var (sourceType, fieldName) = PlaceholderTagMaps.Classify(tag);
            mappings.Add(new ExportTemplateColumnMapping
            {
                ColumnIndex = 0,
                HeaderText = string.Empty,
                FieldKey = fieldName ?? string.Empty,
                PlaceholderTag = tag,
                SourceType = sourceType
            });
        }

        return (templateRow, mappings);
    }
}
