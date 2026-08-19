using GenDoc.Models.TemplateBuilder;
using GenDoc.Services.Templates;

namespace GenDoc.Tests.Templates;

// Розкладка - спільна для writer'а і для підписів у конструкторі («рядки 2–3»,
// «A–C»). Якщо вони розійдуться, оператор бачитиме одне, а в книзі буде інше -
// саме той клас помилок, через який мапи тегів колись звели в одне місце.
public class TemplateSheetLayoutTests
{
    [Fact]
    public void Rows_are_assigned_in_document_order()
    {
        var blocks = new[]
        {
            new TemplateBlock(TemplateBlockKind.Title, "ВІДОМІСТЬ"),
            new TemplateBlock(TemplateBlockKind.Paragraph, "перший\nдругий"),
            new TemplateBlock(TemplateBlockKind.Table, Table: new TableSpec(new[]
            {
                new TableColumn("№", "{{номер}}"),
                new TableColumn("ПІБ", "{{піб}}")
            }, RepeatPerPerson: true)),
            new TemplateBlock(TemplateBlockKind.Signatures, Signatures: new[]
            {
                new SignatureLine("Склав", null),
                new SignatureLine("Перевірив", null)
            })
        };

        var layout = TemplateSheetLayout.Compute(blocks);

        Assert.Equal((1, 1), (layout.Placements[0].FirstRow, layout.Placements[0].LastRow));
        Assert.Equal((2, 3), (layout.Placements[1].FirstRow, layout.Placements[1].LastRow));
        Assert.Equal((4, 5), (layout.Placements[2].FirstRow, layout.Placements[2].LastRow));
        Assert.Equal((6, 7), (layout.Placements[3].FirstRow, layout.Placements[3].LastRow));

        Assert.Equal(4, layout.HeaderRowIndex);
        Assert.Equal(5, layout.TemplateRowIndex);
        Assert.Equal(2, layout.ColumnCount);
        Assert.Equal("A1:B7", TemplateSheetLayout.Range(layout));
    }

    [Fact]
    public void Sheet_without_a_table_reports_no_template_row()
    {
        var layout = TemplateSheetLayout.Compute(new[]
        {
            new TemplateBlock(TemplateBlockKind.Title, "Довідка")
        });

        Assert.Equal(0, layout.TemplateRowIndex);
        Assert.Equal("A1:A1", TemplateSheetLayout.Range(layout));
    }

    [Theory]
    [InlineData(1, "A")]
    [InlineData(3, "C")]
    [InlineData(26, "Z")]
    [InlineData(27, "AA")]
    [InlineData(28, "AB")]
    public void Column_letters_match_excel(int index, string expected)
        => Assert.Equal(expected, TemplateSheetLayout.ColumnLetter(index));

    // Найважливіше: те, що показує розкладка, і те, що написав writer, - одне й те саме.
    [Fact]
    public void Writer_puts_the_template_row_where_the_layout_says()
    {
        var blocks = new[]
        {
            new TemplateBlock(TemplateBlockKind.Title, "ВІДОМІСТЬ"),
            new TemplateBlock(TemplateBlockKind.Paragraph, "рядок\nще рядок"),
            new TemplateBlock(TemplateBlockKind.Table, Table: new TableSpec(new[]
            {
                new TableColumn("ПІБ", "{{піб}}")
            }, RepeatPerPerson: true))
        };

        var document = new TemplateBuilderDocument(blocks, Mode: TemplateBuilderMode.Excel);
        var layout = TemplateSheetLayout.Compute(blocks);

        Assert.Equal(layout.TemplateRowIndex, TemplateBlockXlsxWriter.Write(document).TemplateRowIndex);
    }
}
