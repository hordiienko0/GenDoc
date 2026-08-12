using ClosedXML.Excel;
using GenDoc.Models.TemplateBuilder;
using GenDoc.Services;
using GenDoc.Services.Templates;

namespace GenDoc.Tests.Templates;

public class TemplateBlockXlsxWriterTests
{
    private static TemplateBuilderDocument Vidomist(params TemplateBlock[] extra)
    {
        var blocks = new List<TemplateBlock>
        {
            new(TemplateBlockKind.Title, "ВІДОМІСТЬ обліку"),
            new(TemplateBlockKind.Table, Table: new TableSpec(new[]
            {
                new TableColumn("№ з/п", "{{номер}}"),
                new TableColumn("Звання", "{{звання}}"),
                new TableColumn("ПІБ", "{{піб}}")
            }, RepeatPerPerson: true))
        };
        blocks.AddRange(extra);

        return new TemplateBuilderDocument(blocks, Mode: TemplateBuilderMode.Excel);
    }

    private static IXLWorksheet SheetOf(byte[] xlsx)
    {
        var workbook = new XLWorkbook(new MemoryStream(xlsx));
        return workbook.Worksheets.First();
    }

    [Fact]
    public void Writes_header_row_and_template_row_under_the_title()
    {
        var result = TemplateBlockXlsxWriter.Write(Vidomist());
        var sheet = SheetOf(result.Content);

        Assert.Equal("ВІДОМІСТЬ обліку", sheet.Cell(1, 1).GetString());
        Assert.Equal(new[] { "№ з/п", "Звання", "ПІБ" },
            new[] { sheet.Cell(2, 1).GetString(), sheet.Cell(2, 2).GetString(), sheet.Cell(2, 3).GetString() });
        Assert.Equal("{{піб}}", sheet.Cell(3, 3).GetString());
        Assert.Equal(3, result.TemplateRowIndex);
    }

    // Головне про Excel-режим: зібрана книга мусить читатись наявним конвеєром
    // так само, як книга, завантажена файлом. Тому рядок-шаблон, який знайде
    // ExportTemplateService, має збігтися з тим, який порахував writer — інакше
    // клонувався б заголовок, а рядок даних лишався б порожнім.
    [Fact]
    public void Template_row_matches_the_one_the_upload_scanner_would_find()
    {
        var result = TemplateBlockXlsxWriter.Write(Vidomist(
            new TemplateBlock(TemplateBlockKind.Signatures, Signatures: new[] { new SignatureLine("Склав", 7) })));

        var sheet = SheetOf(result.Content);
        var found = ExportTemplateService.FindTemplateRow(sheet.RangeUsed()!);

        Assert.Equal(result.TemplateRowIndex, found);
    }

    [Fact]
    public void Title_and_signature_rows_span_the_whole_table_width()
    {
        var result = TemplateBlockXlsxWriter.Write(Vidomist(
            new TemplateBlock(TemplateBlockKind.Signatures, Signatures: new[] { new SignatureLine("Склав", 7) })));

        var sheet = SheetOf(result.Content);

        // Заголовок і рядок підпису — на всю ширину таблиці, а не в першу колонку.
        Assert.Contains(sheet.MergedRanges, m =>
            m.RangeAddress.FirstAddress.RowNumber == 1 && m.RangeAddress.LastAddress.ColumnNumber == 3);
        Assert.Contains(sheet.MergedRanges, m =>
            m.RangeAddress.FirstAddress.RowNumber == 4 && m.RangeAddress.LastAddress.ColumnNumber == 3);

        // Підписанта не передали — лишається місце під ручний підпис.
        Assert.Equal("Склав: _______________", sheet.Cell(4, 1).GetString());
    }

    [Fact]
    public void Signature_uses_the_person_picked_from_permanent_staff()
    {
        var result = TemplateBlockXlsxWriter.Write(
            Vidomist(new TemplateBlock(TemplateBlockKind.Signatures, Signatures: new[] { new SignatureLine("Склав", 7) })),
            new Dictionary<int, SignatoryInfo> { [7] = new("майор", "Даниленко Є. О.") });

        var sheet = SheetOf(result.Content);

        Assert.Equal("Склав: майор _______________ Даниленко Є. О.", sheet.Cell(4, 1).GetString());
    }

    // Тег у клітинці мусить лишитись текстом: "{{номер}}" схожий на що завгодно,
    // але підстановка на генерації працює лише з рядком.
    [Fact]
    public void Tags_stay_plain_text()
    {
        var sheet = SheetOf(TemplateBlockXlsxWriter.Write(Vidomist()).Content);

        Assert.Equal(XLDataType.Text, sheet.Cell(3, 1).DataType);
        Assert.Equal("{{номер}}", sheet.Cell(3, 1).GetString());
    }

    // Аркуші: блоки розкладаються за SheetIndex, назви беруться з документа,
    // а рядок-шаблон у книзі один — той, що на аркуші з таблицею.
    [Fact]
    public void Blocks_go_to_their_own_sheets()
    {
        var document = new TemplateBuilderDocument(new[]
        {
            new TemplateBlock(TemplateBlockKind.Title, "ВІДОМІСТЬ"),
            new TemplateBlock(TemplateBlockKind.Table, Table: new TableSpec(new[]
            {
                new TableColumn("ПІБ", "{{піб}}")
            }, RepeatPerPerson: true)),
            new TemplateBlock(TemplateBlockKind.Title, "ДОВІДКОВО", SheetIndex: 1),
            new TemplateBlock(TemplateBlockKind.Paragraph, "Військова частина {{номер_вч}}", SheetIndex: 1)
        }, Mode: TemplateBuilderMode.Excel, SheetNames: new[] { "Основний", "Довідка" });

        var result = TemplateBlockXlsxWriter.Write(document);
        using var workbook = new XLWorkbook(new MemoryStream(result.Content));

        Assert.Equal(new[] { "Основний", "Довідка" }, workbook.Worksheets.Select(w => w.Name));
        Assert.Equal("{{піб}}", workbook.Worksheet("Основний").Cell(3, 1).GetString());
        Assert.Equal("ДОВІДКОВО", workbook.Worksheet("Довідка").Cell(1, 1).GetString());
        Assert.Equal("Військова частина {{номер_вч}}", workbook.Worksheet("Довідка").Cell(2, 1).GetString());

        // Рядок-шаблон — з аркуша, де є таблиця.
        Assert.Equal(3, result.TemplateRowIndex);
    }

    [Theory]
    [InlineData("Відомість [2025]", "Відомість 2025")]
    [InlineData("   ", "Відомість")]
    [InlineData("a/b:c*d?e", "abcde")]
    public void Sheet_names_are_cleaned_for_excel(string raw, string expected)
        => Assert.Equal(expected, TemplateBlockXlsxWriter.SafeSheetName(raw, 0));

    [Fact]
    public void Document_without_a_table_reports_no_template_row()
    {
        var document = new TemplateBuilderDocument(
            new[] { new TemplateBlock(TemplateBlockKind.Title, "Без таблиці") },
            Mode: TemplateBuilderMode.Excel);

        Assert.Equal(0, TemplateBlockXlsxWriter.Write(document).TemplateRowIndex);
    }
}
