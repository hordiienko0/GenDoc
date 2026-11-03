using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Duty;
using GenDoc.Services.Intakes;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Duty;

namespace GenDoc.Tests.Duty;

public class DutyRosterViewModelTests
{
    private static readonly DateOnly From = new(2026, 9, 19);

    private sealed class NoActiveIntake : IIntakeService
    {
        public Task<Intake?> GetActiveAsync() => Task.FromResult<Intake?>(null);
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
    }

    private static int SeedIntake(TestDb db, int people)
    {
        using var ctx = db.Factory.CreateDbContext();
        var root = new OrgNode { Name = "Набір №29", Path = "/1/", Depth = 0 };
        ctx.OrgNodes.Add(root);
        ctx.SaveChanges();
        var intake = new Intake
        {
            Number = 29, DisplayNumber = "Набір №29", Status = IntakeStatus.Active, RootOrgNodeId = root.Id,
            DateStart = new DateOnly(2026, 9, 1), DateEnd = new DateOnly(2026, 11, 30)
        };
        ctx.Intakes.Add(intake);
        ctx.SaveChanges();
        ctx.Recipients.AddRange(Enumerable.Range(1, people).Select(i => new Recipient
        {
            LastName = $"Слухач{i:00}", FirstName = "Андрій", MiddleName = "Петрович", Rank = "солдат", IntakeId = intake.Id
        }));
        ctx.SaveChanges();
        return intake.Id;
    }

    private static async Task<(DutyRosterViewModel Vm, DutyRosterService Service)> CreatedRoster(TestDb db, int people = 12, int days = 20)
    {
        SeedIntake(db, people);
        var service = new DutyRosterService(db.Factory, new FakeAuditLog(), new FakeCurrentUser(), TestServices.UserSettings(db, null));
        var vm = new DutyRosterViewModel(service, new ActiveIntakeState(new NoActiveIntake()));

        await vm.InitializeCommand.ExecuteAsync(null);
        await vm.PendingWork;
        vm.NewDateFrom = From.ToDateTime(TimeOnly.MinValue);
        vm.NewDateTo = From.AddDays(days - 1).ToDateTime(TimeOnly.MinValue);
        vm.NewWeekendsGreen = false;
        await vm.CreateCommand.ExecuteAsync(null);
        return (vm, service);
    }

    private static int OnDuty(DutyRosterViewModel vm, int dayIndex) => vm.Rows.Count(r => r.Cells[dayIndex].IsDuty);

    [Fact]
    public async Task WithoutRostersTheCreatePanelOpensWithTheIntakePreselected()
    {
        using var db = new TestDb();
        SeedIntake(db, 3);
        var service = new DutyRosterService(db.Factory, new FakeAuditLog(), new FakeCurrentUser(), TestServices.UserSettings(db, null));
        var vm = new DutyRosterViewModel(service, new ActiveIntakeState(new NoActiveIntake()));

        await vm.InitializeCommand.ExecuteAsync(null);
        await vm.PendingWork;

        Assert.True(vm.IsCreating);
        Assert.False(vm.HasRoster);
        var intake = Assert.Single(vm.Folders);
        Assert.Equal("Набір №29", intake.Name);
        Assert.True(intake.IsChecked);
        Assert.Equal(new[] { intake.Id }, vm.PickedFolderIds);
        Assert.Equal("Разом 3 особи з 1 папки, вкладені папки входять.", vm.NewPeopleSummary);
        Assert.Equal(DateTime.Today, vm.NewDateFrom);
    }

    [Fact]
    public async Task TickingAFolderCoversItsSubfoldersAndTheNextRosterKeepsTheChoice()
    {
        using var db = new TestDb();
        SeedIntake(db, 4);
        int groupId, secondedId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var groupNode = new OrgNode { Name = "1 група", ParentId = 1, Path = "/1/2/", Depth = 1 };
            var secondedNode = new OrgNode { Name = "Командировані", Depth = 0, SortOrder = 1 };
            ctx.OrgNodes.AddRange(groupNode, secondedNode);
            ctx.SaveChanges();
            secondedNode.Path = $"/{secondedNode.Id}/";
            ctx.Recipients.AddRange(Enumerable.Range(1, 2).Select(i => new Recipient
            {
                LastName = $"Командирований{i}", FirstName = "Іван", Rank = "сержант", OrgNodeId = secondedNode.Id
            }));
            ctx.SaveChanges();
            (groupId, secondedId) = (groupNode.Id, secondedNode.Id);
        }
        var service = new DutyRosterService(db.Factory, new FakeAuditLog(), new FakeCurrentUser(), TestServices.UserSettings(db, null));
        var vm = new DutyRosterViewModel(service, new ActiveIntakeState(new NoActiveIntake()));
        await vm.InitializeCommand.ExecuteAsync(null);
        await vm.PendingWork;

        var intake = vm.Folders.Single(f => f.Name == "Набір №29");
        var group = Assert.Single(intake.Children);
        var seconded = vm.Folders.Single(f => f.Id == secondedId);
        Assert.True(group.IsCovered);
        Assert.True(group.IsTicked);
        Assert.False(group.CanTick);

        group.IsTicked = true;
        seconded.IsTicked = true;
        Assert.Equal(new[] { intake.Id, secondedId }, vm.PickedFolderIds);
        Assert.Equal("Разом 6 осіб з 2 папок, вкладені папки входять.", vm.NewPeopleSummary);

        intake.IsTicked = false;
        Assert.False(group.IsCovered);
        Assert.False(group.IsTicked);

        vm.NewDateFrom = From.ToDateTime(TimeOnly.MinValue);
        vm.NewDateTo = From.AddDays(9).ToDateTime(TimeOnly.MinValue);
        vm.NewWeekendsGreen = false;
        intake.IsTicked = true;
        await vm.CreateCommand.ExecuteAsync(null);

        Assert.Equal(6, vm.Rows.Count);
        Assert.EndsWith("· Набір №29, Командировані", vm.RosterCaption);

        await vm.StartCreateCommand.ExecuteAsync(null);
        Assert.Equal(new[] { intake.Id, secondedId }, vm.PickedFolderIds);
        Assert.True(vm.Folders.Single(f => f.Id == secondedId).IsChecked);
        Assert.Contains(vm.Folders.SelectMany(f => f.SelfAndDescendants()), f => f.Id == groupId && f.IsCovered);
    }

    [Fact]
    public async Task CreatedRosterShowsAFullGridAndStats()
    {
        using var db = new TestDb();
        var (vm, _) = await CreatedRoster(db);

        Assert.True(vm.HasRoster);
        Assert.False(vm.IsCreating);
        Assert.Equal(12, vm.Rows.Count);
        Assert.Equal(20, vm.Days.Count);
        Assert.All(Enumerable.Range(0, 20), d => Assert.Equal(3, OnDuty(vm, d)));
        Assert.All(vm.Days, d => Assert.Equal(3, d.Count));
        Assert.StartsWith("Нарядів на людину: 5 - 5", vm.StatsText);
        Assert.Contains("на вересень-жовтень 2026 р.", vm.TitlePreview);
        Assert.Single(vm.Rosters);
    }

    [Fact]
    public async Task PaintingDutyMakesItManualKeepsThreePerDayAndSaves()
    {
        using var db = new TestDb();
        var (vm, service) = await CreatedRoster(db);
        var row = vm.Rows.First(r => !r.Cells[0].IsDuty);

        vm.BeginPaint(row.Cells[0]);
        await vm.EndPaintAsync();

        Assert.True(row.Cells[0].IsDuty);
        Assert.False(row.Cells[0].IsAuto);
        Assert.Equal(3, OnDuty(vm, 0));

        var saved = await service.LoadAsync(vm.Roster!.Id);
        Assert.Equal(DutyCellSource.Manual, saved!.Members[vm.Rows.IndexOf(row)].Duties[From]);
    }

    [Fact]
    public async Task ClickingAnAutoDutyRemovesItForGoodWithACross()
    {
        using var db = new TestDb();
        var (vm, _) = await CreatedRoster(db);
        var row = vm.Rows.First(r => r.Cells[2].IsAuto);

        vm.BeginPaint(row.Cells[2]);
        await vm.EndPaintAsync();

        Assert.False(row.Cells[2].IsDuty);
        Assert.True(row.Cells[2].IsBlocked);
        Assert.Equal("×", row.Cells[2].Text);
        Assert.Equal(3, OnDuty(vm, 2));
    }

    [Fact]
    public async Task DraggingTheExemptBrushPaintsSeveralDaysGreenAndFreesThem()
    {
        using var db = new TestDb();
        var (vm, _) = await CreatedRoster(db);
        var row = vm.Rows[4];

        vm.Brush = DutyBrush.Exempt;
        vm.BeginPaint(row.Cells[5]);
        vm.ContinuePaint(row.Cells[6]);
        vm.ContinuePaint(row.Cells[7]);
        await vm.EndPaintAsync();

        Assert.All(row.Cells.Skip(5).Take(3), c =>
        {
            Assert.True(c.IsExempt);
            Assert.False(c.IsDuty);
        });
        Assert.All(Enumerable.Range(5, 3), d => Assert.Equal(3, OnDuty(vm, d)));
    }

    [Fact]
    public async Task RowAndDayExemptionsClearDuties()
    {
        using var db = new TestDb();
        var (vm, _) = await CreatedRoster(db);

        await vm.ToggleRowExemptCommand.ExecuteAsync(vm.Rows[0]);
        await vm.ToggleDayExemptCommand.ExecuteAsync(vm.Days[3]);

        Assert.True(vm.Rows[^1].IsExemptWholePeriod);
        Assert.Equal(0, vm.Rows[^1].Total);
        Assert.True(vm.Days[3].IsExempt);
        Assert.Equal(0, OnDuty(vm, 3));
        Assert.False(vm.Days[3].IsShort);
        Assert.Equal("Повернути в графік", vm.Rows[^1].ExemptMenuHeader);
    }

    [Fact]
    public async Task RandomizeChangesTheVariantButKeepsManualMarks()
    {
        using var db = new TestDb();
        var (vm, _) = await CreatedRoster(db);
        var row = vm.Rows.First(r => !r.Cells[0].IsDuty);
        vm.BeginPaint(row.Cells[0]);
        await vm.EndPaintAsync();
        var seed = vm.Roster!.Seed;

        await vm.RandomizeCommand.ExecuteAsync(null);

        Assert.NotEqual(seed, vm.Roster.Seed);
        Assert.True(row.Cells[0].IsDuty);
        Assert.False(row.Cells[0].IsAuto);
    }

    [Fact]
    public async Task FreezeMakesEveryDutyManualAndClearManualRemovesThem()
    {
        using var db = new TestDb();
        var (vm, _) = await CreatedRoster(db);

        await vm.FreezeCommand.ExecuteAsync(null);
        Assert.DoesNotContain(vm.Rows.SelectMany(r => r.Cells), c => c.IsAuto);

        await vm.ClearManualCoreAsync();
        Assert.All(vm.Rows.SelectMany(r => r.Cells).Where(c => c.IsDuty), c => Assert.True(c.IsAuto));
        Assert.All(Enumerable.Range(0, 20), d => Assert.Equal(3, OnDuty(vm, d)));
    }

    [Fact]
    public async Task ChangingThePeriodRebuildsTheGridAndRenamesTheRoster()
    {
        using var db = new TestDb();
        var (vm, service) = await CreatedRoster(db);

        vm.DateTo = From.AddDays(29).ToDateTime(TimeOnly.MinValue);
        await vm.PendingWork;

        Assert.Equal(30, vm.Days.Count);
        Assert.All(Enumerable.Range(0, 30), d => Assert.Equal(3, OnDuty(vm, d)));
        Assert.Equal("19.09 - 18.10.2026 · Набір №29", vm.SelectedRoster?.Label);
        Assert.Equal(From.AddDays(29), (await service.LoadAsync(vm.Roster!.Id))!.DateTo);

        vm.DateTo = From.AddDays(-1).ToDateTime(TimeOnly.MinValue);
        await vm.PendingWork;
        Assert.Equal("Дата «по» раніша за дату «з».", vm.PeriodError);
        Assert.Equal(30, vm.Days.Count);
    }

    [Fact]
    public async Task MovingARowChangesNumbersAndShiftsButNotWhoStandsWhen()
    {
        using var db = new TestDb();
        var (vm, service) = await CreatedRoster(db);
        var first = vm.Rows[0];
        var duties = first.Cells.Where(c => c.IsDuty).Select(c => c.Date).ToList();

        await vm.MoveDownCommand.ExecuteAsync(first);

        Assert.Same(first, vm.Rows[1]);
        Assert.Equal(2, first.Number);
        Assert.Equal(duties, first.Cells.Where(c => c.IsDuty).Select(c => c.Date));
        var saved = await service.LoadAsync(vm.Roster!.Id);
        Assert.Equal(first.DisplayName, saved!.Members[1].DisplayName);
    }

    [Fact]
    public async Task CellsShowRotatedShiftsAndJustNWhenTicked()
    {
        using var db = new TestDb();
        var (vm, service) = await CreatedRoster(db);

        Assert.False(vm.DutyMarkOnly);
        Assert.True(vm.Roster!.ShiftRotation);
        foreach (var day in Enumerable.Range(0, vm.Days.Count))
        {
            Assert.Equal(new[] { "І", "ІІ", "ІІІ" },
                vm.Rows.Select(r => r.Cells[day]).Where(c => c.IsDuty).Select(c => c.Text).OrderBy(t => t.Length));
        }
        Assert.Contains(Enumerable.Range(0, vm.Days.Count),
            day => vm.Rows.Select(r => r.Cells[day]).First(c => c.IsDuty).Text != "І");
        Assert.Contains("зміна (по черзі змін)", vm.Rows.SelectMany(r => r.Cells).First(c => c.IsDuty).Hint);

        vm.DutyMarkOnly = true;
        await vm.PendingWork;

        Assert.All(vm.Rows.SelectMany(r => r.Cells).Where(c => c.IsDuty), c => Assert.Equal("н", c.Text));
        Assert.True((await service.LoadAsync(vm.Roster!.Id))!.DutyMarkOnly);

        var next = await service.CreateAsync(new DutyRosterCreateRequest(From.AddDays(20), From.AddDays(30), vm.Roster.FolderIds, true, WeekendsGreen: false));
        Assert.True(next.DutyMarkOnly);
    }

    [Fact]
    public async Task AddingAPersonByNameAddsARowThatJoinsTheQueue()
    {
        using var db = new TestDb();
        var (vm, service) = await CreatedRoster(db);

        vm.ManualName = "ВПИСАНИЙ В.В.";
        await vm.AddManualPersonCommand.ExecuteAsync(null);

        Assert.Equal(13, vm.Rows.Count);
        Assert.Equal("ВПИСАНИЙ В.В.", vm.Rows[^1].DisplayName);
        Assert.True(vm.Rows[^1].Total > 0);
        Assert.Equal(string.Empty, vm.ManualName);
        Assert.Equal(13, (await service.LoadAsync(vm.Roster!.Id))!.Members.Count);
    }

    [Fact]
    public async Task RosterWithoutIntakeOpensTheMembersPanelAndTakesTypedNames()
    {
        using var db = new TestDb();
        SeedIntake(db, 3);
        var service = new DutyRosterService(db.Factory, new FakeAuditLog(), new FakeCurrentUser(), TestServices.UserSettings(db, null));
        var vm = new DutyRosterViewModel(service, new ActiveIntakeState(new NoActiveIntake()));
        await vm.InitializeCommand.ExecuteAsync(null);
        await vm.PendingWork;

        Assert.Single(vm.Folders).IsChecked = false;
        Assert.Empty(vm.PickedFolderIds);
        Assert.StartsWith("Папок не відмічено", vm.NewPeopleSummary);
        vm.NewDateFrom = From.ToDateTime(TimeOnly.MinValue);
        vm.NewDateTo = From.AddDays(5).ToDateTime(TimeOnly.MinValue);
        vm.NewWeekendsGreen = false;
        await vm.CreateCommand.ExecuteAsync(null);

        Assert.True(vm.HasRoster);
        Assert.Empty(vm.Rows);
        Assert.True(vm.IsSettingsOpen);
        Assert.Equal(3, vm.PeopleToAdd.Count);

        foreach (var name in new[] { "ПЕРШИЙ А.А.", "ДРУГИЙ Б.Б.", "ТРЕТІЙ В.В." })
        {
            vm.ManualName = name;
            await vm.AddManualPersonCommand.ExecuteAsync(null);
        }

        Assert.Equal(3, vm.Rows.Count);
        Assert.All(Enumerable.Range(0, 6), d => Assert.Equal(3, OnDuty(vm, d)));
    }

    [Fact]
    public async Task RemovedPersonStaysOutUntilAddedBack()
    {
        using var db = new TestDb();
        var (vm, service) = await CreatedRoster(db);
        var removed = vm.Rows[2].Member.RecipientId!.Value;

        await vm.RemoveMemberCoreAsync(vm.Rows[2]);
        Assert.Contains(removed, (await service.LoadAsync(vm.Roster!.Id))!.ExcludedRecipientIds);

        vm.PersonToAdd = vm.PeopleToAdd.Single(p => p.RecipientId == removed);
        await vm.AddPersonCommand.ExecuteAsync(null);

        Assert.Contains(vm.Rows, r => r.Member.RecipientId == removed);
        Assert.DoesNotContain(removed, (await service.LoadAsync(vm.Roster.Id))!.ExcludedRecipientIds);
    }

    [Fact]
    public async Task WeekendsCheckboxOnlyColoursSaturdaysAndSundays()
    {
        using var db = new TestDb();
        var (vm, _) = await CreatedRoster(db);
        Assert.False(vm.WeekendsGreen);
        var before = vm.Rows.Select(r => string.Join(",", r.Cells.Select(c => c.Text))).ToList();

        vm.WeekendsGreen = true;
        await vm.PendingWork;

        var weekends = vm.Days.Where(d => d.IsWeekend).ToList();
        Assert.NotEmpty(weekends);
        Assert.All(weekends, d =>
        {
            Assert.True(d.IsWeekendGreen);
            Assert.False(d.IsExempt);
            Assert.Equal(3, d.Count);
        });
        var weekendIndex = vm.Days.IndexOf(weekends[0]);
        Assert.All(vm.Rows, r => Assert.True(r.Cells[weekendIndex].IsWeekendGreen));
        Assert.All(vm.Days, d => Assert.Equal(3, d.Count));
        Assert.Equal(before, vm.Rows.Select(r => string.Join(",", r.Cells.Select(c => c.Text))));

        vm.WeekendsGreen = false;
        await vm.PendingWork;

        Assert.All(weekends, d => Assert.False(d.IsWeekendGreen));
        Assert.Equal(before, vm.Rows.Select(r => string.Join(",", r.Cells.Select(c => c.Text))));

        await vm.StartCreateCommand.ExecuteAsync(null);
        Assert.False(vm.NewWeekendsGreen);
    }

    [Fact]
    public async Task RosterSavedWithEmptyWeekendsIsRefilledOnOpen()
    {
        using var db = new TestDb();
        var (vm, service) = await CreatedRoster(db);
        var roster = (await service.LoadAsync(vm.Roster!.Id))!;
        var saturday = roster.Days.First(DutyRosterPlanner.IsWeekend);
        foreach (var member in roster.Members) member.Duties.Remove(saturday);
        await service.SaveAsync(roster);

        await vm.OpenRosterAsync(roster.Id);

        Assert.Equal(3, vm.Days.Single(d => d.Date == saturday).Count);
        Assert.Equal(3, (await service.LoadAsync(roster.Id))!.Members.Count(m => m.Duties.ContainsKey(saturday)));
    }

    [Fact]
    public async Task CanteenBrushPutsYellowKThatTheQueueWorksAround()
    {
        using var db = new TestDb();
        var (vm, service) = await CreatedRoster(db);
        var cell = vm.Rows.First(r => r.Cells[4].IsDuty).Cells[4];
        var member = cell.Row.Member;

        vm.IsCanteenBrush = true;
        vm.BeginPaint(cell);
        await vm.EndPaintAsync();

        Assert.True(cell.IsCanteen);
        Assert.False(cell.IsDuty);
        Assert.Equal("к", cell.Text);
        Assert.Equal(3, vm.Days[4].Count);
        var saved = (await service.LoadAsync(vm.Roster!.Id))!;
        var index = vm.Rows.IndexOf(cell.Row);
        Assert.True(saved.IsCanteen(index, vm.Days[4].Date));
        Assert.False(saved.IsDuty(index, vm.Days[4].Date));

        vm.Brush = DutyBrush.Erase;
        vm.BeginPaint(cell);
        await vm.EndPaintAsync();

        Assert.False(cell.IsCanteen);
        Assert.Empty(member.Canteen);
    }

    [Fact]
    public async Task ShiftBrushPinsTheShiftAndOthersTakeTheRest()
    {
        using var db = new TestDb();
        var (vm, service) = await CreatedRoster(db);
        Assert.Equal(new[] { "І", "ІІ", "ІІІ" }, vm.ShiftBrushes.Select(b => b.Label));
        var onDuty = vm.Rows.Where(r => r.Cells[2].IsDuty).ToList();
        var pinned = onDuty.First(r => r.Cells[2].Text != "І");
        var others = onDuty.Where(r => r != pinned).ToList();

        vm.ShiftBrushes[0].IsSelected = true;
        Assert.Equal(DutyBrush.Shift, vm.Brush);
        Assert.False(vm.IsDutyBrush);
        vm.BeginPaint(pinned.Cells[2]);
        await vm.EndPaintAsync();

        Assert.Equal("І", pinned.Cells[2].Text);
        Assert.Equal(new[] { "ІІ", "ІІІ" }, others.Select(r => r.Cells[2].Text).OrderBy(t => t.Length));
        Assert.Equal(3, vm.Days[2].Count);
        var saved = (await service.LoadAsync(vm.Roster!.Id))!;
        Assert.Equal(1, saved.Members[vm.Rows.IndexOf(pinned)].Shifts[vm.Days[2].Date]);

        vm.BeginPaint(others[0].Cells[2]);
        await vm.EndPaintAsync();
        Assert.True(others[0].Cells[2].HasWarning);
        Assert.Contains("Одна зміна вручну двом людям", vm.WarningsText);

        vm.BeginPaint(pinned.Cells[2]);
        await vm.EndPaintAsync();
        Assert.False(pinned.Member.Shifts.ContainsKey(vm.Days[2].Date));
        Assert.Equal("І", others[0].Cells[2].Text);
        Assert.DoesNotContain("Одна зміна вручну двом людям", vm.WarningsText);

        vm.IsDutyBrush = true;
        Assert.All(vm.ShiftBrushes, b => Assert.False(b.IsSelected));
    }

    [Fact]
    public async Task ExemptWholePeriodMovesToTheBottom()
    {
        using var db = new TestDb();
        var (vm, service) = await CreatedRoster(db);
        var row = vm.Rows[2];
        var others = vm.Rows.Where(r => r != row).Select(r => r.DisplayName).ToList();

        await vm.ToggleRowExemptCommand.ExecuteAsync(row);

        Assert.Equal(row.DisplayName, vm.Rows[^1].DisplayName);
        Assert.True(vm.Rows[^1].IsExemptWholePeriod);
        Assert.Equal(others, vm.Rows.Take(vm.Rows.Count - 1).Select(r => r.DisplayName));
        Assert.Equal(vm.Rows.Count, vm.Rows[^1].Number);
        Assert.Equal(row.DisplayName, (await service.LoadAsync(vm.Roster!.Id))!.Members[^1].DisplayName);

        vm.ManualName = "ВПИСАНИЙ В.В.";
        await vm.AddManualPersonCommand.ExecuteAsync(null);
        Assert.Equal("ВПИСАНИЙ В.В.", vm.Rows[^2].DisplayName);
        Assert.Equal(row.DisplayName, vm.Rows[^1].DisplayName);
    }

    [Fact]
    public async Task FastDragFillsTheCellsTheMouseSkipped()    {
        using var db = new TestDb();
        var (vm, _) = await CreatedRoster(db);
        var row = vm.Rows[4];

        vm.Brush = DutyBrush.Exempt;
        vm.BeginPaint(row.Cells[2]);
        vm.ContinuePaint(row.Cells[7]);
        vm.ContinuePaint(vm.Rows[6].Cells[7]);
        await vm.EndPaintAsync();

        Assert.All(row.Cells.Skip(2).Take(6), c => Assert.True(c.IsExempt));
        Assert.True(vm.Rows[5].Cells[7].IsExempt);
        Assert.True(vm.Rows[6].Cells[7].IsExempt);
        Assert.False(row.Cells[8].IsExempt);
        Assert.False(row.Cells[1].IsExempt);
    }

    [Fact]
    public async Task LongPeriodWarnsThatA4PrintWillBeSmall()
    {
        using var db = new TestDb();
        var (vm, _) = await CreatedRoster(db, days: 31);
        Assert.DoesNotContain("Аркуш A4", vm.WarningsText);

        vm.DateTo = From.AddDays(61).ToDateTime(TimeOnly.MinValue);
        await vm.PendingWork;

        Assert.True(vm.HasWarnings);
        Assert.Contains("Аркуш A4: 62 дні, 12 осіб - друк дрібний", vm.WarningsText);
    }

    [Fact]
    public async Task TooFewPeopleShowWarnings()
    {
        using var db = new TestDb();
        var (vm, _) = await CreatedRoster(db, people: 2, days: 3);

        Assert.True(vm.HasWarnings);
        Assert.Contains("Неповні дні", vm.WarningsText);
        Assert.All(vm.Days, d => Assert.True(d.IsShort));
    }
}
