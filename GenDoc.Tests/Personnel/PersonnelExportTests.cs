using System.IO;
using ClosedXML.Excel;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Personnel;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Personnel;

namespace GenDoc.Tests.Personnel;

// Експорт особового складу: папка (з вкладеними чи без) або позначені, фільтри, вибрані поля в заданому порядку.
public class PersonnelExportTests
{
    private sealed record Seed(int RootId, int RegularId, int LimitedId);

    // «31 набір» → «Придатні», «Обмежено придатні»; люди лежать і в корені, і у вкладених папках.
    private static Seed SeedTree(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });

        var root = new OrgNode { Name = "31 набір", Depth = 0, SortOrder = 0, Path = "/" };
        ctx.OrgNodes.Add(root);
        ctx.SaveChanges();
        root.Path = $"/{root.Id}/";

        var regular = new OrgNode { Name = "Придатні", ParentId = root.Id, Depth = 1, SortOrder = 0, Path = "/" };
        var limited = new OrgNode { Name = "Обмежено придатні", ParentId = root.Id, Depth = 1, SortOrder = 1, Path = "/" };
        ctx.OrgNodes.AddRange(regular, limited);
        ctx.SaveChanges();
        regular.Path = $"{root.Path}{regular.Id}/";
        limited.Path = $"{root.Path}{limited.Id}/";

        var room = new Room { Building = "Корпус 2", Number = "14", Capacity = 4 };
        ctx.Rooms.Add(room);
        ctx.SaveChanges();

        var shevchenko = TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас", "полковник");
        shevchenko.OrgNodeId = root.Id;
        shevchenko.ServiceNumber = "0012345";
        shevchenko.Phone = "+380501112233";
        shevchenko.DateOfBirth = new DateOnly(1980, 3, 9);
        shevchenko.RoomId = room.Id;

        var franko = TemplateFixtures.Person(2, "ФРАНКО", "Іван", "старший сержант");
        franko.OrgNodeId = regular.Id;
        franko.FitnessCategory = FitnessCategoryHelper.Regular;

        var yermak = TemplateFixtures.Person(3, "ЄРМАК", "Андрій", "солдат");
        yermak.OrgNodeId = limited.Id;
        yermak.FitnessCategory = FitnessCategoryHelper.Limited;
        yermak.Vos = "100";
        yermak.Phone = "0671234567";

        var koval = TemplateFixtures.Person(4, "КОВАЛЬ", "Петро");
        koval.OrgNodeId = regular.Id;
        koval.FitnessCategory = FitnessCategoryHelper.Regular;
        koval.Vos = "122";

        var hnatiuk = new Recipient
        {
            Id = 5, LastName = "ГНАТЮК", FirstName = "Олег", MiddleName = null,
            Rank = "працівник ЗСУ", Position = "слухач", ServiceNumber = "СН0005", OrgNodeId = root.Id
        };

        ctx.Recipients.AddRange(shevchenko, franko, yermak, koval, hnatiuk);
        ctx.SaveChanges();
        return new Seed(root.Id, regular.Id, limited.Id);
    }

    private static string[] LastNames(IEnumerable<PersonExportRow> rows) => rows.Select(r => r.Person.LastName).ToArray();

    private static PersonExportField Field(string key) => PersonExportFields.Find(key)!;

    private static PersonExportRequest FolderRequest(Seed seed, bool includeDescendants = true)
        => new(seed.RootId, "31 набір", "31 набір", includeDescendants);

    private static async Task<PersonnelExportDialogViewModel> OpenDialogAsync(TestDb db, PersonExportRequest request)
    {
        var vm = new PersonnelExportDialogViewModel(TestServices.PersonExport(db, 1), request);
        await vm.InitializeAsync();
        return vm;
    }

    // ---------- завантаження й фільтри ----------

    [Fact]
    public async Task Load_TakesTheWholeBranch_WithFolderNamePathAndTreeOrder()
    {
        using var db = new TestDb();
        var seed = SeedTree(db);

        var rows = await TestServices.PersonExport(db).LoadAsync(seed.RootId);

        Assert.Equal(5, rows.Count);
        var yermak = rows.Single(r => r.Person.LastName == "ЄРМАК");
        Assert.Equal("Обмежено придатні", yermak.FolderName);
        Assert.Equal("31 набір › Обмежено придатні", yermak.FolderPath);

        var shevchenko = rows.Single(r => r.Person.LastName == "ШЕВЧЕНКО");
        Assert.Equal("31 набір", shevchenko.FolderPath);
        Assert.Equal("Корпус 2 14", Field("Room").Text(shevchenko));
        Assert.True(shevchenko.FolderOrder < rows.Single(r => r.Person.LastName == "ФРАНКО").FolderOrder);
        Assert.True(rows.Single(r => r.Person.LastName == "ФРАНКО").FolderOrder < yermak.FolderOrder);
    }

    [Fact]
    public async Task Load_FromSubfolder_KeepsThePathFromTheRoot()
    {
        using var db = new TestDb();
        var seed = SeedTree(db);

        var rows = await TestServices.PersonExport(db).LoadAsync(seed.RegularId);

        Assert.Equal(new[] { "КОВАЛЬ", "ФРАНКО" }, LastNames(rows.OrderBy(r => r.Person.LastName)));
        Assert.All(rows, r => Assert.Equal("31 набір › Придатні", r.FolderPath));
    }

    [Fact]
    public async Task Filter_WithoutDescendants_KeepsOnlyPeopleOfTheFolderItself()
    {
        using var db = new TestDb();
        var seed = SeedTree(db);
        var rows = await TestServices.PersonExport(db).LoadAsync(seed.RootId);

        var own = PersonExportQuery.Apply(rows,
            new PersonExportFilter { NodeId = seed.RootId, IncludeDescendants = false }, PersonExportSort.Rank);

        Assert.Equal(new[] { "ШЕВЧЕНКО", "ГНАТЮК" }, LastNames(own));
    }

    [Fact]
    public async Task Filter_CombinesSearchRankFitnessAndFieldConditions()
    {
        using var db = new TestDb();
        var seed = SeedTree(db);
        var rows = await TestServices.PersonExport(db).LoadAsync(seed.RootId);

        List<PersonExportRow> Apply(PersonExportFilter filter) => PersonExportQuery.Apply(rows, filter, PersonExportSort.Rank);

        Assert.Equal(new[] { "ФРАНКО" }, LastNames(Apply(new PersonExportFilter { NodeId = seed.RootId, SearchText = "франк" })));

        Assert.Equal(new[] { "ШЕВЧЕНКО", "КОВАЛЬ" }, LastNames(Apply(new PersonExportFilter
        {
            NodeId = seed.RootId, RankCategories = new[] { RankCategory.Officers }
        })));

        Assert.Equal(new[] { "ЄРМАК" }, LastNames(Apply(new PersonExportFilter
        {
            NodeId = seed.RootId, Fitness = FitnessFilter.LimitedOnly
        })));

        Assert.Equal(new[] { "КОВАЛЬ", "ФРАНКО", "ГНАТЮК" }, LastNames(Apply(new PersonExportFilter
        {
            NodeId = seed.RootId,
            Conditions = new[] { new PersonExportCondition(Field("Phone"), PersonExportOperator.IsEmpty, null) }
        })));

        Assert.Equal(new[] { "КОВАЛЬ" }, LastNames(Apply(new PersonExportFilter
        {
            NodeId = seed.RootId,
            Conditions = new[]
            {
                new PersonExportCondition(Field("Vos"), PersonExportOperator.IsNotEmpty, null),
                new PersonExportCondition(Field("Vos"), PersonExportOperator.NotContains, "100")
            }
        })));

        Assert.Equal(new[] { "ШЕВЧЕНКО" }, LastNames(Apply(new PersonExportFilter
        {
            NodeId = seed.RootId,
            Conditions = new[] { new PersonExportCondition(Field("DateOfBirth"), PersonExportOperator.Contains, ".1980") }
        })));

        Assert.Equal(new[] { "ЄРМАК" }, LastNames(Apply(new PersonExportFilter
        {
            NodeId = seed.RootId,
            Conditions = new[] { new PersonExportCondition(Field("Folder"), PersonExportOperator.EqualTo, "обмежено придатні") }
        })));
    }

    [Fact]
    public async Task Filter_ConditionWithoutValue_IsStillBeingTyped_AndDoesNotHideAnyone()
    {
        using var db = new TestDb();
        var seed = SeedTree(db);
        var rows = await TestServices.PersonExport(db).LoadAsync(seed.RootId);

        var result = PersonExportQuery.Apply(rows, new PersonExportFilter
        {
            NodeId = seed.RootId,
            Conditions = new[] { new PersonExportCondition(Field("Vos"), PersonExportOperator.Contains, "  ") }
        }, PersonExportSort.Rank);

        Assert.Equal(5, result.Count);
    }

    [Fact]
    public async Task Sort_ByRankThenSurname_OrByFolderThenRankThenSurname()
    {
        using var db = new TestDb();
        var seed = SeedTree(db);
        var rows = await TestServices.PersonExport(db).LoadAsync(seed.RootId);
        var filter = new PersonExportFilter { NodeId = seed.RootId };

        Assert.Equal(new[] { "ШЕВЧЕНКО", "КОВАЛЬ", "ФРАНКО", "ЄРМАК", "ГНАТЮК" },
            LastNames(PersonExportQuery.Apply(rows, filter, PersonExportSort.Rank)));
        // У корені полковник ШЕВЧЕНКО вище за ГНАТЮКА, хоч за абеткою той перший; без звання - у кінці папки.
        Assert.Equal(new[] { "ШЕВЧЕНКО", "ГНАТЮК", "КОВАЛЬ", "ФРАНКО", "ЄРМАК" },
            LastNames(PersonExportQuery.Apply(rows, filter, PersonExportSort.Folder)));
    }

    [Fact]
    public async Task Sort_SameRank_GoesBySurnameAlphabetically()
    {
        using var db = new TestDb();
        var seed = SeedTree(db);
        using (var ctx = db.Factory.CreateDbContext())
        {
            foreach (var (id, lastName) in new[] { (6, "ЯКИМЕНКО"), (7, "АНДРІЄНКО"), (8, "ІВАНЕНКО"), (9, "ҐАВРИЛЮК") })
            {
                var person = TemplateFixtures.Person(id, lastName, "Іван", "солдат");
                person.OrgNodeId = seed.LimitedId;
                ctx.Recipients.Add(person);
            }
            ctx.SaveChanges();
        }

        var rows = await TestServices.PersonExport(db).LoadAsync(seed.LimitedId);
        var sorted = PersonExportQuery.Apply(rows, new PersonExportFilter { NodeId = seed.LimitedId }, PersonExportSort.Rank);

        Assert.Equal(new[] { "АНДРІЄНКО", "ҐАВРИЛЮК", "ЄРМАК", "ІВАНЕНКО", "ЯКИМЕНКО" }, LastNames(sorted));
    }

    [Fact]
    public async Task Dialog_OrdersRowsByRankThenSurname_EvenIfAlphabetWasRememberedBefore()
    {
        using var db = new TestDb();
        var seed = SeedTree(db);
        // Колись запам'ятоване «За абеткою» (0) - такого порядку більше немає.
        await TestServices.PersonExport(db, 1).SavePrefsAsync(new PersonExportPrefs { Sort = (PersonExportSort)0 });

        var vm = await OpenDialogAsync(db, FolderRequest(seed));

        Assert.Equal(PersonExportSort.Rank, vm.SelectedSort.Value);
        Assert.DoesNotContain(vm.SortOptions, o => o.Value == (PersonExportSort)0);
        Assert.Equal(new[] { "ШЕВЧЕНКО", "КОВАЛЬ", "ФРАНКО", "ЄРМАК", "ГНАТЮК" }, LastNames(vm.MatchedRows));
    }

    // ---------- каталог полів ----------

    [Fact]
    public void Catalog_HasUniqueKeys_CoversTheWholeQuestionnaire_AndDefaultsExist()
    {
        Assert.Equal(PersonExportFields.All.Count, PersonExportFields.All.Select(f => f.Key).Distinct().Count());
        Assert.All(PersonQuestionnaire.Fields, q => Assert.NotNull(PersonExportFields.Find(q.Key)));
        Assert.All(PersonExportFields.DefaultKeys, key => Assert.NotNull(PersonExportFields.Find(key)));
    }

    // ---------- файл ----------

    [Fact]
    public async Task Xlsx_HasChosenColumnsInOrder_TypedDates_AndKeepsLeadingZeros()
    {
        using var db = new TestDb();
        var seed = SeedTree(db);
        var audit = new FakeAuditLog();
        var service = TestServices.PersonExport(db, auditLog: audit);
        var rows = PersonExportQuery.Apply(
            await service.LoadAsync(seed.RootId), new PersonExportFilter { NodeId = seed.RootId }, PersonExportSort.Rank);
        var fields = new[] { "RowNumber", "FullName", "Rank", "ServiceNumber", "DateOfBirth", "Phone", "Folder" }
            .Select(Field).ToList();
        var path = Path.Combine(Path.GetTempPath(), $"personnel_export_{Guid.NewGuid():N}.xlsx");

        try
        {
            var result = await service.SaveXlsxAsync(rows, fields, path, "31 набір");

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(5, result.RowCount);

            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheet(PersonExportService.SheetName);
            Assert.Equal(
                new[] { "№ з/п", "ПІБ повністю", "Звання", "Особовий номер", "Дата народження", "Телефон", "Папка (підрозділ)" },
                Enumerable.Range(1, 7).Select(c => sheet.Cell(1, c).GetString()));

            Assert.Equal(1, sheet.Cell(2, 1).GetValue<int>());
            Assert.Equal("ШЕВЧЕНКО Тарас Петрович", sheet.Cell(2, 2).GetString());
            Assert.Equal("полковник", sheet.Cell(2, 3).GetString());
            Assert.Equal(XLDataType.Text, sheet.Cell(2, 4).DataType);
            Assert.Equal("0012345", sheet.Cell(2, 4).GetString());
            Assert.Equal(XLDataType.DateTime, sheet.Cell(2, 5).DataType);
            Assert.Equal(new DateTime(1980, 3, 9), sheet.Cell(2, 5).GetDateTime());
            Assert.Equal("+380501112233", sheet.Cell(2, 6).GetString());
            Assert.Equal("31 набір", sheet.Cell(2, 7).GetString());

            Assert.Equal(5, sheet.Cell(6, 1).GetValue<int>());
            Assert.Equal("ГНАТЮК Олег", sheet.Cell(6, 2).GetString());
            Assert.True(sheet.Cell(6, 5).IsEmpty());
            Assert.True(sheet.Cell(7, 1).IsEmpty());

            Assert.Equal(XLPageOrientation.Landscape, sheet.PageSetup.PageOrientation);
            Assert.True(sheet.AutoFilter.IsEnabled);

            var entry = Assert.Single(audit.Entries);
            Assert.Equal("export:Recipient:5", entry);
            Assert.Contains("31 набір", audit.Details[0]);
            Assert.Contains("Телефон", audit.Details[0]);
            Assert.Contains(Path.GetFileName(path), audit.Details[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ClipboardTable_IsTabSeparated_WithHeaderAndOneLinePerPerson()
    {
        using var db = new TestDb();
        var seed = SeedTree(db);
        var rows = PersonExportQuery.Apply(
            await TestServices.PersonExport(db).LoadAsync(seed.RootId),
            new PersonExportFilter { NodeId = seed.RootId, SearchText = "шевч" }, PersonExportSort.Rank);

        var text = PersonExportTable.Build(rows, new[] { Field("RowNumber"), Field("ShortName"), Field("DateOfBirth"), Field("Vos") })
            .ToClipboardText();

        Assert.Equal(
            "№ з/п\tПІБ скорочено (ПРІЗВИЩЕ І. П.)\tДата народження\tВОС" + Environment.NewLine
            + "1\tШЕВЧЕНКО Т. П.\t09.03.1980\t",
            text);
    }

    // ---------- діалог ----------

    [Fact]
    public async Task Dialog_OpensWithDefaultColumns_AndCountsEveryoneInTheFolder()
    {
        using var db = new TestDb();
        var seed = SeedTree(db);

        var vm = await OpenDialogAsync(db, FolderRequest(seed));

        Assert.Equal(PersonExportFields.DefaultKeys, vm.SelectedFields.Select(f => f.Field.Key));
        Assert.Equal(5, vm.MatchCount);
        Assert.Equal("До експорту: 5 осіб · 6 колонок", vm.SummaryText);
        Assert.True(vm.SaveCommand.CanExecute(null));
        Assert.False(vm.HasActiveFilters);

        Assert.Equal(new[] { 2, 1, 1, 1 }, vm.RankOptions.Select(o => o.Count));
        Assert.All(vm.RankOptions, o => Assert.True(o.IsChecked));
    }

    [Fact]
    public async Task Dialog_FiltersNarrowTheCount_AndResetBringsEveryoneBack()
    {
        using var db = new TestDb();
        var seed = SeedTree(db);
        var vm = await OpenDialogAsync(db, FolderRequest(seed));

        vm.IncludeDescendants = false;
        Assert.Equal(2, vm.MatchCount);
        Assert.Equal(new[] { 1, 0, 0, 1 }, vm.RankOptions.Select(o => o.Count));

        vm.IncludeDescendants = true;
        vm.RankOptions.Single(o => o.Category == RankCategory.Officers).IsChecked = false;
        Assert.Equal(new[] { "ФРАНКО", "ЄРМАК", "ГНАТЮК" }, LastNames(vm.MatchedRows));

        vm.SelectedFitness = vm.FitnessOptions.Single(o => o.Value == FitnessFilter.RegularOnly);
        Assert.Equal(new[] { "ФРАНКО", "ГНАТЮК" }, LastNames(vm.MatchedRows));

        vm.AddConditionCommand.Execute(null);
        var condition = Assert.Single(vm.Conditions);
        condition.Field = Field("Fitness");
        condition.Comparison = condition.Operators.Single(o => o.Value == PersonExportOperator.IsNotEmpty);
        Assert.False(condition.NeedsValue);
        Assert.Equal(new[] { "ФРАНКО" }, LastNames(vm.MatchedRows));
        Assert.True(vm.HasActiveFilters);

        vm.ResetFiltersCommand.Execute(null);
        Assert.Equal(5, vm.MatchCount);
        Assert.Empty(vm.Conditions);
        Assert.False(vm.HasActiveFilters);
    }

    [Fact]
    public async Task Dialog_NoMatchesOrNoColumns_BlocksExport()
    {
        using var db = new TestDb();
        var seed = SeedTree(db);
        var vm = await OpenDialogAsync(db, FolderRequest(seed));

        vm.SearchText = "немає такого";
        Assert.Equal(0, vm.MatchCount);
        Assert.Equal("Під ці умови ніхто не підпадає", vm.SummaryText);
        Assert.False(vm.SaveCommand.CanExecute(null));
        Assert.False(vm.CopyCommand.CanExecute(null));

        vm.SearchText = null;
        vm.ClearFieldsCommand.Execute(null);
        Assert.Equal("Оберіть хоча б одне поле", vm.SummaryText);
        Assert.True(vm.HasNoColumns);
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Dialog_CheckedFieldsBecomeColumnsInTheOrderTheyWereChosen_AndCanBeMoved()
    {
        using var db = new TestDb();
        var seed = SeedTree(db);
        var vm = await OpenDialogAsync(db, FolderRequest(seed));
        ExportFieldOptionViewModel Option(string key) =>
            vm.FieldGroups.SelectMany(g => g.Fields).Single(f => f.Field.Key == key);

        vm.ClearFieldsCommand.Execute(null);
        Option("Phone").IsChecked = true;
        Option("FullName").IsChecked = true;
        Option("Rank").IsChecked = true;
        Assert.Equal(new[] { "Phone", "FullName", "Rank" }, vm.SelectedFields.Select(f => f.Field.Key));
        Assert.Equal("ПОРЯДОК КОЛОНОК (3)", vm.ColumnsHeader);

        vm.MoveFieldUpCommand.Execute(Option("Rank"));
        vm.MoveFieldDownCommand.Execute(Option("Phone"));
        vm.MoveFieldUpCommand.Execute(Option("Rank"));
        Assert.Equal(new[] { "Rank", "Phone", "FullName" }, vm.SelectedFields.Select(f => f.Field.Key));

        vm.RemoveFieldCommand.Execute(Option("Phone"));
        Assert.False(Option("Phone").IsChecked);
        Assert.Equal(new[] { "Rank", "FullName" }, vm.SelectedFields.Select(f => f.Field.Key));

        vm.SelectAllFieldsCommand.Execute(null);
        Assert.Equal(PersonExportFields.All.Count, vm.SelectedFields.Count);
        Assert.Equal(new[] { "Rank", "FullName" }, vm.SelectedFields.Take(2).Select(f => f.Field.Key));

        vm.DefaultFieldsCommand.Execute(null);
        Assert.Equal(PersonExportFields.DefaultKeys, vm.SelectedFields.Select(f => f.Field.Key));
    }

    [Fact]
    public async Task Dialog_SavesTheFile_AndRemembersColumnsAndSortForTheNextTime()
    {
        using var db = new TestDb();
        var seed = SeedTree(db);
        var vm = await OpenDialogAsync(db, FolderRequest(seed));
        ExportFieldOptionViewModel Option(string key) =>
            vm.FieldGroups.SelectMany(g => g.Fields).Single(f => f.Field.Key == key);

        vm.ClearFieldsCommand.Execute(null);
        Option("ShortName").IsChecked = true;
        Option("Vos").IsChecked = true;
        vm.SelectedSort = vm.SortOptions.Single(o => o.Value == PersonExportSort.Rank);
        vm.SelectedFitness = vm.FitnessOptions.Single(o => o.Value == FitnessFilter.LimitedOnly);
        var path = Path.Combine(Path.GetTempPath(), $"personnel_dialog_{Guid.NewGuid():N}.xlsx");

        try
        {
            var result = await vm.ExportToFileAsync(path);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(1, result.RowCount);
            using (var workbook = new XLWorkbook(path))
            {
                var sheet = workbook.Worksheet(PersonExportService.SheetName);
                Assert.Equal("ПІБ скорочено (ПРІЗВИЩЕ І. П.)", sheet.Cell(1, 1).GetString());
                Assert.Equal("ЄРМАК А. П.", sheet.Cell(2, 1).GetString());
                Assert.Equal("100", sheet.Cell(2, 2).GetString());
                Assert.True(sheet.Cell(3, 1).IsEmpty());
            }
        }
        finally
        {
            File.Delete(path);
        }

        // Наступне відкриття: ті самі колонки й сортування, а фільтри - з чистого аркуша.
        var again = await OpenDialogAsync(db, FolderRequest(seed));
        Assert.Equal(new[] { "ShortName", "Vos" }, again.SelectedFields.Select(f => f.Field.Key));
        Assert.Equal(PersonExportSort.Rank, again.SelectedSort.Value);
        Assert.Equal(FitnessFilter.All, again.SelectedFitness.Value);
        Assert.Equal(5, again.MatchCount);
    }

    [Fact]
    public async Task Dialog_ForCheckedPeople_TakesOnlyThem_WhateverFolderTheyAreIn()
    {
        using var db = new TestDb();
        var seed = SeedTree(db);

        var vm = await OpenDialogAsync(db,
            FolderRequest(seed, includeDescendants: false) with { OnlyIds = new[] { 2, 3 } });

        Assert.False(vm.IsFolderScope);
        Assert.Equal("Позначені в списку: 2 · папка «31 набір»", vm.ScopeText);
        Assert.Equal(new[] { "ФРАНКО", "ЄРМАК" }, LastNames(vm.MatchedRows));
        Assert.Equal("31 набір (позначені)", vm.ScopeTitle);
    }

    [Fact]
    public async Task Dialog_PrefilledSearchFromTheList_IsAnOrdinaryFilter()
    {
        using var db = new TestDb();
        var seed = SeedTree(db);

        var vm = await OpenDialogAsync(db, FolderRequest(seed) with { SearchText = "ковал" });

        Assert.Equal(new[] { "КОВАЛЬ" }, LastNames(vm.MatchedRows));
        Assert.True(vm.HasActiveFilters);
        Assert.StartsWith("особовий_склад_31 набір_", vm.SuggestedFileName());
        Assert.EndsWith(".xlsx", vm.SuggestedFileName());
    }

    [Fact]
    public async Task Dialog_ClipboardText_FollowsFiltersColumnsAndSort()
    {
        using var db = new TestDb();
        var seed = SeedTree(db);
        var vm = await OpenDialogAsync(db, FolderRequest(seed));
        ExportFieldOptionViewModel Option(string key) =>
            vm.FieldGroups.SelectMany(g => g.Fields).Single(f => f.Field.Key == key);

        vm.ClearFieldsCommand.Execute(null);
        Option("RowNumber").IsChecked = true;
        Option("LastName").IsChecked = true;
        vm.SelectedSort = vm.SortOptions.Single(o => o.Value == PersonExportSort.Rank);
        vm.RankOptions.Single(o => o.Category == RankCategory.Unknown).IsChecked = false;

        Assert.Equal(
            string.Join(Environment.NewLine, "№ з/п\tПрізвище", "1\tШЕВЧЕНКО", "2\tКОВАЛЬ", "3\tФРАНКО", "4\tЄРМАК"),
            vm.BuildClipboardText());
    }
}
