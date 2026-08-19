using ClosedXML.Excel;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Generation;

// Другий шлях, яким та сама відомість потрапляє у файл: експорт зі списку.
// Він теж підставляв підпис курсового офіцера - і теж мовчки віддавав файл із
// порожнім місцем підпису, коли офіцера не знайшлось.
public class ExportCourseOfficerGuardTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-export-{Guid.NewGuid():N}");

    public ExportCourseOfficerGuardTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static byte[] BuildSheetWithSignature()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Відомість");
        sheet.Cell(1, 1).Value = "ПІБ";
        sheet.Cell(2, 1).Value = "{{піб}}";
        sheet.Cell(4, 1).Value = "{{курсовий_офіцер}}";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static int SeedTemplate(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();

        var template = new ExportTemplate
        {
            Name = "Відомість допуску",
            OriginalFileName = "dopusk.xlsx",
            Content = BuildSheetWithSignature(),
            UsesPlaceholders = true,
            TemplateRowIndex = 2,
            UploadedAt = DateTime.Now
        };
        template.ColumnMappings.Add(new ExportTemplateColumnMapping
        {
            ColumnIndex = 1, HeaderText = string.Empty,
            FieldKey = nameof(ExportFieldKey.FullNameFormatted),
            PlaceholderTag = "{{піб}}", SourceType = MappingSourceType.Recipient
        });
        template.ColumnMappings.Add(new ExportTemplateColumnMapping
        {
            ColumnIndex = 0, HeaderText = string.Empty,
            FieldKey = nameof(ExportFieldKey.CourseOfficerSignature),
            PlaceholderTag = "{{курсовий_офіцер}}", SourceType = MappingSourceType.Recipient
        });
        ctx.ExportTemplates.Add(template);
        ctx.SaveChanges();

        return template.Id;
    }

    [Fact]
    public async Task ExportByTemplateAsync_NoCourseOfficer_FailsInsteadOfWritingEmptySignature()
    {
        using var db = new TestDb();
        var templateId = SeedTemplate(db);
        var filePath = Path.Combine(_folder, "відомість.xlsx");

        var service = new ExportService(db.Factory, new FakeAuditLog(), new XlsxGenerationService());

        var result = await service.ExportByTemplateAsync(
            templateId, TemplateFixtures.Roster(2), new Dictionary<string, string>(), filePath);

        Assert.False(result.Success);
        Assert.Contains("курсов", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(filePath));
    }
}
