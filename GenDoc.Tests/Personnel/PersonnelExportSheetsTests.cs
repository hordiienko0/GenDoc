using System.IO;
using ClosedXML.Excel;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Duty;
using GenDoc.Services.Personnel;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Personnel;

namespace GenDoc.Tests.Personnel;

// Експорт окремими аркушами: усі слухачі, придатні й обмежено придатні з прикомандированих, набір.
// Папки відмічають галочками в дереві - як у графіку чергувань і вечірній перевірці.
public class PersonnelExportSheetsTests
{
    private sealed record Seed(int RootId, int PlatoonId, int SecondedId, int StaffId);

    // «31 набір» → «1 взвод», «Прикомандировані»; поруч у корені - «Постійний склад».
    private static Seed SeedTree(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });

        var root = new OrgNode { Name = "31 набір", Depth = 0, SortOrder = 0, Path = "/" };
        var staff = new OrgNode { Name = "Постійний склад", Depth = 0, SortOrder = 1, Path = "/" };
        ctx.OrgNodes.AddRange(root, staff);
        ctx.SaveChanges();
        root.Path = $"/{root.Id}/";
        staff.Path = $"/{staff.Id}/";

        var platoon = new OrgNode { Name = "1 взвод", ParentId = root.Id, Depth = 1, SortOrder = 0, Path = "/" };
        var seconded = new OrgNode { Name = "Прикомандировані", ParentId = root.Id, Depth = 1, SortOrder = 1, Path = "/" };
        ctx.OrgNodes.AddRange(platoon, seconded);
        ctx.SaveChanges();
        platoon.Path = $"{root.Path}{platoon.Id}/";
        seconded.Path = $"{root.Path}{seconded.Id}/";

        ctx.Intakes.Add(new Intake { Number = 31, DisplayNumber = "31", RootOrgNodeId = root.Id });

        Recipient Person(int id, string last, string first, string rank, int nodeId, string? fitness = null)
        {
            var person = TemplateFixtures.Person(id, last, first, rank);
            person.OrgNodeId = nodeId;
            person.FitnessCategory = fitness;
            return person;
        }

        ctx.Recipients.AddRange(
            Person(1, "ШЕВЧЕНКО", "Тарас", "полковник", root.Id),
            Person(2, "ГНАТЮК", "Олег", "солдат", root.Id),
            Person(3, "ФРАНКО", "Іван", "старший сержант", platoon.Id, FitnessCategoryHelper.Regular),
            Person(4, "КОВАЛЬ", "Петро", "майор", seconded.Id, FitnessCategoryHelper.Regular),
            Person(5, "ЄРМАК", "Андрій", "солдат", seconded.Id, FitnessCategoryHelper.Limited),
            Person(6, "БОЙКО", "Назар", "капітан", seconded.Id),
            Person(7, "ІВАНЕНКО", "Ігор", "майор", staff.Id));
        ctx.SaveChanges();
        return new Seed(root.Id, platoon.Id, seconded.Id, staff.Id);
    }

    private static string[] LastNames(IEnumerable<PersonExportRow> rows) => rows.Select(r => r.Person.LastName).ToArray();

    private static async Task<PersonnelExportDialogViewModel> OpenDialogAsync(
        TestDb db, PersonExportRequest request, IAuditLogService? audit = null)
    {
        var vm = new PersonnelExportDialogViewModel(TestServices.PersonExport(db, 1, audit), request);
        await vm.InitializeAsync();
        return vm;
    }

    private static PersonExportRequest RootRequest(Seed seed) => new(seed.RootId, "31 набір", "31 набір", true);

    private static ExportSheetOptionViewModel Sheet(PersonnelExportDialogViewModel vm, PersonExportSheetKind kind)
        => vm.Sheets.Single(s => s.Kind == kind);

    private static DutyFolderOption Folder(int id, int? parentId, string name, bool isIntakeRoot = false)
        => new(id, parentId, name, null, isIntakeRoot, Array.Empty<int>());

    // ---------- дерево й поділ ----------

    [Fact]
    public async Task LoadTree_ListsFoldersParentsFirst_EachWithPeopleOfItsNestedFolders()
    {
        using var db = new TestDb();
        var seed = SeedTree(db);

        var tree = await TestServices.PersonExport(db).LoadTreeAsync();

        Assert.Equal(new[] { "31 набір", "Постійний склад", "1 взвод", "Прикомандировані" }, tree.Folders.Select(f => f.Name));
        Assert.Equal(7, tree.Rows.Count);

        var root = tree.Folders.Single(f => f.Id == seed.RootId);
        Assert.True(root.IsIntakeRoot);
        Assert.Null(root.ParentId);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, root.PeopleIds.Order());

        var seconded = tree.Folders.Single(f => f.Id == seed.SecondedId);
        Assert.False(seconded.IsIntakeRoot);
        Assert.Equal(seed.RootId, seconded.ParentId);
        Assert.Equal(new[] { 4, 5, 6 }, seconded.PeopleIds.Order());

        Assert.Equal(new[] { 7 }, tree.Folders.Single(f => f.Id == seed.StaffId).PeopleIds);
        Assert.Equal(seed.RootId, tree.DefaultIntakeRootId);
    }

    [Fact]
    public async Task LoadTree_DefaultIntake_IsTheLastActiveOne_OrTheNewestWhenNoneIsActive()
    {
        using var db = new TestDb();
        var roots = new Dictionary<int, int>();
        using (var ctx = db.Factory.CreateDbContext())
        {
            foreach (var (number, status) in new[]
                     {
                         (29, IntakeStatus.Active), (30, IntakeStatus.Completed),
                         (31, IntakeStatus.Active), (32, IntakeStatus.Planned)
                     })
            {
                var root = new OrgNode { Name = $"{number} набір", Depth = 0, SortOrder = number, Path = "/" };
                ctx.OrgNodes.Add(root);
                ctx.SaveChanges();
                root.Path = $"/{root.Id}/";
                ctx.Intakes.Add(new Intake { Number = number, DisplayNumber = $"{number}", RootOrgNodeId = root.Id, Status = status });
                roots[number] = root.Id;
            }
            ctx.SaveChanges();
        }

        Assert.Equal(roots[31], (await TestServices.PersonExport(db).LoadTreeAsync()).DefaultIntakeRootId);

        using (var ctx = db.Factory.CreateDbContext())
        {
            foreach (var intake in ctx.Intakes.Where(i => i.Status == IntakeStatus.Active)) intake.Status = IntakeStatus.Completed;
            ctx.SaveChanges();
        }

        Assert.Equal(roots[32], (await TestServices.PersonExport(db).LoadTreeAsync()).DefaultIntakeRootId);
    }

    [Fact]
    public async Task Sheets_SplitPeopleIntoEveryoneSecondedByFitnessAndTheIntakeWithoutSeconded()
    {
        using var db = new TestDb();
        var seed = SeedTree(db);
        var rows = PersonExportQuery.Apply(
            await TestServices.PersonExport(db).LoadAsync(seed.RootId),
            new PersonExportFilter { NodeId = seed.RootId }, PersonExportSort.Alphabet);
        var secondedIds = new HashSet<int> { 4, 5, 6 };

        string[] On(PersonExportSheetKind kind) => LastNames(PersonExportSheets.Rows(kind, rows, secondedIds));

        Assert.Equal(new[] { "БОЙКО", "ГНАТЮК", "ЄРМАК", "КОВАЛЬ", "ФРАНКО", "ШЕВЧЕНКО" }, On(PersonExportSheetKind.All));
        // Без позначки придатності - придатний, як і всюди в програмі.
        Assert.Equal(new[] { "БОЙКО", "КОВАЛЬ" }, On(PersonExportSheetKind.SecondedFit));
        Assert.Equal(new[] { "ЄРМАК" }, On(PersonExportSheetKind.SecondedLimited));
        Assert.Equal(new[] { "ГНАТЮК", "ФРАНКО", "ШЕВЧЕНКО" }, On(PersonExportSheetKind.Intake));
    }

    [Fact]
    public void SheetNames_FitExcelLimits_AndGoInTheAgreedOrder()
    {
        Assert.Equal(
            new[] { "Усі слухачі", "Придатні (прикоманд.)", "Обмежено придатні (прикоманд.)", "Набір" },
            PersonExportSheets.Kinds.Select(PersonExportSheets.SheetName));
        Assert.All(PersonExportSheets.Kinds.Select(PersonExportSheets.SheetName), name =>
        {
            Assert.InRange(name.Length, 1, 31);
            Assert.Equal(-1, name.IndexOfAny(new[] { ':', '\\', '/', '?', '*', '[', ']' }));
        });
    }

    // ---------- які папки відмічені одразу ----------

    [Fact]
    public void DefaultIntakeFolder_IsTheIntakeTheExportWasOpenedFrom_OtherwiseTheLastActiveOne()
    {
        var folders = new[]
        {
            Folder(1, null, "Курси"),
            Folder(2, 1, "30 набір", isIntakeRoot: true),
            Folder(3, 1, "31 набір", isIntakeRoot: true),
            Folder(4, 3, "Всі"),
            Folder(5, 4, "Придатні"),
            Folder(6, 1, "Прикомандировані"),
            Folder(7, null, "Постійний склад")
        };
        var tree = new PersonExportTree(folders, Array.Empty<PersonExportRow>(), DefaultIntakeRootId: 3);

        // З кореня набору чи будь-якої вкладеної папки - цей набір цілком.
        Assert.Equal(3, PersonnelExportDialogViewModel.DefaultIntakeFolder(tree, 3));
        Assert.Equal(3, PersonnelExportDialogViewModel.DefaultIntakeFolder(tree, 5));
        Assert.Equal(2, PersonnelExportDialogViewModel.DefaultIntakeFolder(tree, 2));
        // Не з набору (папка над наборами, прикомандировані, постійний склад) - останній активний набір.
        Assert.Equal(3, PersonnelExportDialogViewModel.DefaultIntakeFolder(tree, 1));
        Assert.Equal(3, PersonnelExportDialogViewModel.DefaultIntakeFolder(tree, 6));
        Assert.Equal(3, PersonnelExportDialogViewModel.DefaultIntakeFolder(tree, 7));
        // Наборів немає взагалі - лишається сама відкрита папка.
        Assert.Equal(7, PersonnelExportDialogViewModel.DefaultIntakeFolder(tree with { DefaultIntakeRootId = null }, 7));
    }

    // ---------- діалог ----------

    [Fact]
    public async Task Dialog_OpensAsOneList_AndTheSheetsAreOptIn()
    {
        using var db = new TestDb();
        var seed = SeedTree(db);

        var vm = await OpenDialogAsync(db, RootRequest(seed));

        Assert.False(vm.BySheets);
        Assert.True(vm.IsSingleList);
        Assert.True(vm.ShowIncludeDescendants);
        Assert.Equal("Папка: 31 набір", vm.ScopeText);
        Assert.Equal("До експорту: 6 осіб · 6 колонок", vm.SummaryText);
        Assert.True(vm.CopyCommand.CanExecute(null));
    }

    [Fact]
    public async Task Dialog_BySheets_StartsFromTheOpenedFolderAndItsSecondedFolder()
    {
        using var db = new TestDb();
        var seed = SeedTree(db);
        var vm = await OpenDialogAsync(db, RootRequest(seed));

        vm.BySheets = true;

        Assert.Equal(new[] { seed.RootId }, vm.IntakeFolders.PickedIds);
        Assert.Equal(new[] { seed.SecondedId }, vm.SecondedFolders.PickedIds);
        Assert.Equal("3 особи з 1 папки, вкладені папки входять.", vm.SecondedFolders.Summary);

        // Набір і прикомандировані - різне: у дереві набору їхня папка не відмічена, хоч і лежить у наборі.
        var intakeRoot = vm.IntakeFolders.Folders.Single(f => f.Id == seed.RootId);
        var secondedInIntakeTree = intakeRoot.Children.Single(f => f.Id == seed.SecondedId);
        Assert.True(intakeRoot.IsTicked);
        Assert.True(intakeRoot.Children.Single(f => f.Id == seed.PlatoonId).IsTicked);
        Assert.True(secondedInIntakeTree.IsExcluded);
        Assert.False(secondedInIntakeTree.IsTicked);
        Assert.False(secondedInIntakeTree.CanTick);
        Assert.Equal(new[] { 1, 2, 3 }, vm.IntakeFolders.PeopleIds.Order());
        Assert.Equal("3 особи з 1 папки, вкладені папки входять, без 3 прикомандированих.", vm.IntakeFolders.Summary);

        // Аркуші йдуть так: усі слухачі, придатні й обмежено придатні з прикомандированих, у кінці набір.
        Assert.Equal(
            new[]
            {
                PersonExportSheetKind.All, PersonExportSheetKind.SecondedFit,
                PersonExportSheetKind.SecondedLimited, PersonExportSheetKind.Intake
            },
            vm.Sheets.Select(s => s.Kind));
        Assert.Equal(new[] { 6, 2, 1, 3 }, vm.Sheets.Select(s => s.Count));
        Assert.All(vm.Sheets, s => Assert.True(s.IsChecked));
        Assert.Equal(6, vm.MatchCount);
        Assert.Equal("До експорту: 4 аркуші · 6 осіб · 6 колонок", vm.SummaryText);
        Assert.Equal("Аркуші з папок: Прикомандировані, 31 набір", vm.ScopeText);
        Assert.False(vm.ShowIncludeDescendants);

        Assert.True(vm.SaveCommand.CanExecute(null));
        // Книгу з аркушами в буфер обміну не покласти.
        Assert.False(vm.CopyCommand.CanExecute(null));

        vm.BySheets = false;
        Assert.Equal("До експорту: 6 осіб · 6 колонок", vm.SummaryText);
        Assert.True(vm.CopyCommand.CanExecute(null));
    }

    [Fact]
    public async Task Dialog_BySheets_CountsOnlyCheckedSheets_AndFiltersApplyToAllOfThem()
    {
        using var db = new TestDb();
        var seed = SeedTree(db);
        var vm = await OpenDialogAsync(db, RootRequest(seed));
        vm.BySheets = true;

        // Без «Усіх слухачів» ті самі шестеро розходяться по трьох інших аркушах.
        Sheet(vm, PersonExportSheetKind.All).IsChecked = false;
        Assert.Equal(6, vm.MatchCount);
        Assert.Equal("До експорту: 3 аркуші · 6 осіб · 6 колонок", vm.SummaryText);

        Sheet(vm, PersonExportSheetKind.Intake).IsChecked = false;
        Assert.Equal(3, vm.MatchCount);

        vm.SelectedFitness = vm.FitnessOptions.Single(o => o.Value == FitnessFilter.LimitedOnly);
        Assert.Equal(new[] { 1, 0, 1, 0 }, vm.Sheets.Select(s => s.Count));
        Assert.Equal(1, vm.MatchCount);
        // Лічильники звань рахуються від людей відмічених папок, а не від фільтрів.
        Assert.Equal(new[] { 3, 1, 2, 0 }, vm.RankOptions.Select(o => o.Count));

        vm.ResetFiltersCommand.Execute(null);
        vm.SearchText = "ковал";
        Assert.Equal(new[] { 1, 1, 0, 0 }, vm.Sheets.Select(s => s.Count));

        vm.SearchText = null;
        foreach (var sheet in vm.Sheets) sheet.IsChecked = false;
        Assert.Equal(0, vm.MatchCount);
        Assert.Equal("Відмітьте хоча б один аркуш", vm.SummaryText);
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Dialog_BySheets_FoldersAreTickedInATree_LikeInTheDutyRoster()
    {
        using var db = new TestDb();
        var seed = SeedTree(db);
        var vm = await OpenDialogAsync(db, RootRequest(seed));
        vm.BySheets = true;

        // Ще одна папка в набір - її люди додаються до «Усіх слухачів» і «Набору».
        vm.IntakeFolders.Folders.Single(f => f.Id == seed.StaffId).IsTicked = true;
        Assert.Equal(new[] { 7, 2, 1, 4 }, vm.Sheets.Select(s => s.Count));
        Assert.Equal("Аркуші з папок: Прикомандировані, 31 набір, Постійний склад", vm.ScopeText);
        Assert.Equal(new[] { 4, 1, 2, 0 }, vm.RankOptions.Select(o => o.Count));

        // Вкладена у відмічену вже входить - окремо її не відмітити.
        var nested = vm.IntakeFolders.Folders.Single(f => f.Id == seed.RootId).Children.Single(f => f.Id == seed.PlatoonId);
        Assert.True(nested.IsTicked);
        Assert.False(nested.CanTick);

        // Зняли папку прикомандированих - тепер це звичайна папка набору: вона знову відмічена разом із ним.
        vm.SecondedFolders.Folders.SelectMany(f => f.SelfAndDescendants()).Single(f => f.Id == seed.SecondedId).IsTicked = false;
        Assert.Equal(new[] { 7, 0, 0, 7 }, vm.Sheets.Select(s => s.Count));
        Assert.Equal("Папок не відмічено.", vm.SecondedFolders.Summary);
        var formerSeconded = vm.IntakeFolders.Folders.SelectMany(f => f.SelfAndDescendants()).Single(f => f.Id == seed.SecondedId);
        Assert.False(formerSeconded.IsExcluded);
        Assert.True(formerSeconded.IsTicked);
        Assert.Equal("7 осіб з 2 папок, вкладені папки входять.", vm.IntakeFolders.Summary);

        foreach (var folder in vm.IntakeFolders.Folders) folder.IsTicked = false;
        Assert.Equal(0, vm.MatchCount);
        Assert.Equal("Відмітьте папки набору чи прикомандированих", vm.SummaryText);
        Assert.Equal("Аркуші з папок: папок не відмічено", vm.ScopeText);
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Dialog_BySheets_SavesOneWorkbook_EachSheetNumberedFromOne_AndRemembersTheChoice()
    {
        using var db = new TestDb();
        var seed = SeedTree(db);
        var audit = new FakeAuditLog();
        var vm = await OpenDialogAsync(db, RootRequest(seed), audit);
        ExportFieldOptionViewModel Option(string key) =>
            vm.FieldGroups.SelectMany(g => g.Fields).Single(f => f.Field.Key == key);

        vm.BySheets = true;
        vm.ClearFieldsCommand.Execute(null);
        Option("RowNumber").IsChecked = true;
        Option("LastName").IsChecked = true;
        Option("Fitness").IsChecked = true;
        var path = Path.Combine(Path.GetTempPath(), $"personnel_sheets_{Guid.NewGuid():N}.xlsx");

        try
        {
            var result = await vm.ExportToFileAsync(path);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(6, result.RowCount);

            using var workbook = new XLWorkbook(path);
            Assert.Equal(
                new[] { "Усі слухачі", "Придатні (прикоманд.)", "Обмежено придатні (прикоманд.)", "Набір" },
                workbook.Worksheets.Select(w => w.Name));

            string[] Column(IXLWorksheet sheet, int column) => sheet.RowsUsed().Skip(1)
                .Select(r => r.Cell(column).GetString()).ToArray();

            var all = workbook.Worksheet("Усі слухачі");
            Assert.Equal(new[] { "№ з/п", "Прізвище", "Придатність" }, Enumerable.Range(1, 3).Select(c => all.Cell(1, c).GetString()));
            Assert.Equal(new[] { "БОЙКО", "ГНАТЮК", "ЄРМАК", "КОВАЛЬ", "ФРАНКО", "ШЕВЧЕНКО" }, Column(all, 2));
            Assert.Equal(new[] { "1", "2", "3", "4", "5", "6" }, Column(all, 1));

            var fit = workbook.Worksheet("Придатні (прикоманд.)");
            Assert.Equal(new[] { "БОЙКО", "КОВАЛЬ" }, Column(fit, 2));
            Assert.Equal(new[] { "1", "2" }, Column(fit, 1));

            var limited = workbook.Worksheet("Обмежено придатні (прикоманд.)");
            Assert.Equal(new[] { "ЄРМАК" }, Column(limited, 2));
            Assert.Equal(new[] { "1" }, Column(limited, 1));
            Assert.Equal(FitnessCategoryHelper.Limited, limited.Cell(2, 3).GetString());

            var intake = workbook.Worksheet("Набір");
            Assert.Equal(new[] { "ГНАТЮК", "ФРАНКО", "ШЕВЧЕНКО" }, Column(intake, 2));
            Assert.Equal(new[] { "1", "2", "3" }, Column(intake, 1));
            Assert.Equal(XLPageOrientation.Landscape, intake.PageSetup.PageOrientation);
            Assert.True(intake.AutoFilter.IsEnabled);

            Assert.Equal("export:Recipient:6", Assert.Single(audit.Entries));
            Assert.Contains("«Прикомандировані, 31 набір»", audit.Details[0]);
            Assert.Contains("аркуші: Усі слухачі - 6, Придатні (прикоманд.) - 2, Обмежено придатні (прикоманд.) - 1, Набір - 3", audit.Details[0]);
        }
        finally
        {
            File.Delete(path);
        }

        // Наступного разу вікно відкривається одразу з аркушами.
        var again = await OpenDialogAsync(db, RootRequest(seed));
        Assert.True(again.BySheets);
        Assert.All(again.Sheets, s => Assert.True(s.IsChecked));
        Assert.Equal(new[] { 6, 2, 1, 3 }, again.Sheets.Select(s => s.Count));
        Assert.Equal("До експорту: 4 аркуші · 6 осіб · 3 колонки", again.SummaryText);
    }

    [Fact]
    public async Task Dialog_BySheets_WritesOnlyCheckedSheets_AndAnEmptyOneKeepsItsHeader()
    {
        using var db = new TestDb();
        var seed = SeedTree(db);
        var vm = await OpenDialogAsync(db, RootRequest(seed));
        vm.BySheets = true;
        Sheet(vm, PersonExportSheetKind.All).IsChecked = false;
        Sheet(vm, PersonExportSheetKind.Intake).IsChecked = false;
        vm.SelectedFitness = vm.FitnessOptions.Single(o => o.Value == FitnessFilter.RegularOnly);
        var path = Path.Combine(Path.GetTempPath(), $"personnel_sheets_{Guid.NewGuid():N}.xlsx");

        try
        {
            var result = await vm.ExportToFileAsync(path);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(2, result.RowCount);

            using var workbook = new XLWorkbook(path);
            Assert.Equal(new[] { "Придатні (прикоманд.)", "Обмежено придатні (прикоманд.)" }, workbook.Worksheets.Select(w => w.Name));

            var empty = workbook.Worksheet("Обмежено придатні (прикоманд.)");
            Assert.Equal("№ з/п", empty.Cell(1, 1).GetString());
            Assert.True(empty.Cell(2, 1).IsEmpty());
            Assert.Single(empty.RowsUsed());
        }
        finally
        {
            File.Delete(path);
        }

        var again = await OpenDialogAsync(db, RootRequest(seed));
        Assert.True(again.BySheets);
        Assert.Equal(new[] { false, true, true, false }, again.Sheets.Select(s => s.IsChecked));
    }

    [Fact]
    public async Task Dialog_ForCheckedPeople_HasNoSheets_AndDoesNotForgetTheSheetChoice()
    {
        using var db = new TestDb();
        var seed = SeedTree(db);
        await TestServices.PersonExport(db, 1).SavePrefsAsync(new PersonExportPrefs
        {
            Fields = PersonExportFields.DefaultKeys.ToList(),
            BySheets = true,
            Sheets = new List<PersonExportSheetKind> { PersonExportSheetKind.SecondedFit }
        });

        var vm = await OpenDialogAsync(db, RootRequest(seed) with { OnlyIds = new[] { 4, 7 } });

        Assert.False(vm.IsFolderScope);
        Assert.False(vm.BySheets);
        Assert.False(vm.ShowIncludeDescendants);
        Assert.Equal(new[] { "КОВАЛЬ" }, LastNames(vm.MatchedRows));
        Assert.True(vm.CopyCommand.CanExecute(null));

        var path = Path.Combine(Path.GetTempPath(), $"personnel_sheets_{Guid.NewGuid():N}.xlsx");
        try
        {
            var result = await vm.ExportToFileAsync(path);
            Assert.True(result.Success, result.ErrorMessage);
            using var workbook = new XLWorkbook(path);
            Assert.Equal(PersonExportService.SheetName, Assert.Single(workbook.Worksheets).Name);
        }
        finally
        {
            File.Delete(path);
        }

        var prefs = await TestServices.PersonExport(db, 1).LoadPrefsAsync();
        Assert.True(prefs.BySheets);
        Assert.Equal(new[] { PersonExportSheetKind.SecondedFit }, prefs.Sheets);
    }

    [Theory]
    [InlineData("platoon")]   // вкладена папка набору
    [InlineData("seconded")]  // сама папка прикомандированих
    [InlineData("staff")]     // папка поза набором
    public async Task Dialog_BySheets_WhereverItWasOpenedFrom_TakesTheWholeIntakeAndItsSeconded(string openedFrom)
    {
        using var db = new TestDb();
        var seed = SeedTree(db);
        var nodeId = openedFrom switch
        {
            "platoon" => seed.PlatoonId,
            "seconded" => seed.SecondedId,
            _ => seed.StaffId
        };
        var vm = await OpenDialogAsync(db, new PersonExportRequest(nodeId, "папка", "папка", true));

        vm.BySheets = true;

        Assert.Equal(new[] { seed.RootId }, vm.IntakeFolders.PickedIds);
        Assert.Equal(new[] { seed.SecondedId }, vm.SecondedFolders.PickedIds);
        Assert.Equal(new[] { 6, 2, 1, 3 }, vm.Sheets.Select(s => s.Count));
        Assert.Equal("Аркуші з папок: Прикомандировані, 31 набір", vm.ScopeText);

        // Один список без аркушів і далі бере саме ту папку, з якої відкрили.
        vm.BySheets = false;
        Assert.Equal(openedFrom == "seconded" ? 3 : 1, vm.MatchCount);
    }
}
