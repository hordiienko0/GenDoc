using GenDoc.Models;
using GenDoc.Services.Documents;
using GenDoc.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Tests.Archive;

// Групові документи бувають двох ґатунків: XLSX-відомість (ExportTemplateId
// заповнено, TemplateId — null) і груповий DOCX (навпаки). Ланцюг версій
// мусить розрізняти їх обидва.
public class GroupDocumentArchiveTests
{
    // Заводить UserProfile з Id=1: GeneratedGroupDocument.GeneratedByUserId —
    // обов'язковий FK, а FakeCurrentUser лише підмінює контекст виконання,
    // у БД нічого не пише (як і в DocumentVersionChainTests).
    private static void SeedUser(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        var user = new UserProfile
        {
            FullName = "Тест Тестович", PasswordHash = "x", CreatedAt = DateTime.Now
        };
        ctx.Users.Add(user);
        ctx.SaveChanges();
    }

    private static int AddExportTemplate(TestDb db, string name)
    {
        using var ctx = db.Factory.CreateDbContext();
        var template = new ExportTemplate
        {
            Name = name, OriginalFileName = $"{name}.xlsx",
            Content = Array.Empty<byte>(), UploadedAt = DateTime.Now
        };
        ctx.ExportTemplates.Add(template);
        ctx.SaveChanges();
        return template.Id;
    }

    private static int AddDocxTemplate(TestDb db, string name)
    {
        using var ctx = db.Factory.CreateDbContext();
        var template = new Template
        {
            Name = name, OriginalFileName = $"{name}.docx",
            Content = Array.Empty<byte>(), UploadedAt = DateTime.Now,
            Kind = Models.Enums.TemplateKind.Group
        };
        ctx.Templates.Add(template);
        ctx.SaveChanges();
        return template.Id;
    }

    private static int AddGroupDocument(
        TestDb db, int? exportTemplateId, int? templateId, int version, bool isCurrent,
        bool hasContent = true)
    {
        using var ctx = db.Factory.CreateDbContext();
        var doc = new GeneratedGroupDocument
        {
            ExportTemplateId = exportTemplateId,
            TemplateId = templateId,
            GeneratedAt = DateTime.Now.AddMinutes(version),
            GeneratedByUserId = 1,
            FileName = $"group-v{version}.xlsx",
            SizeBytes = 5,
            RecipientCount = 3,
            Version = version,
            IsCurrent = isCurrent,
            HasContent = hasContent,
            Content = hasContent ? new GeneratedGroupDocumentContent { Content = new byte[] { 1, 2 } } : null
        };
        ctx.GeneratedGroupDocuments.Add(doc);
        ctx.SaveChanges();
        return doc.Id;
    }

    // ── Зелені: XLSX-відомості ──────────────────────────────────────

    [Fact]
    public async Task DeleteGroupAsync_Xlsx_PromotesPreviousVersionOfTheSameTemplate()
    {
        using var db = new TestDb();
        SeedUser(db);
        var templateId = AddExportTemplate(db, "Залік");
        var v1 = AddGroupDocument(db, templateId, null, version: 1, isCurrent: false);
        var v2 = AddGroupDocument(db, templateId, null, version: 2, isCurrent: true);

        await TestServices.Archive(db).DeleteGroupAsync(new[] { v2 });

        using var ctx = db.Factory.CreateDbContext();
        Assert.True(ctx.GeneratedGroupDocuments.First(g => g.Id == v1).IsCurrent);
        Assert.NotNull(ctx.GeneratedGroupDocuments.IgnoreQueryFilters().First(g => g.Id == v2).DeletedAt);
    }

    // Видалення відомості одного XLSX-шаблону не повинно чіпати інший шаблон.
    [Fact]
    public async Task DeleteGroupAsync_Xlsx_DoesNotTouchOtherTemplate()
    {
        using var db = new TestDb();
        SeedUser(db);
        var zalik = AddExportTemplate(db, "Залік");
        var dopusk = AddExportTemplate(db, "Допуск");
        var zalikV1 = AddGroupDocument(db, zalik, null, version: 1, isCurrent: false);
        var zalikV2 = AddGroupDocument(db, zalik, null, version: 2, isCurrent: true);
        var dopuskV1 = AddGroupDocument(db, dopusk, null, version: 1, isCurrent: true);

        await TestServices.Archive(db).DeleteGroupAsync(new[] { zalikV2 });

        using var ctx = db.Factory.CreateDbContext();
        Assert.True(ctx.GeneratedGroupDocuments.First(g => g.Id == zalikV1).IsCurrent);
        Assert.True(ctx.GeneratedGroupDocuments.First(g => g.Id == dopuskV1).IsCurrent);
    }

    [Fact]
    public async Task MakeGroupCurrentAsync_Xlsx_MovesFlagWithinTemplate()
    {
        using var db = new TestDb();
        SeedUser(db);
        var templateId = AddExportTemplate(db, "Залік");
        var v1 = AddGroupDocument(db, templateId, null, version: 1, isCurrent: false);
        var v2 = AddGroupDocument(db, templateId, null, version: 2, isCurrent: true);

        await TestServices.Archive(db).MakeGroupCurrentAsync(v1);

        using var ctx = db.Factory.CreateDbContext();
        Assert.True(ctx.GeneratedGroupDocuments.First(g => g.Id == v1).IsCurrent);
        Assert.False(ctx.GeneratedGroupDocuments.First(g => g.Id == v2).IsCurrent);
    }

    // ── Червоні: груповий DOCX (дефект A1) ──────────────────────────

    // ExportTemplateId у групового DOCX — NULL, і умова `g.ExportTemplateId ==
    // doc.ExportTemplateId` перекладається в `IS NULL`, тобто зачіпає всі
    // групові DOCX усіх шаблонів одразу.
    [Fact]
    public async Task DeleteGroupAsync_Docx_DoesNotTouchOtherDocxTemplate()
    {
        using var db = new TestDb();
        SeedUser(db);
        var rapportA = AddDocxTemplate(db, "Рапорт А");
        var rapportB = AddDocxTemplate(db, "Рапорт Б");
        var aV1 = AddGroupDocument(db, null, rapportA, version: 1, isCurrent: false);
        var aV2 = AddGroupDocument(db, null, rapportA, version: 2, isCurrent: true);
        // bV1 навмисно має вищу версію (3), а не 1: інакше aV1 і bV1 дають нічию за
        // Version, і OrderByDescending(Version).FirstOrDefault() без тайбрейка випадково
        // повертає aV1 навіть у зіпсованому (кросс-шаблонному) наборі кандидатів — тест
        // проходив би і на багу, і на фіксі. З version:3 зіпсований запит натомість
        // детерміновано обирає bV1, aV1 лишається непідвищеним, і тест валиться до фіксу.
        var bV1 = AddGroupDocument(db, null, rapportB, version: 3, isCurrent: true);

        await TestServices.Archive(db).DeleteGroupAsync(new[] { aV2 });

        using var ctx = db.Factory.CreateDbContext();
        Assert.True(ctx.GeneratedGroupDocuments.First(g => g.Id == aV1).IsCurrent);
        Assert.True(ctx.GeneratedGroupDocuments.First(g => g.Id == bV1).IsCurrent);
    }

    [Fact]
    public async Task RestoreGroupAsync_Docx_DoesNotStealCurrentFlagFromOtherTemplate()
    {
        using var db = new TestDb();
        SeedUser(db);
        var rapportA = AddDocxTemplate(db, "Рапорт А");
        var rapportB = AddDocxTemplate(db, "Рапорт Б");
        var aV1 = AddGroupDocument(db, null, rapportA, version: 1, isCurrent: true);
        var bV1 = AddGroupDocument(db, null, rapportB, version: 1, isCurrent: true);

        var service = TestServices.Archive(db);
        await service.DeleteGroupAsync(new[] { aV1 });
        await service.RestoreGroupAsync(aV1);

        using var ctx = db.Factory.CreateDbContext();
        Assert.True(ctx.GeneratedGroupDocuments.First(g => g.Id == aV1).IsCurrent);
        Assert.True(ctx.GeneratedGroupDocuments.First(g => g.Id == bV1).IsCurrent);
    }

    // Дефект A2: DOCX-групи віддаються з ExportTemplateId ?? 0 і назвою «—».
    [Fact]
    public async Task QueryGroupAsync_ReturnsDocxGroupsWithTheirTemplateName()
    {
        using var db = new TestDb();
        SeedUser(db);
        var rapport = AddDocxTemplate(db, "Рапорт котлове");
        AddGroupDocument(db, null, rapport, version: 1, isCurrent: true);

        var rows = await TestServices.Archive(db).QueryGroupAsync(new GroupArchiveFilter(null, null, null, 0, 50));

        var row = Assert.Single(rows);
        Assert.Equal("Рапорт котлове", row.TemplateName);
        Assert.True(row.TemplateAlive);
    }

    // Знахідка рев'ю Task 12: id XLSX- і DOCX-шаблонів нумеруються в окремих
    // таблицях з незалежною послідовністю, тож можуть числом випадково збігтися.
    // У свіжій тестовій БД перший запис у кожній з таблиць отримує Id=1 — цього
    // досить, щоб детерміновано відтворити колізію, не підганяючи id вручну.
    // Якщо фільтр перевіряє лише ExportTemplateId (як було до фіксу), вибір
    // DOCX-шаблону в списку поверне чужу XLSX-відомість з тим самим номером —
    // рівно та вада, яку мав усунути цей таск.
    [Fact]
    public async Task QueryGroupAsync_FilterDistinguishesTemplateKindEvenWhenIdsCollide()
    {
        using var db = new TestDb();
        SeedUser(db);
        var xlsxTemplateId = AddExportTemplate(db, "Залік");
        var docxTemplateId = AddDocxTemplate(db, "Рапорт");
        Assert.Equal(xlsxTemplateId, docxTemplateId); // саме та колізія, яку тест перевіряє

        var xlsxDocId = AddGroupDocument(db, xlsxTemplateId, null, version: 1, isCurrent: true);
        var docxDocId = AddGroupDocument(db, null, docxTemplateId, version: 1, isCurrent: true);

        var service = TestServices.Archive(db);

        var docxRows = await service.QueryGroupAsync(new GroupArchiveFilter(null, docxTemplateId, null, 0, 50));
        var docxRow = Assert.Single(docxRows);
        Assert.Equal(docxDocId, docxRow.Id);

        var xlsxRows = await service.QueryGroupAsync(new GroupArchiveFilter(xlsxTemplateId, null, null, 0, 50));
        var xlsxRow = Assert.Single(xlsxRows);
        Assert.Equal(xlsxDocId, xlsxRow.Id);
    }

    // Дефект A3: FirstAsync по таблиці вмісту кидає «Sequence contains no elements».
    [Fact]
    public async Task OpenGroupAsync_DocumentWithoutStoredContent_ReturnsFailureInsteadOfThrowing()
    {
        using var db = new TestDb();
        SeedUser(db);
        var templateId = AddExportTemplate(db, "Залік");
        var docId = AddGroupDocument(db, templateId, null, version: 1, isCurrent: true, hasContent: false);

        var service = TestServices.Archive(db);
        var result = await service.OpenGroupAsync(docId);
        Assert.False(result.Success);
        Assert.Contains("не збережено в архіві", result.ErrorMessage);
    }

    // Фінальне рев'ю, Finding 2: GetGroupVersionsAsync лишався єдиним місцем, яке
    // порівнювало серію вручну (без g.IntakeId, і без переходу через SameGroupSeries,
    // яким керуються DeleteGroupAsync/MakeGroupCurrentAsync/RestoreGroupAsync). Тест
    // покриває обидва ґатунки групових документів і те, що чужий шаблон не потрапляє
    // до переліку версій.
    [Fact]
    public async Task GetGroupVersionsAsync_Xlsx_ReturnsOwnVersionsNewestFirst_ExcludingOtherTemplate()
    {
        using var db = new TestDb();
        SeedUser(db);
        var zalik = AddExportTemplate(db, "Залік");
        var dopusk = AddExportTemplate(db, "Допуск");
        var zalikV1 = AddGroupDocument(db, zalik, null, version: 1, isCurrent: false);
        var zalikV2 = AddGroupDocument(db, zalik, null, version: 2, isCurrent: true);
        AddGroupDocument(db, dopusk, null, version: 1, isCurrent: true);

        var versions = await TestServices.Archive(db).GetGroupVersionsAsync(zalik, null);

        Assert.Equal(new[] { zalikV2, zalikV1 }, versions.Select(v => v.Id));
    }

    [Fact]
    public async Task GetGroupVersionsAsync_Docx_ReturnsOwnVersionsNewestFirst_ExcludingOtherTemplate()
    {
        using var db = new TestDb();
        SeedUser(db);
        var rapportA = AddDocxTemplate(db, "Рапорт А");
        var rapportB = AddDocxTemplate(db, "Рапорт Б");
        var aV1 = AddGroupDocument(db, null, rapportA, version: 1, isCurrent: false);
        var aV2 = AddGroupDocument(db, null, rapportA, version: 2, isCurrent: true);
        AddGroupDocument(db, null, rapportB, version: 1, isCurrent: true);

        var versions = await TestServices.Archive(db).GetGroupVersionsAsync(null, rapportA);

        Assert.Equal(new[] { aV2, aV1 }, versions.Select(v => v.Id));
    }
}
