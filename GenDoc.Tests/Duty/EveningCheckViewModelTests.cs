using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Services.Duty;
using GenDoc.Services.Intakes;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Duty;

namespace GenDoc.Tests.Duty;

public class EveningCheckViewModelTests
{
    private sealed class FixedIntake : IIntakeService
    {
        private readonly Intake? _active;
        public FixedIntake(Intake? active) => _active = active;
        public Task<Intake?> GetActiveAsync() => Task.FromResult(_active);
        public Task<Intake?> GetByIdAsync(int id) => throw new NotSupportedException();
        public Task<int> GetNextNumberAsync() => throw new NotSupportedException();
        public Task<string> GetNumberTemplateAsync() => throw new NotSupportedException();
        public Task<Intake> CreateAsync(IntakeCreateRequest request) => throw new NotSupportedException();
        public Task<IReadOnlyList<IntakeOverview>> GetOverviewsAsync() => throw new NotSupportedException();
        public Task<IReadOnlyList<int>> GetYearsAsync() => throw new NotSupportedException();
        public Task<IntakeCloseInfo> GetCloseInfoAsync(int intakeId) => throw new NotSupportedException();
        public Task CloseAsync(IntakeCloseRequest request) => throw new NotSupportedException();
        public Task<string?> GetReopenConflictAsync(int intakeId) => throw new NotSupportedException();
        public Task ReopenAsync(int intakeId) => throw new NotSupportedException();
        public Task<IntakeUpdateResult> UpdateAsync(IntakeUpdateRequest request) => throw new NotSupportedException();
        public Task<int?> GetBaseNodeIdAsync(int intakeId) => throw new NotSupportedException();
    }

    private static Intake ActiveIntake(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        return ctx.Intakes.Single();
    }

    private static async Task<EveningCheckViewModel> Initialized(TestDb db, Intake? active)
    {
        var state = new ActiveIntakeState(new FixedIntake(active));
        await state.RefreshAsync();
        var vm = new EveningCheckViewModel(EveningCheckServiceTests.Service(db), state);
        await vm.InitializeCommand.ExecuteAsync(null);
        await vm.PendingWork;
        return vm;
    }

    private static DutyFolderViewModel Folder(EveningCheckGroupViewModel group, int id)
        => group.Folders.SelectMany(f => f.SelfAndDescendants()).Single(f => f.Id == id);

    [Fact]
    public async Task OpensWithTwoMonthsTheIntakeRootAndItsSecondedFolder()
    {
        using var db = new TestDb();
        var seeded = EveningCheckServiceTests.Seed(db);
        var vm = await Initialized(db, ActiveIntake(db));

        var today = DateTime.Today;
        Assert.Equal(today, vm.DateFrom);
        Assert.Equal(today.AddMonths(2).AddDays(-1), vm.DateTo);
        Assert.Null(vm.PeriodError);
        Assert.Contains("2 сторінки дат (", vm.PeriodText);

        Assert.Equal(EveningCheckText.CourseTitle, vm.Course.Title);
        Assert.Equal(EveningCheckText.SecondedTitle, vm.Seconded.Title);
        Assert.Equal(new[] { seeded.RootId }, vm.Course.PickedFolderIds);
        Assert.Equal(new[] { seeded.SecondedId }, vm.Seconded.PickedFolderIds);
        Assert.True(Folder(vm.Course, seeded.SecondedId).IsCovered);

        // Набір бере всіх своїх, крім тих, хто в папці прикомандированих
        Assert.Equal(new[] { "УСС Олег Геннадійович", "ГОРДІЄНКО Микола Геннадійович", "СОЛОМЕНЦЕВ Дмитро Олександрович" },
            vm.Course.People.Select(p => p.FullName));
        Assert.Equal(3, vm.Course.TakenByOtherBlock);
        Assert.Contains("без 3 осіб з блоку «Прикомандировані»", vm.Course.Summary);
        Assert.Equal(new[] { "БУРЛА Володимир Миколайович", "ОКОМАНЮК Володимир Валентинович", "БОРИСЮК Андрій Володимирович" },
            vm.Seconded.People.Select(p => p.FullName));
        Assert.All(vm.Seconded.People, p => Assert.True(p.IsChecked));
        Assert.Equal("Начальник курсів П та ПК:  підполковник                     Д. ВЕРЕМЕЄНКО", vm.Signature);
        Assert.StartsWith("6 осіб з 1 папки", vm.Course.FolderSummary);
    }

    [Fact]
    public async Task WithoutAnActiveIntakeTheNewestRootIsPreselected()
    {
        using var db = new TestDb();
        var seeded = EveningCheckServiceTests.Seed(db);
        var vm = await Initialized(db, null);

        Assert.Equal(new[] { seeded.RootId }, vm.Course.PickedFolderIds);
        Assert.Equal(new[] { seeded.SecondedId }, vm.Seconded.PickedFolderIds);
    }

    [Fact]
    public void SecondedFolderIsFoundInsideTheIntakeBeforeOthers()
    {
        var folders = new List<DutyFolderOption>
        {
            new(1, null, "Командировані", null, false, Array.Empty<int>()),
            new(2, null, "Набір №30", 30, true, Array.Empty<int>()),
            new(3, 2, "1 група", 30, false, Array.Empty<int>()),
            new(4, 3, "Прикомандировані", 30, false, Array.Empty<int>()),
            new(5, null, "Набір №29", 29, true, Array.Empty<int>()),
            new(6, 5, "Прикомандировані", 29, false, Array.Empty<int>())
        };

        Assert.Equal(new[] { 4 }, EveningCheckViewModel.DefaultSecondedFolders(folders, 2));
        Assert.Equal(new[] { 6 }, EveningCheckViewModel.DefaultSecondedFolders(folders, 5));
        Assert.Equal(new[] { 1 }, EveningCheckViewModel.DefaultSecondedFolders(folders, null));
        Assert.Empty(EveningCheckViewModel.DefaultSecondedFolders(folders.Where(f => f.Id is 2 or 3).ToList(), 2));
    }

    [Fact]
    public async Task ExcludingTheSecondedBlockPutsItsPeopleBackIntoTheIntake()
    {
        using var db = new TestDb();
        EveningCheckServiceTests.Seed(db);
        var vm = await Initialized(db, ActiveIntake(db));

        vm.Seconded.IsIncluded = false;
        await vm.PendingWork;

        Assert.Equal(6, vm.Course.People.Count);
        Assert.Equal(0, vm.Course.TakenByOtherBlock);
        var data = vm.BuildData()!;
        Assert.Single(data.Groups);
        Assert.Equal(6, data.Groups[0].People.Count);

        vm.Seconded.IsIncluded = true;
        await vm.PendingWork;
        Assert.Equal(3, vm.Course.People.Count);
    }

    [Fact]
    public async Task RetickingFoldersReloadsPeopleAndKeepsUnchecked()
    {
        using var db = new TestDb();
        var seeded = EveningCheckServiceTests.Seed(db);
        var vm = await Initialized(db, ActiveIntake(db));

        vm.Seconded.People[0].IsChecked = false;
        Assert.Equal(new[] { "", "1", "2" }, vm.Seconded.People.Select(p => p.NumberText));

        Folder(vm.Seconded, seeded.SecondedId).IsTicked = false;
        await vm.PendingWork;
        Assert.Empty(vm.Seconded.People);
        Assert.Equal(6, vm.Course.People.Count);
        Assert.StartsWith("Папок не відмічено", vm.Seconded.FolderSummary);

        Folder(vm.Seconded, seeded.SecondedId).IsTicked = true;
        await vm.PendingWork;
        Assert.Equal(3, vm.Seconded.People.Count);
        Assert.False(vm.Seconded.People[0].IsChecked);
        Assert.Equal(3, vm.Course.People.Count);

        var data = vm.BuildData()!;
        Assert.Equal(2, data.Groups.Single(g => g.Title == EveningCheckText.SecondedTitle).People.Count);
    }

    [Fact]
    public async Task SecondedSplitByFitnessPutsLimitedFirstAndRestartsNumbering()
    {
        using var db = new TestDb();
        var seeded = EveningCheckServiceTests.Seed(db);
        using (var ctx = db.Factory.CreateDbContext())
        {
            var borysiuk = ctx.Recipients.Single(r => r.LastName == "Борисюк");
            borysiuk.FitnessCategory = FitnessCategoryHelper.Limited;
            ctx.SaveChanges();
        }
        var vm = await Initialized(db, ActiveIntake(db));

        Assert.True(vm.Seconded.CanSplitByFitness);
        Assert.True(vm.Seconded.SplitByFitness);
        Assert.False(vm.Course.CanSplitByFitness);
        Assert.False(vm.Course.SplitByFitness);

        Assert.Equal(new[] { "БОРИСЮК Андрій Володимирович", "БУРЛА Володимир Миколайович", "ОКОМАНЮК Володимир Валентинович" },
            vm.Seconded.People.Select(p => p.FullName));
        Assert.Equal(new[] { "1", "1", "2" }, vm.Seconded.People.Select(p => p.NumberText));
        Assert.Equal(FitnessCategoryHelper.Limited, vm.Seconded.People[0].FitnessMark);
        Assert.Contains("дві таблиці: 1 обмежено придатний і 2 придатних · 2 блоки по 25 рядків", vm.Seconded.Summary);
        Assert.True(vm.Seconded.ToGroup().SplitByFitness);
        Assert.Equal(2, EveningCheckXlsxExporter.BlocksOf(vm.Seconded.ToGroup()));

        vm.Seconded.People[1].IsChecked = false;
        vm.Seconded.SplitByFitness = false;
        Assert.Equal(new[] { "БУРЛА Володимир Миколайович", "ОКОМАНЮК Володимир Валентинович", "БОРИСЮК Андрій Володимирович" },
            vm.Seconded.People.Select(p => p.FullName));
        Assert.Equal(new[] { "", "1", "2" }, vm.Seconded.People.Select(p => p.NumberText));
        Assert.False(vm.Seconded.ToGroup().SplitByFitness);
        Assert.DoesNotContain("дві таблиці", vm.Seconded.Summary);
        Assert.Equal(1, EveningCheckXlsxExporter.BlocksOf(vm.Seconded.ToGroup()));
    }

    [Fact]
    public async Task CheckAllAndUncheckAllRenumber()
    {
        using var db = new TestDb();
        EveningCheckServiceTests.Seed(db);
        var vm = await Initialized(db, ActiveIntake(db));

        vm.Seconded.UncheckAllCommand.Execute(null);
        Assert.Equal(0, vm.Seconded.CheckedCount);
        Assert.StartsWith("0 з 3 осіб", vm.Seconded.Summary);

        vm.Seconded.CheckAllCommand.Execute(null);
        Assert.Equal(3, vm.Seconded.CheckedCount);
        Assert.Equal("3", vm.Seconded.People[2].NumberText);
    }

    [Fact]
    public async Task TitlesAndSignatureAreEditable()
    {
        using var db = new TestDb();
        EveningCheckServiceTests.Seed(db);
        var vm = await Initialized(db, ActiveIntake(db));

        vm.Course.IsIncluded = false;
        await vm.PendingWork;
        vm.Seconded.Title = "ПРИКОМАНДИРОВАНІ 30 НАБОРУ";
        vm.Signature = "  Начальник курсів П та ПК:  майор  І. ПЕТРЕНКО  ";

        var data = vm.BuildData()!;
        Assert.Single(data.Groups);
        Assert.Equal("ПРИКОМАНДИРОВАНІ 30 НАБОРУ", data.Groups[0].Title);
        Assert.Equal("Начальник курсів П та ПК:  майор  І. ПЕТРЕНКО", data.Signature);
        Assert.Contains("Разом", vm.TotalText);

        vm.Seconded.IsIncluded = false;
        await vm.PendingWork;
        Assert.Equal("Відмітьте хоча б один блок.", vm.TotalText);
    }

    [Fact]
    public async Task BadPeriodIsReportedAndBlocksTheData()
    {
        using var db = new TestDb();
        EveningCheckServiceTests.Seed(db);
        var vm = await Initialized(db, ActiveIntake(db));

        vm.DateTo = vm.DateFrom!.Value.AddDays(-1);
        Assert.Equal("Дата «по» раніша за дату «з».", vm.PeriodError);
        Assert.Null(vm.BuildData());

        vm.DateTo = vm.DateFrom!.Value.AddDays(EveningCheckXlsxExporter.MaxDays);
        Assert.Contains("довший", vm.PeriodError);

        vm.DateTo = null;
        Assert.Equal("Вкажіть дати «з» і «по».", vm.PeriodError);

        vm.ResetPeriodCommand.Execute(null);
        Assert.Null(vm.PeriodError);
        Assert.NotNull(vm.BuildData());
    }

    [Fact]
    public async Task DataRendersToAWorkbookWithTheChosenPeople()
    {
        using var db = new TestDb();
        EveningCheckServiceTests.Seed(db);
        var vm = await Initialized(db, ActiveIntake(db));
        vm.DateFrom = new DateTime(2026, 9, 15);
        vm.DateTo = new DateTime(2026, 10, 15);

        using var workbook = EveningCheckXlsxExporter.Build(vm.BuildData()!);
        var sheet = workbook.Worksheet(EveningCheckXlsxExporter.SheetName);

        Assert.Equal("УСС Олег Геннадійович", sheet.Cell(4, 3).GetString());
        Assert.Equal("старший лейтенант", sheet.Cell(4, 2).GetString());
        var seconded = sheet.Column(1).CellsUsed(c => c.GetString() == EveningCheckText.SecondedTitle).Single();
        Assert.Equal("БУРЛА Володимир Миколайович", sheet.Cell(seconded.Address.RowNumber + 3, 3).GetString());
        Assert.Single(sheet.Column(1).CellsUsed(c => c.GetString() == EveningCheckText.SummaryTitle));
    }
}
