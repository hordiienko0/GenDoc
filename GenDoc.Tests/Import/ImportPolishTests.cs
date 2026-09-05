using System.Windows;
using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Services.Import;
using GenDoc.Services.Intakes;
using GenDoc.Services.OrgTree;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Import;
using GenDoc.ViewModels.Personnel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.Tests.Import;

public class ImportPolishTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    private string TempFile(byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");
        File.WriteAllBytes(path, content);
        _tempFiles.Add(path);
        return path;
    }

    private sealed class NoCounts : ICountService
    {
        public Task<Dictionary<int, int>> GetTreeCountsAsync() => Task.FromResult(new Dictionary<int, int>());
    }

    private sealed class NoDialogs : IDialogService
    {
        public bool? ShowDialog<TViewModel>(TViewModel viewModel, Window? owner = null) where TViewModel : notnull => null;
    }

    private static ImportViewModel BuildViewModel(TestDb db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db.Factory);
        services.AddSingleton<IAuditLogService>(new FakeAuditLog());
        services.AddSingleton<ICurrentUserContext>(new FakeCurrentUser());
        services.AddSingleton<IUserSettingsService, UserSettingsService>();
        services.AddSingleton<GenDoc.Services.Completeness.ICompletenessService>(
            _ => TestServices.Completeness(db));
        services.AddSingleton<IIntakeService, IntakeService>();
        services.AddSingleton<IOrgTreeService, OrgTreeService>();
        services.AddSingleton<ICountService, NoCounts>();
        services.AddSingleton<IDialogService, NoDialogs>();
        services.AddSingleton<ActiveIntakeState>();
        services.AddSingleton<OrgTreeViewModel>();
        services.AddTransient<IntakeWizardViewModel>();
        var provider = services.BuildServiceProvider();

        return new ImportViewModel(
            new ImportService(db.Factory, new FakeAuditLog()),
            provider.GetRequiredService<IIntakeService>(),
            provider.GetRequiredService<IOrgTreeService>(),
            provider.GetRequiredService<IDialogService>(),
            provider);
    }

    private static ImportParseResult FileWith(params (string FullName, string ServiceNumber)[] people)
    {
        var parsed = new ImportParseResult
        {
            FilePath = "test.xlsx",
            Columns =
            {
                new ImportColumn(0, "ПІБ", "") { MappedField = ImportTargetField.FullName },
                new ImportColumn(1, "Особовий номер", "") { MappedField = ImportTargetField.ServiceNumber }
            }
        };

        foreach (var (fullName, serviceNumber) in people)
            parsed.RawRows.Add(new string?[] { fullName, serviceNumber });

        parsed.TotalRows = parsed.RawRows.Count;
        return parsed;
    }

    [Fact]
    public void LoadFile_LockedByExcel_AsksToCloseTheFile()
    {
        using var db = new TestDb();
        var vm = BuildViewModel(db);
        var path = TempFile(new byte[] { 0x50, 0x4B, 0x03, 0x04 });
        using var lockHandle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        vm.LoadFile(path);

        Assert.False(vm.HasFile);
        Assert.Equal(1, vm.CurrentStep);
        Assert.True(vm.HasFileError);
        Assert.Contains("Закрийте файл в Excel і спробуйте ще раз", vm.FileError);
        Assert.Contains(Path.GetFileName(path), vm.FileError);
    }

    [Fact]
    public void LoadFile_MissingFile_SaysItWasNotFound()
    {
        using var db = new TestDb();
        var vm = BuildViewModel(db);
        var missing = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");

        vm.LoadFile(missing);

        Assert.False(vm.HasFile);
        Assert.Contains("не знайдено", vm.FileError);
        Assert.DoesNotContain("Закрийте файл", vm.FileError);
    }

    [Fact]
    public void LoadFile_NotAWorkbook_ExplainsHowToResave()
    {
        using var db = new TestDb();
        var vm = BuildViewModel(db);
        var path = TempFile(new byte[] { 0x01, 0x02, 0x03 });

        vm.LoadFile(path);

        Assert.False(vm.HasFile);
        Assert.Contains("не є книгою Excel", vm.FileError);
        Assert.Contains("збережіть як .xlsx", vm.FileError);
    }

    [Fact]
    public void Preview_ShowsEveryRowThatNeedsAttention_AndOnlyASampleOfGoodOnes()
    {
        var rows = Enumerable.Range(1, 30).Select(i => new ImportRowPreview
        {
            RowNumber = i + 1,
            Status = i % 3 == 0 ? ImportRowStatus.Duplicate : i % 7 == 0 ? ImportRowStatus.Error : ImportRowStatus.Ok
        }).ToList();

        var shown = ImportViewModel.PreviewRows(rows).ToList();

        var attention = rows.Where(r => r.Status is ImportRowStatus.Error or ImportRowStatus.Duplicate).ToList();
        Assert.All(attention, row => Assert.Contains(row, shown));
        Assert.Equal(attention.Count + ImportViewModel.PreviewSampleSize, shown.Count);
        Assert.Equal(shown.Select(r => r.RowNumber).OrderBy(n => n), shown.Select(r => r.RowNumber));
    }

    [Fact]
    public void Preview_WithoutIssues_KeepsTheShortSample()
    {
        var rows = Enumerable.Range(1, 12)
            .Select(i => new ImportRowPreview { RowNumber = i + 1, Status = ImportRowStatus.Ok })
            .ToList();

        Assert.Equal(ImportViewModel.PreviewSampleSize, ImportViewModel.PreviewRows(rows).Count());
    }

    [Fact]
    public void Load_PreviewHeaderPromisesAllProblemRows()
    {
        using var db = new TestDb();
        var vm = BuildViewModel(db);
        var people = Enumerable.Range(1, 20)
            .Select(i => ($"Особа{i} Тест Тестович", i <= 12 ? "СН0001" : $"СН{i:0000}"))
            .ToArray();

        vm.Load(FileWith(people), "список.xlsx");

        Assert.Equal(11, vm.IssueCount);
        Assert.Equal(11 + ImportViewModel.PreviewSampleSize, vm.Preview.Count);
        Assert.Equal(11, vm.Preview.Count(r => r.Status == ImportRowStatus.Duplicate));
        Assert.Contains("показано всі 11 рядків, що потребують уваги", vm.PreviewHeaderText);
        Assert.Contains("причини вказані вище", vm.SkippedNoteText);
    }

    private static int SeedTrashedPerson(TestDb db, string serviceNumber)
    {
        using var ctx = db.Factory.CreateDbContext();
        var intake = new Intake { Number = 14, DisplayNumber = "Набір №14" };
        ctx.Intakes.Add(intake);
        ctx.SaveChanges();

        var person = new Recipient
        {
            LastName = "Ковальчук", FirstName = "Василь", MiddleName = "Богданович",
            Rank = "солдат", ServiceNumber = serviceNumber, IntakeId = intake.Id,
            DeletedAt = DateTime.Now
        };
        ctx.Recipients.Add(person);
        ctx.SaveChanges();
        return person.Id;
    }

    [Fact]
    public void Validate_PersonInTrash_IsADuplicateThatPointsToTheTrash()
    {
        using var db = new TestDb();
        SeedTrashedPerson(db, "СН0001");
        var service = new ImportService(db.Factory, new FakeAuditLog());

        var row = Assert.Single(service.Validate(FileWith(("Ковальчук Василь Богданович", "СН0001"))));

        Assert.Equal(ImportRowStatus.Duplicate, row.Status);
        Assert.Null(row.ExistingRecipientId);
        Assert.Contains("в кошику", row.Note);
        Assert.Contains("«Кошик»", row.Note);
    }

    [Fact]
    public void Import_PersonInTrash_DoesNotCreateASecondCardWithTheSameNumber()
    {
        using var db = new TestDb();
        SeedTrashedPerson(db, "СН0001");
        var service = new ImportService(db.Factory, new FakeAuditLog());

        var summary = service.Import(FileWith(("Ковальчук Василь Богданович", "СН0001"), ("Петренко Іван Іванович", "СН0002")));

        Assert.Equal(1, summary.Imported);
        Assert.Equal(1, summary.Skipped);
        using var ctx = db.Factory.CreateDbContext();
        Assert.Single(ctx.Recipients.IgnoreQueryFilters().Where(r => r.ServiceNumber == "СН0001").ToList());
    }

    [Fact]
    public void Validate_LivePersonWinsOverATrashedNamesake()
    {
        using var db = new TestDb();
        SeedTrashedPerson(db, "СН0001");
        int liveId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var live = new Recipient { LastName = "Живий", FirstName = "Петро", ServiceNumber = "СН0001" };
            ctx.Recipients.Add(live);
            ctx.SaveChanges();
            liveId = live.Id;
        }
        var service = new ImportService(db.Factory, new FakeAuditLog());

        var row = Assert.Single(service.Validate(FileWith(("Живий Петро", "СН0001"))));

        Assert.Equal(liveId, row.ExistingRecipientId);
        Assert.DoesNotContain("кошику", row.Note);
    }
}
