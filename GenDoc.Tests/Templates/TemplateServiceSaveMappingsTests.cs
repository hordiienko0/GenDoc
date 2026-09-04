using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Templates;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Templates;

public class TemplateServiceSaveMappingsTests
{
    private static (TemplateService Service, int TemplateId, int MappingId) Seed(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();

        var template = new Template
        {
            Name = "Відомість", OriginalFileName = "v.docx",
            Content = Array.Empty<byte>(), UploadedAt = DateTime.Now
        };
        ctx.Templates.Add(template);
        ctx.SaveChanges();

        var mapping = new TemplateFieldMapping
        {
            TemplateId = template.Id,
            PlaceholderTag = "{{піб_ініціали}}",
            SourceType = MappingSourceType.Recipient,
            FieldName = "ShortName"
        };
        ctx.TemplateFieldMappings.Add(mapping);
        ctx.SaveChanges();

        return (new TemplateService(db.Factory, new FakeAuditLog(), new FakeCurrentUser()), template.Id, mapping.Id);
    }

    private static TemplateFieldMapping Stored(TestDb db, int mappingId)
    {
        using var ctx = db.Factory.CreateDbContext();
        return ctx.TemplateFieldMappings.Single(m => m.Id == mappingId);
    }

    [Fact]
    public void SaveMappings_NullFieldNameForAutoSource_KeepsTheStoredField()
    {
        using var db = new TestDb();
        var (service, templateId, mappingId) = Seed(db);

        service.SaveMappings(templateId, new List<(int, MappingSourceType, string?, string?)>
        {
            (mappingId, MappingSourceType.Recipient, null, null)
        });

        var stored = Stored(db, mappingId);
        Assert.Equal(MappingSourceType.Recipient, stored.SourceType);
        Assert.Equal("ShortName", stored.FieldName);
    }

    [Fact]
    public void SaveMappings_ExplicitFieldName_Overwrites()
    {
        using var db = new TestDb();
        var (service, templateId, mappingId) = Seed(db);

        service.SaveMappings(templateId, new List<(int, MappingSourceType, string?, string?)>
        {
            (mappingId, MappingSourceType.Recipient, "LastName", null)
        });

        Assert.Equal("LastName", Stored(db, mappingId).FieldName);
    }

    [Fact]
    public void SaveMappings_ManualSource_ClearsTheField()
    {
        using var db = new TestDb();
        var (service, templateId, mappingId) = Seed(db);

        service.SaveMappings(templateId, new List<(int, MappingSourceType, string?, string?)>
        {
            (mappingId, MappingSourceType.Manual, null, null)
        });

        var stored = Stored(db, mappingId);
        Assert.Equal(MappingSourceType.Manual, stored.SourceType);
        Assert.Null(stored.FieldName);
    }
}
