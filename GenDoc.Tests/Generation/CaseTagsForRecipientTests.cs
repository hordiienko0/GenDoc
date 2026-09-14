using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Templates;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Generation;

/// <summary>
/// Теги відмінків для рапортів про хворобу й відпустку: посада в родовому («начальника курсів…»),
/// звання/ПІБ/посада в давальному («капітану БУГАЮ Сергію Михайловичу, курсовому офіцеру…»)
/// і дієслово «приступив/приступила». Усе заповнюється з картки людини, без ручного вводу.
/// </summary>
public class CaseTagsForRecipientTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-case-{Guid.NewGuid():N}"[..18]);

    public CaseTagsForRecipientTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static readonly IProgress<string> NoProgress = new Progress<string>(_ => { });

    private static readonly string[] CaseTags =
    {
        "{{посада_зв}}", "{{звання_дв}}", "{{піб_дв}}", "{{посада_дв}}",
        "{{приступив}}", "{{ознайомлений}}", "{{піб_підпис}}"
    };

    private static byte[] TemplateWithCaseTags()
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, DocumentFormat.OpenXml.WordprocessingDocumentType.Document, true))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(
                new Paragraph(new Run(new Text("Прошу {{звання_зв}} {{піб_зв}}, {{посада_зв}}, вважати таким."))),
                new Paragraph(new Run(new Text("Надати {{звання_дв}} {{піб_дв}}, {{посада_дв}}, відпустку."))),
                new Paragraph(new Run(new Text("Вважати таким, що {{приступив}} до виконання обов'язків."))),
                new Paragraph(new Run(new Text("З правилами {{ознайомлений}}. Підпис: {{звання}} {{піб_підпис}}.")))));
            main.Document.Save();
        }
        return ms.ToArray();
    }

    private static string TextOf(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        return string.Concat(doc.MainDocumentPart!.Document!.Body!.Descendants<Text>().Select(t => t.Text));
    }

    private (int TemplateId, int PersonId) Seed(TestDb db, Gender gender)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
        ctx.OrganizationSettings.Add(new OrganizationSettings
        {
            UnitNumber = "А1234", City = "Львів", CommanderRank = "полковник",
            CommanderFullName = "І. ПЕТРЕНКО", CommanderPosition = "начальник",
            HrOfficerFullName = "К. КАДРОВ", UnitFullName = "Коледж"
        });
        var intake = new Intake { Number = 30, DisplayNumber = "Набір №30" };
        ctx.Intakes.Add(intake);

        var template = new Template
        {
            Name = "Шаблон_Відмінки", OriginalFileName = "cases.docx",
            Content = TemplateWithCaseTags(), UploadedAt = DateTime.Now,
            Kind = TemplateKind.PerRecipient
        };
        ctx.Templates.Add(template);

        var person = gender == Gender.Male
            ? new Recipient
            {
                LastName = "Бугай", FirstName = "Сергій", MiddleName = "Михайлович",
                Rank = "капітан", Gender = Gender.Male,
                Position = "курсовий офіцер курсів перепідготовки та підвищення кваліфікації"
            }
            : new Recipient
            {
                LastName = "Ковальська", FirstName = "Олена", MiddleName = "Іванівна",
                Rank = "капітан", Gender = Gender.Female,
                Position = "начальник служби захисту"
            };
        ctx.Recipients.Add(person);
        ctx.SaveChanges();

        person.IntakeId = intake.Id;
        foreach (var tag in CaseTags.Concat(new[] { "{{звання_зв}}", "{{піб_зв}}", "{{звання}}" }))
        {
            var (sourceType, fieldName) = PlaceholderTagMaps.Classify(tag);
            ctx.TemplateFieldMappings.Add(new TemplateFieldMapping
            {
                TemplateId = template.Id, PlaceholderTag = tag,
                SourceType = sourceType, FieldName = fieldName, IsInsideRepeatingBlock = false
            });
        }
        ctx.SaveChanges();
        return (template.Id, person.Id);
    }

    [Theory]
    [InlineData("{{посада_зв}}")]
    [InlineData("{{звання_дв}}")]
    [InlineData("{{піб_дв}}")]
    [InlineData("{{посада_дв}}")]
    [InlineData("{{приступив}}")]
    [InlineData("{{ознайомлений}}")]
    [InlineData("{{піб_підпис}}")]
    public void CaseTags_AreFilledFromThePersonCard_NotAskedFromTheOfficer(string tag)
    {
        var (sourceType, fieldName) = PlaceholderTagMaps.Classify(tag);

        Assert.Equal(MappingSourceType.Recipient, sourceType);
        Assert.False(string.IsNullOrEmpty(fieldName), $"{tag}: немає поля-джерела");
    }

    [Fact]
    public void MaleOfficer_GetsGenitiveAndDativeFormsOfRankNameAndPosition()
    {
        using var db = new TestDb();
        var (templateId, personId) = Seed(db, Gender.Male);

        var result = TestServices.Generation(db).GenerateTemplatesForRecipients(
            new[] { templateId }, Array.Empty<int>(), new[] { personId }, _folder,
            new Dictionary<string, string>(), NoProgress);

        Assert.Equal(1, result.Generated);
        var text = TextOf(Directory.GetFiles(_folder, "*.docx", SearchOption.AllDirectories).Single());

        Assert.DoesNotContain("{{", text, StringComparison.Ordinal);
        Assert.Contains("капітана БУГАЯ Сергія Михайловича, курсового офіцера курсів перепідготовки та підвищення кваліфікації", text, StringComparison.Ordinal);
        Assert.Contains("капітану БУГАЮ Сергію Михайловичу, курсовому офіцеру курсів перепідготовки та підвищення кваліфікації", text, StringComparison.Ordinal);
        Assert.Contains("що приступив до виконання", text, StringComparison.Ordinal);
        Assert.Contains("З правилами ознайомлений.", text, StringComparison.Ordinal);
        Assert.Contains("Підпис: капітан Сергій БУГАЙ.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FemaleOfficer_GetsFeminineFormsOfNameAndVerb()
    {
        using var db = new TestDb();
        var (templateId, personId) = Seed(db, Gender.Female);

        TestServices.Generation(db).GenerateTemplatesForRecipients(
            new[] { templateId }, Array.Empty<int>(), new[] { personId }, _folder,
            new Dictionary<string, string>(), NoProgress);

        var text = TextOf(Directory.GetFiles(_folder, "*.docx", SearchOption.AllDirectories).Single());

        Assert.DoesNotContain("{{", text, StringComparison.Ordinal);
        Assert.Contains("капітану КОВАЛЬСЬКІЙ Олені Іванівні", text, StringComparison.Ordinal);
        Assert.Contains("що приступила до виконання", text, StringComparison.Ordinal);
        Assert.Contains("З правилами ознайомлена.", text, StringComparison.Ordinal);
        Assert.Contains("Підпис: капітан Олена КОВАЛЬСЬКА.", text, StringComparison.Ordinal);
    }
}
