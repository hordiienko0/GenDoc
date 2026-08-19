using GenDoc.Models;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Archive;

// «Запуски» з обраним набором показують запуски цього набору І запуски без набору
// (відомості по постійному складу) - інакше останні не видимі ніде.
public class ArchiveRunsTests
{
    [Fact]
    public async Task GetRuns_WithIntake_IncludesRunsOfThatIntakeAndRunsWithoutIntake()
    {
        using var db = new TestDb();
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
            var package = new GenerationPackage { Name = "П" };
            ctx.GenerationPackages.Add(package);
            ctx.Intakes.AddRange(
                new Intake { Number = 1, DisplayNumber = "Набір №1" },
                new Intake { Number = 2, DisplayNumber = "Набір №2" });
            ctx.SaveChanges();

            ctx.GenerationPackageRuns.AddRange(
                new GenerationPackageRun { GenerationPackage = package, RunAt = DateTime.Now, RunByUserId = 1, IntakeId = 1 },
                new GenerationPackageRun { GenerationPackage = package, RunAt = DateTime.Now, RunByUserId = 1, IntakeId = 2 },
                new GenerationPackageRun { GenerationPackage = package, RunAt = DateTime.Now, RunByUserId = 1, IntakeId = null });
            ctx.SaveChanges();
        }

        var runs = await TestServices.Archive(db).GetRunsAsync(intakeId: 1, year: null);

        Assert.Equal(2, runs.Count);
        Assert.Contains(runs, r => r.IntakeNumber == 1);
        Assert.Contains(runs, r => r.IntakeNumber is null);
        Assert.DoesNotContain(runs, r => r.IntakeNumber == 2);
    }
}
