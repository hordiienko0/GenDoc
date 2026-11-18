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

/// <summary>
/// Імпорт перевіряє, чи людина вже є в особовому складі (у будь-якому наборі чи в постійному складі),
/// показує, де саме, і чим файл відрізняється від картки; дублів через Excel бути не повинно.
/// </summary>
public class ImportExistingPersonTests
{
    private static ImportService Build(TestDb db) => new(db.Factory, new FakeAuditLog());

    private static ImportParseResult File(string[] headers, params string?[][] rows)
    {
        var parsed = new ImportParseResult { FilePath = "список.xlsx" };
        for (var i = 0; i < headers.Length; i++)
        {
            var noteAssigned = false;
            parsed.Columns.Add(new ImportColumn(i, headers[i], "")
            {
                MappedField = ImportService.AutoMapHeader(headers[i], ref noteAssigned)
            });
        }

        foreach (var row in rows) parsed.RawRows.Add(row);
        parsed.TotalRows = parsed.RawRows.Count;
        return parsed;
    }

    private sealed record Seeded(int OldIntakeId, int NewIntakeId, int PersonId, int FitFolderId);

    /// <summary>Набір №14 з папками «Всі / Придатні» і людина в ньому; набір №15 - куди імпортуємо.</summary>
    private static Seeded Seed(TestDb db, Action<Recipient>? configure = null)
    {
        using var ctx = db.Factory.CreateDbContext();

        var root = new OrgNode { Name = "Курси", Depth = 0 };
        ctx.OrgNodes.Add(root);
        ctx.SaveChanges();
        root.Path = $"/{root.Id}/";

        var oldIntake = new Intake { Number = 14, DisplayNumber = "Набір №14" };
        var newIntake = new Intake { Number = 15, DisplayNumber = "Набір №15" };
        ctx.Intakes.AddRange(oldIntake, newIntake);
        ctx.SaveChanges();

        var intakeRoot = new OrgNode { Name = "Набір №14", ParentId = root.Id, Depth = 1, IntakeId = oldIntake.Id };
        ctx.OrgNodes.Add(intakeRoot);
        ctx.SaveChanges();
        var all = new OrgNode { Name = "Всі", ParentId = intakeRoot.Id, Depth = 2, IntakeId = oldIntake.Id };
        ctx.OrgNodes.Add(all);
        ctx.SaveChanges();
        var fit = new OrgNode { Name = "Придатні", ParentId = all.Id, Depth = 3, IntakeId = oldIntake.Id };
        ctx.OrgNodes.Add(fit);
        ctx.SaveChanges();

        var person = new Recipient
        {
            LastName = "Ковальчук", FirstName = "Василь", MiddleName = "Богданович",
            Rank = "солдат", Phone = "067 123-45-67",
            DateOfBirth = new DateOnly(1990, 5, 1),
            IntakeId = oldIntake.Id, OrgNodeId = fit.Id
        };
        configure?.Invoke(person);
        ctx.Recipients.Add(person);
        ctx.SaveChanges();

        return new Seeded(oldIntake.Id, newIntake.Id, person.Id, fit.Id);
    }

    [Fact]
    public void Validate_PersonFromAnotherIntake_WithoutServiceNumber_IsFoundAndShowsWhereHeIs()
    {
        using var db = new TestDb();
        var seeded = Seed(db);

        var row = Assert.Single(Build(db).Validate(
            File(new[] { "ПІБ", "Дата народження", "Звання" },
                new[] { "Ковальчук Василь Богданович", "01.05.1990", "солдат" }),
            new ImportTarget(ImportTargetKind.Intake, seeded.NewIntakeId)));

        Assert.Equal(ImportRowStatus.Duplicate, row.Status);
        Assert.Equal(seeded.PersonId, row.ExistingRecipientId);
        Assert.Equal("Набір №14 / Всі / Придатні", row.ExistingLocation);
        Assert.Contains("Набір №14 / Всі / Придатні", row.Note);
        Assert.Contains("Дані збігаються", row.Note);
        Assert.Empty(row.Differences);
        Assert.False(row.CanUpdate);
        Assert.True(row.CanMove);
        Assert.False(row.CanAddAsNew);
    }

    [Fact]
    public void Import_PersonFromAnotherIntake_IsNotAddedASecondTime()
    {
        using var db = new TestDb();
        var seeded = Seed(db);

        var summary = Build(db).Import(
            File(new[] { "ПІБ", "Дата народження" }, new[] { "Ковальчук Василь Богданович", "01.05.1990" }),
            new ImportTarget(ImportTargetKind.Intake, seeded.NewIntakeId));

        Assert.Equal(0, summary.Imported);
        Assert.Equal(1, summary.Skipped);
        using var ctx = db.Factory.CreateDbContext();
        Assert.Single(ctx.Recipients.ToList());
    }

    [Fact]
    public void Validate_FileHasServiceNumberTheCardLacks_StillMatchesByName()
    {
        using var db = new TestDb();
        var seeded = Seed(db);

        var row = Assert.Single(Build(db).Validate(
            File(new[] { "ПІБ", "Особовий номер", "Дата народження" },
                new[] { "Ковальчук Василь Богданович", "СН0001", "01.05.1990" }),
            new ImportTarget(ImportTargetKind.Intake, seeded.NewIntakeId)));

        Assert.Equal(seeded.PersonId, row.ExistingRecipientId);
        var difference = Assert.Single(row.Differences);
        Assert.Equal("Особовий номер", difference.Field);
        Assert.Equal("СН0001", difference.Incoming);
        Assert.True(row.CanUpdate);
    }

    [Fact]
    public void Validate_DifferentData_ListsEveryDifferenceButIgnoresCaseSpacesAndEmptyCells()
    {
        using var db = new TestDb();
        var seeded = Seed(db);

        var row = Assert.Single(Build(db).Validate(
            File(new[] { "ПІБ", "Дата народження", "Звання", "Телефон", "Посада" },
                new[] { "КОВАЛЬЧУК Василь Богданович", "01.05.1990", "сержант", "0671234567", null }),
            new ImportTarget(ImportTargetKind.Intake, seeded.NewIntakeId)));

        var difference = Assert.Single(row.Differences);
        Assert.Equal("Звання", difference.Field);
        Assert.Equal("солдат", difference.Current);
        Assert.Equal("сержант", difference.Incoming);
        Assert.Equal("Звання: солдат → сержант", difference.Display);
        Assert.Contains("можна актуалізувати", row.Note);
    }

    [Fact]
    public void Import_Update_ChangesTheCardInPlaceAndKeepsItsLocation()
    {
        using var db = new TestDb();
        var seeded = Seed(db);
        var service = Build(db);
        var target = new ImportTarget(ImportTargetKind.Intake, seeded.NewIntakeId);
        var parsed = File(new[] { "ПІБ", "Дата народження", "Звання", "Телефон" },
            new[] { "Ковальчук Василь Богданович", "01.05.1990", "сержант", null });

        var row = Assert.Single(service.Validate(parsed, target));
        var summary = service.Import(parsed, target,
            new Dictionary<int, ImportExistingAction> { [row.RowNumber] = ImportExistingAction.Update });

        Assert.Equal(1, summary.Updated);
        Assert.Equal(0, summary.Imported);
        Assert.Equal(0, summary.Moved);

        using var ctx = db.Factory.CreateDbContext();
        var person = Assert.Single(ctx.Recipients.ToList());
        Assert.Equal("сержант", person.Rank);
        Assert.Equal("067 123-45-67", person.Phone);
        Assert.Equal(seeded.OldIntakeId, person.IntakeId);
        Assert.Equal(seeded.FitFolderId, person.OrgNodeId);
    }

    [Fact]
    public void Validate_SameNameButOtherDateOfBirth_IsADifferentPerson()
    {
        using var db = new TestDb();
        var seeded = Seed(db);

        var row = Assert.Single(Build(db).Validate(
            File(new[] { "ПІБ", "Дата народження" }, new[] { "Ковальчук Василь Богданович", "02.06.1995" }),
            new ImportTarget(ImportTargetKind.Intake, seeded.NewIntakeId)));

        Assert.Null(row.ExistingRecipientId);
        Assert.NotEqual(ImportRowStatus.Duplicate, row.Status);
    }

    [Fact]
    public void Validate_SameNameOtherServiceNumberAndNoDate_IsADifferentPerson()
    {
        using var db = new TestDb();
        var seeded = Seed(db, p =>
        {
            p.ServiceNumber = "СН0001";
            p.DateOfBirth = null;
        });

        var row = Assert.Single(Build(db).Validate(
            File(new[] { "ПІБ", "Особовий номер" }, new[] { "Ковальчук Василь Богданович", "СН0999" }),
            new ImportTarget(ImportTargetKind.Intake, seeded.NewIntakeId)));

        Assert.Null(row.ExistingRecipientId);
    }

    [Fact]
    public void Validate_NameOnlyMatch_CanBeAddedAsANewPerson()
    {
        using var db = new TestDb();
        var seeded = Seed(db, p => p.DateOfBirth = null);
        var service = Build(db);
        var target = new ImportTarget(ImportTargetKind.Intake, seeded.NewIntakeId);
        var parsed = File(new[] { "ПІБ" }, new[] { "Ковальчук Василь Богданович" });

        var row = Assert.Single(service.Validate(parsed, target));
        Assert.Equal(seeded.PersonId, row.ExistingRecipientId);
        Assert.True(row.CanAddAsNew);
        Assert.Contains("Схоже, вже є в базі", row.Note);

        var summary = service.Import(parsed, target,
            new Dictionary<int, ImportExistingAction> { [row.RowNumber] = ImportExistingAction.AddAsNew });

        Assert.Equal(1, summary.Imported);
        using var ctx = db.Factory.CreateDbContext();
        Assert.Equal(2, ctx.Recipients.Count());
    }

    [Fact]
    public void Import_AddAsNewIsIgnoredWhenTheMatchIsCertain()
    {
        using var db = new TestDb();
        var seeded = Seed(db);
        var service = Build(db);
        var target = new ImportTarget(ImportTargetKind.Intake, seeded.NewIntakeId);
        var parsed = File(new[] { "ПІБ", "Дата народження" }, new[] { "Ковальчук Василь Богданович", "01.05.1990" });

        var row = Assert.Single(service.Validate(parsed, target));
        Assert.False(row.CanAddAsNew);

        var summary = service.Import(parsed, target,
            new Dictionary<int, ImportExistingAction> { [row.RowNumber] = ImportExistingAction.AddAsNew });

        Assert.Equal(0, summary.Imported);
        using var ctx = db.Factory.CreateDbContext();
        Assert.Single(ctx.Recipients.ToList());
    }

    [Fact]
    public void Validate_LatinLookalikeLettersInTheName_StillMatch()
    {
        using var db = new TestDb();
        var seeded = Seed(db, p =>
        {
            p.LastName = "Іванов";
            p.FirstName = "Олег";
            p.MiddleName = "Петрович";
        });

        // «I» і «O» - латиниця, як буває у списках, набраних не в тій розкладці.
        var row = Assert.Single(Build(db).Validate(
            File(new[] { "ПІБ", "Дата народження" }, new[] { "Iванов Oлег Петрович", "01.05.1990" }),
            new ImportTarget(ImportTargetKind.Intake, seeded.NewIntakeId)));

        Assert.Equal(seeded.PersonId, row.ExistingRecipientId);
        Assert.Empty(row.Differences);
    }

    [Fact]
    public void Validate_PersonInTheSameIntake_CannotBeMovedThere()
    {
        using var db = new TestDb();
        var seeded = Seed(db);

        var row = Assert.Single(Build(db).Validate(
            File(new[] { "ПІБ", "Дата народження" }, new[] { "Ковальчук Василь Богданович", "01.05.1990" }),
            new ImportTarget(ImportTargetKind.Intake, seeded.OldIntakeId)));

        Assert.Equal(seeded.PersonId, row.ExistingRecipientId);
        Assert.False(row.CanMove);
    }

    [Fact]
    public void Validate_PermanentStaffPerson_IsReportedAsPermanentStaff()
    {
        using var db = new TestDb();
        var seeded = Seed(db, p =>
        {
            p.IntakeId = null;
            p.OrgNodeId = null;
        });

        var row = Assert.Single(Build(db).Validate(
            File(new[] { "ПІБ", "Дата народження" }, new[] { "Ковальчук Василь Богданович", "01.05.1990" }),
            new ImportTarget(ImportTargetKind.Intake, seeded.NewIntakeId)));

        Assert.Equal("Постійний склад", row.ExistingLocation);
        Assert.True(row.CanMove);
    }

    [Fact]
    public void Validate_SamePersonTwiceInTheFile_SecondRowIsAFileDuplicate()
    {
        using var db = new TestDb();
        var seeded = Seed(db);

        var rows = Build(db).Validate(
            File(new[] { "ПІБ", "Дата народження" },
                new[] { "Ковальчук Василь Богданович", "01.05.1990" },
                new[] { "Ковальчук Василь Богданович", null }),
            new ImportTarget(ImportTargetKind.Intake, seeded.NewIntakeId));

        Assert.Equal(seeded.PersonId, rows[0].ExistingRecipientId);
        Assert.Null(rows[1].ExistingRecipientId);
        Assert.Equal(ImportService.InFileDuplicateNote, rows[1].Note);
    }

    // --- вікно імпорту ---

    private static ImportViewModel BuildViewModel(TestDb db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db.Factory);
        services.AddSingleton<IAuditLogService>(new FakeAuditLog());
        services.AddSingleton<ICurrentUserContext>(new FakeCurrentUser());
        services.AddSingleton<IUserSettingsService, UserSettingsService>();
        services.AddSingleton<GenDoc.Services.Completeness.ICompletenessService>(_ => TestServices.Completeness(db));
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

    private sealed class NoCounts : ICountService
    {
        public Task<Dictionary<int, int>> GetTreeCountsAsync() => Task.FromResult(new Dictionary<int, int>());
    }

    private sealed class NoDialogs : IDialogService
    {
        public bool? ShowDialog<TViewModel>(TViewModel viewModel, Window? owner = null) where TViewModel : notnull => null;
    }

    [Fact]
    public void RunImport_PersonWithDifferentData_AsksToUpdate_AndYesUpdatesTheCard()
    {
        using var db = new TestDb();
        Seed(db);
        var vm = BuildViewModel(db);
        var asked = new List<string>();
        vm.ShowMessage = (text, caption, buttons, _) =>
        {
            asked.Add(text);
            return buttons == MessageBoxButton.YesNoCancel ? MessageBoxResult.Yes : MessageBoxResult.OK;
        };

        vm.TargetExistingIntake = false;
        vm.TargetPermanentStaff = true;
        vm.Load(File(new[] { "ПІБ", "Дата народження", "Звання" },
            new[] { "Ковальчук Василь Богданович", "01.05.1990", "сержант" },
            new[] { "Новенький Іван Іванович", "02.02.2000", "солдат" }), "список.xlsx");

        Assert.Equal(1, vm.ExistingCount);
        Assert.Contains("вже є в особовому складі", vm.ExistingSummaryText);
        var existing = Assert.Single(vm.Preview, r => r.IsExisting);
        Assert.Contains("Звання: солдат → сержант", existing.DifferenceLines);
        Assert.Contains(existing.ActionOptions, o => o.Action == ImportExistingAction.Update);

        Assert.True(vm.RunImportCommand.CanExecute(null));
        vm.RunImportCommand.Execute(null);

        Assert.Contains(asked, t => t.Contains("Актуалізувати їхні картки") && t.Contains("Ковальчук Василь Богданович")
                                                                            && t.Contains("Набір №14 / Всі / Придатні"));

        using var ctx = db.Factory.CreateDbContext();
        var people = ctx.Recipients.OrderBy(r => r.Id).ToList();
        Assert.Equal(2, people.Count);
        Assert.Equal("сержант", people[0].Rank);
        Assert.NotNull(people[0].IntakeId);
    }

    [Fact]
    public void RunImport_AnswerNo_LeavesTheCardAsIsButImportsTheNewOnes()
    {
        using var db = new TestDb();
        Seed(db);
        var vm = BuildViewModel(db);
        vm.ShowMessage = (_, _, buttons, _) =>
            buttons == MessageBoxButton.YesNoCancel ? MessageBoxResult.No : MessageBoxResult.OK;

        vm.TargetExistingIntake = false;
        vm.TargetPermanentStaff = true;
        vm.Load(File(new[] { "ПІБ", "Дата народження", "Звання" },
            new[] { "Ковальчук Василь Богданович", "01.05.1990", "сержант" },
            new[] { "Новенький Іван Іванович", "02.02.2000", "солдат" }), "список.xlsx");

        vm.RunImportCommand.Execute(null);

        using var ctx = db.Factory.CreateDbContext();
        var people = ctx.Recipients.OrderBy(r => r.Id).ToList();
        Assert.Equal(2, people.Count);
        Assert.Equal("солдат", people[0].Rank);
    }

    [Fact]
    public void RunImport_AnswerCancel_ChangesNothing()
    {
        using var db = new TestDb();
        Seed(db);
        var vm = BuildViewModel(db);
        vm.ShowMessage = (_, _, _, _) => MessageBoxResult.Cancel;

        vm.TargetExistingIntake = false;
        vm.TargetPermanentStaff = true;
        vm.Load(File(new[] { "ПІБ", "Дата народження", "Звання" },
            new[] { "Ковальчук Василь Богданович", "01.05.1990", "сержант" }), "список.xlsx");

        vm.RunImportCommand.Execute(null);

        using var ctx = db.Factory.CreateDbContext();
        Assert.Equal("солдат", Assert.Single(ctx.Recipients.ToList()).Rank);
        Assert.True(vm.HasFile);
    }

    [Fact]
    public void UpdateAllCheckbox_SetsTheActionOnEveryRowWithDifferences()
    {
        using var db = new TestDb();
        Seed(db);
        var vm = BuildViewModel(db);

        vm.TargetExistingIntake = false;
        vm.TargetPermanentStaff = true;
        vm.Load(File(new[] { "ПІБ", "Дата народження", "Звання" },
            new[] { "Ковальчук Василь Богданович", "01.05.1990", "сержант" }), "список.xlsx");

        Assert.True(vm.HasUpdatableDuplicates);
        vm.UpdateAllDuplicates = true;

        Assert.Equal(ImportExistingAction.Update, Assert.Single(vm.Preview).Action);
        Assert.Contains("актуалізувати 1", vm.ImportButtonText);

        vm.UpdateAllDuplicates = false;
        Assert.Equal(ImportExistingAction.Skip, Assert.Single(vm.Preview).Action);
    }
}
