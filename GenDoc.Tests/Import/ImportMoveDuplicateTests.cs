using GenDoc.Models;
using GenDoc.Services.Import;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Import;

// Колонка «ДІЯ / Перемістити» на кроці 4. Досі дубль означав лише «пропустити
// рядок»: людина, яка вже є в базі, просто не імпортувалась — і перевести її з
// одного набору в інший через імпорт було неможливо взагалі.
//
// Переносити можна не будь-який дубль. «Дублюється в файлі» — це два рядки
// однієї вивантажки, там переносити нема кого й нема куди. Тому ознакою
// служить ExistingRecipientId: він є рівно тоді, коли в базі знайшлась картка.
public class ImportMoveDuplicateTests
{
    private static ImportService Build(TestDb db) => new(db.Factory, new FakeAuditLog());

    private static ImportParseResult FileWith(params (string FullName, string ServiceNumber, string Rank)[] people)
    {
        var parsed = new ImportParseResult
        {
            FilePath = "test.xlsx",
            Columns =
            {
                new ImportColumn(0, "ПІБ", "") { MappedField = ImportTargetField.FullName },
                new ImportColumn(1, "Особовий номер", "") { MappedField = ImportTargetField.ServiceNumber },
                new ImportColumn(2, "Звання", "") { MappedField = ImportTargetField.Rank }
            }
        };

        foreach (var (fullName, serviceNumber, rank) in people)
            parsed.RawRows.Add(new string?[] { fullName, serviceNumber, rank });

        parsed.TotalRows = parsed.RawRows.Count;
        return parsed;
    }

    // Готує базу: набір №14 з однією людиною і порожній набір №15 як ціль.
    private static (int OldIntakeId, int NewIntakeId, int RecipientId) SeedExistingPerson(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();

        var oldIntake = new Intake { Number = 14, DisplayNumber = "Набір №14" };
        var newIntake = new Intake { Number = 15, DisplayNumber = "Набір №15" };
        ctx.Intakes.AddRange(oldIntake, newIntake);
        ctx.SaveChanges();

        var person = new Recipient
        {
            LastName = "Ковальчук", FirstName = "Василь", MiddleName = "Богданович",
            Rank = "солдат", ServiceNumber = "СН0001", IntakeId = oldIntake.Id
        };
        ctx.Recipients.Add(person);
        ctx.SaveChanges();

        return (oldIntake.Id, newIntake.Id, person.Id);
    }

    [Fact]
    public void Validate_DuplicateAlreadyInDatabase_ExposesTheExistingCard()
    {
        using var db = new TestDb();
        var (_, newIntakeId, recipientId) = SeedExistingPerson(db);

        var previews = Build(db).Validate(
            FileWith(("Ковальчук Василь Богданович", "СН0001", "сержант")),
            new ImportTarget(ImportTargetKind.Intake, newIntakeId));

        var row = Assert.Single(previews);
        Assert.Equal(ImportRowStatus.Duplicate, row.Status);
        Assert.Equal(recipientId, row.ExistingRecipientId);
    }

    // Другий бік межі: два однакові рядки в одній вивантажці. Перший імпортується,
    // другий — дубль, але переносити його нікуди, бо картки в базі ще немає.
    [Fact]
    public void Validate_DuplicateWithinTheFile_HasNothingToMove()
    {
        using var db = new TestDb();

        var previews = Build(db).Validate(
            FileWith(
                ("Мельник Петро Іванович", "СН0777", "капітан"),
                ("Мельник Петро Іванович", "СН0777", "капітан")),
            ImportTarget.FromFile);

        Assert.Equal(ImportRowStatus.Duplicate, previews[1].Status);
        Assert.Null(previews[1].ExistingRecipientId);
    }

    // Суть дії: людина переїжджає в цільовий набір, а поля з файлу оновлюють
    // картку. Порожня комірка НЕ затирає наявне значення — скорочена вивантажка
    // на три колонки інакше витерла б посаду, адресу й решту анкети.
    [Fact]
    public void Import_MarkedDuplicate_MovesThePersonAndUpdatesOnlyNonEmptyFields()
    {
        using var db = new TestDb();
        var (oldIntakeId, newIntakeId, recipientId) = SeedExistingPerson(db);

        using (var ctx = db.Factory.CreateDbContext())
        {
            var person = ctx.Recipients.First(r => r.Id == recipientId);
            person.Position = "курсант";
            ctx.SaveChanges();
        }

        // Колонки «Посада» у файлі немає взагалі — саме той випадок, коли
        // затирання було б найпомітнішим.
        var summary = Build(db).Import(
            FileWith(("Ковальчук Василь Богданович", "СН0001", "сержант")),
            new ImportTarget(ImportTargetKind.Intake, newIntakeId),
            moveRowNumbers: new[] { 2 });

        Assert.Equal(1, summary.Moved);
        Assert.Equal(0, summary.Skipped);

        using var read = db.Factory.CreateDbContext();
        var moved = read.Recipients.First(r => r.Id == recipientId);

        Assert.Equal(newIntakeId, moved.IntakeId);
        Assert.NotEqual(oldIntakeId, moved.IntakeId);
        Assert.Equal("сержант", moved.Rank);
        Assert.Equal("курсант", moved.Position);
    }

    // Без позначки поведінка лишається старою — дубль просто пропускається.
    // Інакше «перенести» стало б замовчуванням і мовчки правило б чужі картки.
    [Fact]
    public void Import_UnmarkedDuplicate_StillJustSkips()
    {
        using var db = new TestDb();
        var (oldIntakeId, newIntakeId, recipientId) = SeedExistingPerson(db);

        var summary = Build(db).Import(
            FileWith(("Ковальчук Василь Богданович", "СН0001", "сержант")),
            new ImportTarget(ImportTargetKind.Intake, newIntakeId));

        Assert.Equal(0, summary.Moved);
        Assert.Equal(1, summary.Skipped);

        using var read = db.Factory.CreateDbContext();
        var untouched = read.Recipients.First(r => r.Id == recipientId);

        Assert.Equal(oldIntakeId, untouched.IntakeId);
        Assert.Equal("солдат", untouched.Rank);
    }

    // Постійний склад — поза наборами, тож перенесення туди мусить занулити
    // IntakeId, а не підставити якийсь набір із файлу.
    [Fact]
    public void Import_MoveToPermanentStaff_ClearsTheIntake()
    {
        using var db = new TestDb();
        var (_, _, recipientId) = SeedExistingPerson(db);

        var summary = Build(db).Import(
            FileWith(("Ковальчук Василь Богданович", "СН0001", "капітан")),
            new ImportTarget(ImportTargetKind.PermanentStaff),
            moveRowNumbers: new[] { 2 });

        Assert.Equal(1, summary.Moved);

        using var read = db.Factory.CreateDbContext();
        var moved = read.Recipients.First(r => r.Id == recipientId);

        Assert.Null(moved.IntakeId);
        Assert.Equal("капітан", moved.Rank);
    }
}
