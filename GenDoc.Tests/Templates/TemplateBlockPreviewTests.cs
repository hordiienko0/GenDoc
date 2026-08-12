using GenDoc.Models.TemplateBuilder;
using GenDoc.Services.Templates;

namespace GenDoc.Tests.Templates;

// Прев'ю — дзеркало TemplateBlockDocxWriter на екрані. Тут перевіряємо саме те, що
// бачить оператор: звідки взялося кожне значення (легенда «з бази» / «вручну»).
public class TemplateBlockPreviewTests
{
    private static readonly Dictionary<string, string> NoValues = new();

    private static List<PreviewLine> Lines(
        TemplateBuilderDocument document,
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<int, SignatoryInfo>? signatories = null)
        => TemplateBlockPreview.Build(document, values, signatories).OfType<PreviewLine>().ToList();

    [Fact]
    public void Manual_tag_becomes_the_generation_time_placeholder()
    {
        var document = new TemplateBuilderDocument(new[]
        {
            new TemplateBlock(TemplateBlockKind.Paragraph, "Наказ № {{номер_наказу}}.")
        });

        var runs = Assert.Single(Lines(document, NoValues)).Runs;

        var manual = Assert.Single(runs.Where(r => r.Kind == PreviewRunKind.ManualValue));
        Assert.Equal(TemplateBlockPreview.ManualPlaceholder, manual.Text);
        Assert.Equal("Наказ № ", runs[0].Text);
    }

    [Fact]
    public void Database_tag_is_replaced_with_the_test_person_value()
    {
        var document = new TemplateBuilderDocument(new[]
        {
            new TemplateBlock(TemplateBlockKind.Paragraph, "Я, {{звання}} {{піб}}.")
        });

        var values = new Dictionary<string, string>
        {
            ["{{звання}}"] = "солдат",
            ["{{піб}}"] = "ТКАЧЕНКО Олег Юрійович"
        };

        var runs = Assert.Single(Lines(document, values)).Runs;
        var fromDb = runs.Where(r => r.Kind == PreviewRunKind.DbValue).Select(r => r.Text).ToList();

        Assert.Equal(new[] { "солдат", "ТКАЧЕНКО Олег Юрійович" }, fromDb);
    }

    // Порожнє значення показуємо самим тегом: інакше слово просто зникає з прев'ю
    // і незрозуміло, чи то поле не заповнене, чи то мітку не розпізнано.
    [Fact]
    public void Empty_database_value_shows_the_tag_itself()
    {
        var document = new TemplateBuilderDocument(new[]
        {
            new TemplateBlock(TemplateBlockKind.Paragraph, "Група {{група}}.")
        });

        var runs = Assert.Single(Lines(document, new Dictionary<string, string>
        {
            ["{{група}}"] = string.Empty
        })).Runs;

        var value = Assert.Single(runs.Where(r => r.Kind == PreviewRunKind.DbValue));
        Assert.Equal("{{група}}", value.Text);
    }

    [Fact]
    public void Layout_mirrors_the_docx_writer()
    {
        var document = new TemplateBuilderDocument(new[]
        {
            new TemplateBlock(TemplateBlockKind.Header, "ЗАТВЕРДЖУЮ\nНачальник курсу"),
            new TemplateBlock(TemplateBlockKind.Title, "АКТ"),
            new TemplateBlock(TemplateBlockKind.Paragraph, "Текст")
        });

        var lines = Lines(document, NoValues);

        // Гриф — праворуч і окремим рядком на кожен перенос, заголовок — по центру
        // й напівжирний, абзац — за шириною: рівно як пише writer.
        Assert.Equal(4, lines.Count);
        Assert.Equal(BlockAlignment.Right, lines[0].Style.Alignment);
        Assert.Equal("Начальник курсу", lines[1].Runs.Single().Text);
        Assert.Equal(BlockAlignment.Center, lines[2].Style.Alignment);
        Assert.True(lines[2].Style.Bold);
        Assert.Equal(BlockAlignment.Justify, lines[3].Style.Alignment);
    }

    [Fact]
    public void Signature_shows_rank_and_name_of_the_picked_person()
    {
        var document = new TemplateBuilderDocument(new[]
        {
            new TemplateBlock(TemplateBlockKind.Signatures, Signatures: new[]
            {
                new SignatureLine("Прийняв", 7)
            })
        });

        var signatories = new Dictionary<int, SignatoryInfo> { [7] = new("майор", "Даниленко Є. О.") };

        var runs = Assert.Single(Lines(document, NoValues, signatories)).Runs;

        Assert.Equal("Прийняв: ", runs[0].Text);
        Assert.Equal(new[] { "майор", "Даниленко Є. О." },
            runs.Where(r => r.Kind == PreviewRunKind.DbValue).Select(r => r.Text));
        Assert.Contains(runs, r => r.Text.Contains("____"));
    }

    // Excel-режим: прев'ю показує сітку з одним рядком даних — рівно тим рядком,
    // який на генерації клонується по одному на людину.
    [Fact]
    public void Table_becomes_a_grid_with_one_substituted_row()
    {
        var document = new TemplateBuilderDocument(new[]
        {
            new TemplateBlock(TemplateBlockKind.Table, Table: new TableSpec(new[]
            {
                new TableColumn("Звання", "{{звання}}"),
                new TableColumn("Наказ", "{{номер_наказу}}")
            }, RepeatPerPerson: true))
        }, Mode: TemplateBuilderMode.Excel);

        var table = Assert.IsType<PreviewTable>(Assert.Single(
            TemplateBlockPreview.Build(document, new Dictionary<string, string> { ["{{звання}}"] = "солдат" })));

        Assert.Equal(new[] { "Звання", "Наказ" }, table.Headers);
        Assert.Equal("солдат", Assert.Single(table.Cells[0]).Text);
        Assert.Equal(PreviewRunKind.DbValue, table.Cells[0][0].Kind);

        // Ручний тег у колонці лишається плашкою — так само, як у тексті документа.
        Assert.Equal(PreviewRunKind.ManualValue, Assert.Single(table.Cells[1]).Kind);
    }

    [Fact]
    public void Collects_tags_from_table_columns_too()
    {
        var document = new TemplateBuilderDocument(new[]
        {
            new TemplateBlock(TemplateBlockKind.Table, Table: new TableSpec(new[]
            {
                new TableColumn("ПІБ", "{{піб}}")
            }, RepeatPerPerson: true))
        }, Mode: TemplateBuilderMode.Excel);

        Assert.Equal(new[] { "{{піб}}" }, TemplateBlockPreview.CollectTags(document));
    }

    [Fact]
    public void Collects_every_tag_once_in_document_order()
    {
        var document = new TemplateBuilderDocument(new[]
        {
            new TemplateBlock(TemplateBlockKind.Header, "{{звання_командира}} {{піб_командира}}"),
            new TemplateBlock(TemplateBlockKind.Paragraph, "{{піб}} і ще раз {{піб}}")
        });

        Assert.Equal(
            new[] { "{{звання_командира}}", "{{піб_командира}}", "{{піб}}" },
            TemplateBlockPreview.CollectTags(document));
    }
}
