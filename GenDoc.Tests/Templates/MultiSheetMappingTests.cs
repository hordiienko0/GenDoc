using GenDoc.Models.TemplateBuilder;
using GenDoc.Services.Templates;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Templates;

public class MultiSheetMappingTests
{
    private static TemplateBuilderService NewService(TestDb db) => new(db.Factory, new FakeAuditLog());

    private static TemplateBuilderDocument TwoSheets() => new(
        new[]
        {
            new TemplateBlock(TemplateBlockKind.Table, Table: new TableSpec(
                new[] { new TableColumn("Прізвище", "{{піб}}") }, RepeatPerPerson: true)),
            new TemplateBlock(TemplateBlockKind.Title, "Додаток до в/ч {{номер_вч}}", SheetIndex: 1)
        },
        Mode: TemplateBuilderMode.Excel,
        SheetNames: new[] { "Відомість", "Додаток" });

    [Fact]
    public void TagOnTheSecondSheet_GetsAMapping()
    {
        using var db = new TestDb();
        var service = NewService(db);

        var id = service.Save(null, "Відомість на два аркуші", TwoSheets());

        using var ctx = db.Factory.CreateDbContext();
        var template = ctx.ExportTemplates.Find(id)!;
        ctx.Entry(template).Collection(t => t.ColumnMappings).Load();

        Assert.Contains(template.ColumnMappings, m => m.PlaceholderTag == "{{номер_вч}}");
    }

    [Fact]
    public void TagOnTheFirstSheet_KeepsItsTemplateRowColumn()
    {
        using var db = new TestDb();
        var service = NewService(db);

        var id = service.Save(null, "Відомість на два аркуші", TwoSheets());

        using var ctx = db.Factory.CreateDbContext();
        var template = ctx.ExportTemplates.Find(id)!;
        ctx.Entry(template).Collection(t => t.ColumnMappings).Load();

        var perPerson = Assert.Single(template.ColumnMappings, m => m.PlaceholderTag == "{{піб}}");
        Assert.Equal(1, perPerson.ColumnIndex);
    }

    [Fact]
    public void TheSameTagOnBothSheets_GivesOneMapping()
    {
        using var db = new TestDb();
        var service = NewService(db);

        var document = new TemplateBuilderDocument(
            new[]
            {
                new TemplateBlock(TemplateBlockKind.Table, Table: new TableSpec(
                    new[] { new TableColumn("Прізвище", "{{піб}}") }, RepeatPerPerson: true)),
                new TemplateBlock(TemplateBlockKind.Title, "в/ч {{номер_вч}}"),
                new TemplateBlock(TemplateBlockKind.Title, "в/ч {{номер_вч}}", SheetIndex: 1)
            },
            Mode: TemplateBuilderMode.Excel,
            SheetNames: new[] { "Відомість", "Додаток" });

        var id = service.Save(null, "Відомість із повтореним тегом", document);

        using var ctx = db.Factory.CreateDbContext();
        var template = ctx.ExportTemplates.Find(id)!;
        ctx.Entry(template).Collection(t => t.ColumnMappings).Load();

        Assert.Single(template.ColumnMappings, m => m.PlaceholderTag == "{{номер_вч}}");
    }

    [Fact]
    public void TagAtTheSameRowNumberOnAnotherSheet_IsNotTreatedAsPerPerson()
    {
        using var db = new TestDb();
        var service = NewService(db);

        var document = new TemplateBuilderDocument(
            new[]
            {
                new TemplateBlock(TemplateBlockKind.Table, Table: new TableSpec(
                    new[] { new TableColumn("Прізвище", "{{піб}}") }, RepeatPerPerson: true)),
                new TemplateBlock(TemplateBlockKind.Title, "Додаток", SheetIndex: 1),
                new TemplateBlock(TemplateBlockKind.Paragraph, "Склав {{посада_командира}}", SheetIndex: 1)
            },
            Mode: TemplateBuilderMode.Excel,
            SheetNames: new[] { "Відомість", "Додаток" });

        var id = service.Save(null, "Відомість із збігом номерів рядків", document);

        using var ctx = db.Factory.CreateDbContext();
        var template = ctx.ExportTemplates.Find(id)!;
        ctx.Entry(template).Collection(t => t.ColumnMappings).Load();

        var mapping = Assert.Single(template.ColumnMappings, m => m.PlaceholderTag == "{{посада_командира}}");
        Assert.Equal(0, mapping.ColumnIndex);
    }
}
