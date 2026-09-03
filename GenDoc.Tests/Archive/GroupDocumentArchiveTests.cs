using GenDoc.Models;
using GenDoc.Services.Documents;
using GenDoc.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Tests.Archive;

public class GroupDocumentArchiveTests
{
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

    [Fact]
    public async Task DeleteGroupAsync_Docx_DoesNotTouchOtherDocxTemplate()
    {
        using var db = new TestDb();
        SeedUser(db);
        var rapportA = AddDocxTemplate(db, "Рапорт А");
        var rapportB = AddDocxTemplate(db, "Рапорт Б");
        var aV1 = AddGroupDocument(db, null, rapportA, version: 1, isCurrent: false);
        var aV2 = AddGroupDocument(db, null, rapportA, version: 2, isCurrent: true);
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

    [Fact]
    public async Task QueryGroupAsync_FilterDistinguishesTemplateKindEvenWhenIdsCollide()
    {
        using var db = new TestDb();
        SeedUser(db);
        var xlsxTemplateId = AddExportTemplate(db, "Залік");
        var docxTemplateId = AddDocxTemplate(db, "Рапорт");
        Assert.Equal(xlsxTemplateId, docxTemplateId);

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
