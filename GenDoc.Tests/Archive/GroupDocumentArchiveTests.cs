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
    [Fact(Skip = "Червоний до Task 12 — ланцюг версій групового DOCX ключується на NULL")]
    public async Task DeleteGroupAsync_Docx_DoesNotTouchOtherDocxTemplate()
    {
        using var db = new TestDb();
        SeedUser(db);
        var rapportA = AddDocxTemplate(db, "Рапорт А");
        var rapportB = AddDocxTemplate(db, "Рапорт Б");
        var aV1 = AddGroupDocument(db, null, rapportA, version: 1, isCurrent: false);
        var aV2 = AddGroupDocument(db, null, rapportA, version: 2, isCurrent: true);
        var bV1 = AddGroupDocument(db, null, rapportB, version: 1, isCurrent: true);

        await TestServices.Archive(db).DeleteGroupAsync(new[] { aV2 });

        using var ctx = db.Factory.CreateDbContext();
        Assert.True(ctx.GeneratedGroupDocuments.First(g => g.Id == aV1).IsCurrent);
        Assert.True(ctx.GeneratedGroupDocuments.First(g => g.Id == bV1).IsCurrent);
    }

    [Fact(Skip = "Червоний до Task 12 — RestoreGroupAsync ключується на NULL ExportTemplateId")]
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
    [Fact(Skip = "Червоний до Task 12 — QueryGroupAsync не показує групові DOCX")]
    public async Task QueryGroupAsync_ReturnsDocxGroupsWithTheirTemplateName()
    {
        using var db = new TestDb();
        SeedUser(db);
        var rapport = AddDocxTemplate(db, "Рапорт котлове");
        AddGroupDocument(db, null, rapport, version: 1, isCurrent: true);

        var rows = await TestServices.Archive(db).QueryGroupAsync(new GroupArchiveFilter(null, null, 0, 50));

        var row = Assert.Single(rows);
        Assert.Equal("Рапорт котлове", row.TemplateName);
        Assert.True(row.TemplateAlive);
    }

    // Дефект A3: FirstAsync по таблиці вмісту кидає «Sequence contains no elements».
    [Fact(Skip = "Червоний до Task 13 — запис без вмісту має давати ArchiveOpResult, а не виняток")]
    public async Task OpenGroupAsync_DocumentWithoutStoredContent_ReturnsFailureInsteadOfThrowing()
    {
        using var db = new TestDb();
        SeedUser(db);
        var templateId = AddExportTemplate(db, "Залік");
        var docId = AddGroupDocument(db, templateId, null, version: 1, isCurrent: true, hasContent: false);

        var service = TestServices.Archive(db);
        // Сигнатуру змінює Task 13; до того часу фіксуємо лише поточну поведінку-виняток.
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.OpenGroupAsync(docId));

        // Після Task 13 (OpenGroupAsync повертає Task<ArchiveOpResult>) замінити тіло на:
        // var result = await service.OpenGroupAsync(docId);
        // Assert.False(result.Success);
        // Assert.Contains("не збережено в архіві", result.ErrorMessage);
    }
}
