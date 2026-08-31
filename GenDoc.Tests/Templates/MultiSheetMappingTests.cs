using GenDoc.Models.TemplateBuilder;
using GenDoc.Services.Templates;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Templates;

// Відомість конструктора може мати кілька аркушів, а SyncExportMappings читав
// лише `Worksheets.First()`: теги з другого аркуша не діставали мапінгу, тобто
// на генерації лишалися сирими {{тегами}} у готовій книзі (аудит 2026-08-28).
public class MultiSheetMappingTests
{
    private static TemplateBuilderService NewService(TestDb db) => new(db.Factory, new FakeAuditLog());

    // Аркуш 1 - таблиця з рядком-шаблоном; аркуш 2 - смуга з тегом рівня
    // документа. Обидва теги мусять опинитися в мапінгах.
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

    // Той самий тег на обох аркушах - один мапінг, а не два: інакше екран
    // «Мітки» показував би дублі, яких оператор не може розрізнити.
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

    // Рядок-шаблон у книзі один на всі аркуші. Тег, що на ДРУГОМУ аркуші
    // випадково стоїть у рядку з тим самим номером, не має вдавати пер-людинну
    // колонку - інакше він клонувався б разом із рядком.
    [Fact]
    public void TagAtTheSameRowNumberOnAnotherSheet_IsNotTreatedAsPerPerson()
    {
        using var db = new TestDb();
        var service = NewService(db);

        var document = new TemplateBuilderDocument(
            new[]
            {
                // Аркуш 1: шапка в рядку 1, рядок-шаблон у рядку 2.
                new TemplateBlock(TemplateBlockKind.Table, Table: new TableSpec(
                    new[] { new TableColumn("Прізвище", "{{піб}}") }, RepeatPerPerson: true)),
                // Аркуш 2: рядок 1 - заголовок, рядок 2 - той самий номер, що й
                // рядок-шаблон на аркуші 1.
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
