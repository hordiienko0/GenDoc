using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Documents;
using GenDoc.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Tests.Completeness;

/// <summary>
/// Пастка забутого Include: хеш «застарілості» рахується на сутності Recipient,
/// і якщо в неї не завантажено Weapons, значення мітки зброї виходить порожнім.
/// Записаний у базу SourceHash рахувався В ГЕНЕРАЦІЇ, де Include(Weapons) є
/// (GenerationService.LoadRosterRecipients), тож хеші не збігаються НІКОЛИ:
/// документ із міткою зброї показується застарілим одразу після генерації.
///
/// Гірший наслідок - «Перегенерувати застарілі» в архіві теж вантажив особу без
/// зброї, тобто перезаписував документ ПОРОЖНІМИ полями зброї, після чого хеш
/// сходився і матриця заспокоювалась. Дані в документі при цьому втрачались.
///
/// Це вже третій випадок того самого класу помилки в проєкті (перший -
/// RecipientService.QueryEntities з порожніми колонками зброї в експорті), тому
/// поруч з інтеграційним тестом стоїть сторож на сам хеш.
/// </summary>
public class WeaponIncludeStalenessTests
{
    private const string WeaponTag = "{{зброя}}";

    private static (int PackageId, int IntakeId, int PersonId, int TemplateId) Seed(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
        ctx.AppSettings.Add(new AppSettings { DetectStaleDocuments = true });

        var intake = new Intake { Number = 1, DisplayNumber = "Набір №1" };
        ctx.Intakes.Add(intake);

        var person = TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас");
        ctx.Recipients.Add(person);

        var template = new Template
        {
            Name = "Акт зі зброєю", OriginalFileName = "a.docx", Content = new byte[] { 1 },
            UploadedAt = DateTime.Now, Kind = TemplateKind.PerRecipient
        };
        ctx.Templates.Add(template);
        ctx.SaveChanges();

        person.IntakeId = intake.Id;
        ctx.Weapons.Add(new Weapon
        {
            RecipientId = person.Id, Name = "АКМ", SerialNumber = "АБ1234", RawText = "АКМ № АБ1234"
        });
        ctx.TemplateFieldMappings.Add(new TemplateFieldMapping
        {
            TemplateId = template.Id, PlaceholderTag = WeaponTag,
            SourceType = MappingSourceType.Recipient, FieldName = "WeaponFull"
        });

        var package = new GenerationPackage { Name = "П" };
        package.Templates.Add(new GenerationPackageTemplate { TemplateId = template.Id, SortOrder = 0 });
        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();

        return (package.Id, intake.Id, person.Id, template.Id);
    }

    // Сторож: хеш МУСИТЬ залежати від зброї. Якщо колись мітку зброї приберуть з
    // розрахунку, цей тест впаде й пояснить, чому «застаріле» перестало ловитись.
    [Fact]
    public void ComputeSourceHash_DependsOnLoadedWeapons()
    {
        var mappings = new List<TemplateFieldMapping>
        {
            new() { PlaceholderTag = WeaponTag, SourceType = MappingSourceType.Recipient, FieldName = "WeaponFull" }
        };
        var withWeapon = TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас");
        withWeapon.Weapons.Add(new Weapon { Name = "АКМ", SerialNumber = "АБ1234" });
        var withoutWeapon = TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас");

        var service = new DocumentHashService();

        Assert.NotEqual(
            service.ComputeSourceHash(mappings, withWeapon, orgSettings: null),
            service.ComputeSourceHash(mappings, withoutWeapon, orgSettings: null));
    }

    // Документ, щойно згенерований для особи зі зброєю, НЕ застарілий у матриці.
    [Fact]
    public async Task Matrix_FreshDocumentForPersonWithWeapon_IsNotStale()
    {
        using var db = new TestDb();
        var (packageId, intakeId, personId, templateId) = Seed(db);

        // SourceHash пишемо так само, як його пише генерація: на сутності з Weapons.
        using (var ctx = db.Factory.CreateDbContext())
        {
            var person = ctx.Recipients
                .Include(r => r.Weapons)
                .First(r => r.Id == personId);
            var mappings = ctx.TemplateFieldMappings.Where(m => m.TemplateId == templateId).ToList();

            ctx.GeneratedDocuments.Add(new GeneratedDocument
            {
                RecipientId = personId, TemplateId = templateId, IntakeId = intakeId,
                FileName = "a.docx", GeneratedAt = DateTime.Now, GeneratedByUserId = 1,
                Version = 1, IsCurrent = true, HasContent = true,
                SourceHash = new DocumentHashService().ComputeSourceHash(mappings, person, orgSettings: null)
            });
            ctx.SaveChanges();
        }

        var data = await TestServices.Completeness(db).BuildAsync(intakeId, packageId);

        Assert.False(data.Docs[(personId, templateId)].IsStale,
            "Документ із міткою зброї застарів одразу після генерації - у комплектності забутий Include(Weapons).");
    }

    // Той самий пропуск у картці особи: вкладка «Документи» показувала «застарів».
    [Fact]
    public async Task RecipientCard_FreshDocumentForPersonWithWeapon_IsNotStale()
    {
        using var db = new TestDb();
        var (packageId, intakeId, personId, templateId) = Seed(db);

        using (var ctx = db.Factory.CreateDbContext())
        {
            var person = ctx.Recipients.Include(r => r.Weapons).First(r => r.Id == personId);
            var mappings = ctx.TemplateFieldMappings.Where(m => m.TemplateId == templateId).ToList();
            ctx.GeneratedDocuments.Add(new GeneratedDocument
            {
                RecipientId = personId, TemplateId = templateId, IntakeId = intakeId,
                FileName = "a.docx", GeneratedAt = DateTime.Now, GeneratedByUserId = 1,
                Version = 1, IsCurrent = true, HasContent = true,
                SourceHash = new DocumentHashService().ComputeSourceHash(mappings, person, orgSettings: null)
            });
            ctx.SaveChanges();
        }

        var statuses = await TestServices.Completeness(db).GetRecipientStatusAsync(personId, packageId);

        Assert.False(Assert.Single(statuses).IsStale);
    }
}
