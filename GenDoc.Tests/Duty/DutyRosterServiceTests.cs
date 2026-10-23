using System.Data.Common;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Duty;
using GenDoc.Tests.Infrastructure;
using Microsoft.Data.Sqlite;

namespace GenDoc.Tests.Duty;

public class DutyRosterServiceTests
{
    private static readonly DateOnly From = new(2026, 9, 19);
    private static readonly DateOnly To = new(2026, 10, 22);

    private static DutyRosterService Service(TestDb db, FakeAuditLog? audit = null, int? userId = null)
        => new(db.Factory, audit ?? new FakeAuditLog(), new FakeCurrentUser(), TestServices.UserSettings(db, userId));

    private sealed record Seeded(int IntakeId, int GroupNodeId, List<Recipient> People, Recipient Staff);

    private static Seeded Seed(TestDb db, int people = 12)
    {
        using var ctx = db.Factory.CreateDbContext();

        var root = new OrgNode { Name = "Набір №29", Path = "/1/", Depth = 0 };
        ctx.OrgNodes.Add(root);
        ctx.SaveChanges();
        var group1 = new OrgNode { Name = "1 група", ParentId = root.Id, Path = "/1/2/", Depth = 1 };
        var group2 = new OrgNode { Name = "2 група", ParentId = root.Id, Path = "/1/3/", Depth = 1 };
        ctx.OrgNodes.AddRange(group1, group2);
        ctx.SaveChanges();

        var intake = new Intake
        {
            Number = 29, DisplayNumber = "Набір №29", Status = IntakeStatus.Active, RootOrgNodeId = root.Id,
            DateStart = new DateOnly(2026, 9, 1), DateEnd = new DateOnly(2026, 11, 30)
        };
        ctx.Intakes.Add(intake);
        ctx.SaveChanges();

        var list = Enumerable.Range(1, people).Select(i => new Recipient
        {
            LastName = $"Слухач{i:00}", FirstName = "Андрій", MiddleName = "Петрович", Rank = "солдат",
            IntakeId = intake.Id, OrgNodeId = i % 2 == 0 ? group2.Id : group1.Id
        }).ToList();
        var staff = new Recipient { LastName = "Курсовий", FirstName = "Олег", Rank = "капітан" };
        ctx.Recipients.AddRange(list);
        ctx.Recipients.Add(staff);
        ctx.SaveChanges();

        return new Seeded(intake.Id, group1.Id, list, staff);
    }

    [Fact]
    public async Task CreateTakesIntakePeopleFillsEveryDayAndSaves()
    {
        using var db = new TestDb();
        var seeded = Seed(db);
        var audit = new FakeAuditLog();

        var created = await Service(db, audit).CreateAsync(new DutyRosterCreateRequest(From, To, seeded.IntakeId, null, true, WeekendsExempt: false));

        Assert.True(created.Id > 0);
        Assert.Equal(12, created.Members.Count);
        Assert.Equal("СЛУХАЧ01 А.П.", created.Members[0].DisplayName);
        Assert.Equal("Набір №29", created.IntakeLabel);
        foreach (var day in created.Days)
            Assert.Equal(3, Enumerable.Range(0, created.Members.Count).Count(i => created.IsDuty(i, day)));
        Assert.Contains(audit.Entries, e => e.StartsWith("create:DutyRoster:", StringComparison.Ordinal));

        var loaded = await Service(db).LoadAsync(created.Id);
        Assert.NotNull(loaded);
        Assert.Equal(created.Seed, loaded!.Seed);
        Assert.Equal(created.Members.Select(m => m.DisplayName), loaded.Members.Select(m => m.DisplayName));
        Assert.Equal(created.Members.SelectMany(m => m.Duties.Keys).Count(), loaded.Members.SelectMany(m => m.Duties.Keys).Count());
        Assert.All(loaded.Members.SelectMany(m => m.Duties.Values), s => Assert.Equal(DutyCellSource.Auto, s));
    }

    [Fact]
    public async Task CreateForSubdivisionTakesOnlyItsPeople()
    {
        using var db = new TestDb();
        var seeded = Seed(db);

        var created = await Service(db).CreateAsync(new DutyRosterCreateRequest(From, To, seeded.IntakeId, seeded.GroupNodeId, true, WeekendsExempt: false));

        Assert.Equal(6, created.Members.Count);
        var options = await Service(db).GetOrgNodesAsync(seeded.IntakeId);
        Assert.Equal(new[] { "Увесь набір", "1 група", "2 група" }, options.Select(o => o.Label));
    }

    [Fact]
    public async Task EventDaysBecomeExemptAndGetNoDuty()
    {
        using var db = new TestDb();
        var seeded = Seed(db);
        var onLeave = seeded.People[0];
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.StaffEvents.Add(new StaffEvent
            {
                RecipientId = onLeave.Id, Kind = StaffEventKind.Leave, CreatedAt = DateTime.Now,
                DateStart = new DateOnly(2026, 9, 10), DateEnd = new DateOnly(2026, 9, 30)
            });
            ctx.SaveChanges();
        }

        var created = await Service(db).CreateAsync(new DutyRosterCreateRequest(From, To, seeded.IntakeId, null, true, WeekendsExempt: false));

        var member = created.Members.Single(m => m.RecipientId == onLeave.Id);
        var leaveDays = DutyRosterEngine.Days(From, new DateOnly(2026, 9, 30)).ToList();
        Assert.All(leaveDays, d => Assert.Equal(DutyCellSource.Event, member.Exemptions[d]));
        Assert.DoesNotContain(member.Duties.Keys, leaveDays.Contains);
        Assert.Contains(member.Duties.Keys, d => d > new DateOnly(2026, 9, 30));

        var withoutEvents = await Service(db).CreateAsync(new DutyRosterCreateRequest(From, To, seeded.IntakeId, null, false, WeekendsExempt: false));
        Assert.Empty(withoutEvents.Members.Single(m => m.RecipientId == onLeave.Id).Exemptions);
    }

    [Fact]
    public async Task ApplyEventExemptionsKeepsManualMarks()
    {
        using var db = new TestDb();
        var seeded = Seed(db);
        var service = Service(db);
        var roster = await service.CreateAsync(new DutyRosterCreateRequest(From, To, seeded.IntakeId, null, true, WeekendsExempt: false));
        roster.Members[1].Exemptions[From] = DutyCellSource.Manual;
        roster.Members[1].Exemptions[From.AddDays(1)] = DutyCellSource.Event;

        await service.ApplyEventExemptionsAsync(roster);

        Assert.Equal(DutyCellSource.Manual, roster.Members[1].Exemptions[From]);
        Assert.False(roster.Members[1].Exemptions.ContainsKey(From.AddDays(1)));
    }

    [Fact]
    public async Task SaveKeepsManualAutoAndExemptMarksAndDropsDaysOutsideThePeriod()
    {
        using var db = new TestDb();
        var seeded = Seed(db);
        var service = Service(db);
        var roster = await service.CreateAsync(new DutyRosterCreateRequest(From, To, seeded.IntakeId, null, false, WeekendsExempt: false));

        roster.Members[0].Duties[From] = DutyCellSource.Manual;
        roster.Members[0].ExemptWholePeriod = true;
        roster.Members[2].Blocked.Add(From.AddDays(1));
        roster.Members.Add(new DutyRosterMemberData { DisplayName = "ВПИСАНИЙ В.В." });
        roster.ExemptDays.Add(new DateOnly(2026, 9, 24));
        roster.Heading = "Графік чергувань\nкурсу";
        roster.Footer.Changeover = "09:00";
        roster.DateTo = new DateOnly(2026, 9, 30);
        await service.SaveAsync(roster);

        var loaded = (await service.LoadAsync(roster.Id))!;
        Assert.Equal(13, loaded.Members.Count);
        Assert.Equal("ВПИСАНИЙ В.В.", loaded.Members[^1].DisplayName);
        Assert.Null(loaded.Members[^1].RecipientId);
        Assert.Equal(DutyCellSource.Manual, loaded.Members[0].Duties[From]);
        Assert.True(loaded.Members[0].ExemptWholePeriod);
        Assert.Contains(From.AddDays(1), loaded.Members[2].Blocked);
        Assert.Contains(new DateOnly(2026, 9, 24), loaded.ExemptDays);
        Assert.Equal("Графік чергувань\nкурсу", loaded.Heading);
        Assert.Equal("09:00", loaded.Footer.Changeover);
        Assert.Equal(3, loaded.Footer.Shifts.Count);
        Assert.DoesNotContain(loaded.Members.SelectMany(m => m.Duties.Keys), d => d > loaded.DateTo);
    }

    [Fact]
    public async Task NewRosterContinuesTheQueueOfThePreviousOne()
    {
        using var db = new TestDb();
        var seeded = Seed(db, people: 9);
        var service = Service(db);

        var first = await service.CreateAsync(new DutyRosterCreateRequest(From, From.AddDays(3), seeded.IntakeId, null, true, WeekendsExempt: false));
        var servedTwice = Enumerable.Range(0, 9).Where(i => first.Members[i].Duties.Count == 2).Select(i => first.Members[i].Key).ToList();
        Assert.Equal(3, servedTwice.Count);

        var second = await service.CreateAsync(new DutyRosterCreateRequest(From.AddDays(4), From.AddDays(4), seeded.IntakeId, null, true, WeekendsExempt: false));
        var carry = await service.GetCarryOverAsync(second);

        Assert.NotNull(carry);
        Assert.Equal(DutyRosterText.PeriodLabel(From, From.AddDays(3)), carry!.FromLabel);
        var onFirstDay = second.Members.Where(m => m.Duties.ContainsKey(From.AddDays(4))).Select(m => m.Key).ToList();
        Assert.Equal(3, onFirstDay.Count);
        Assert.DoesNotContain(onFirstDay, servedTwice.Contains);
    }

    [Fact]
    public async Task NextRosterRepeatsThePreviousComposition()
    {
        using var db = new TestDb();
        var seeded = Seed(db, people: 8);
        var service = Service(db);

        var first = await service.CreateAsync(new DutyRosterCreateRequest(From, From.AddDays(9), seeded.IntakeId, null, true, WeekendsExempt: false));
        var moved = first.Members[5];
        first.Members.RemoveAt(5);
        first.Members.Insert(0, moved);
        first.Members[1].ExemptWholePeriod = true;
        var removed = first.Members[3];
        first.Members.Remove(removed);
        first.ExcludedRecipientIds.Add(removed.RecipientId!.Value);
        first.Members.Add(new DutyRosterMemberData { DisplayName = "ВПИСАНИЙ В.В." });
        first.Members.Add(new DutyRosterMemberData { RecipientId = seeded.Staff.Id, DisplayName = "КУРСОВИЙ О." });
        first.Heading = "Графік чергувань\nкурсу";
        first.CarryOverBalance = false;
        await service.SaveAsync(first);

        int newcomerId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var newcomer = new Recipient { LastName = "Новенький", FirstName = "Петро", Rank = "солдат", IntakeId = seeded.IntakeId };
            ctx.Recipients.Add(newcomer);
            var left = ctx.Recipients.Single(r => r.Id == first.Members[2].RecipientId);
            left.DeletedAt = DateTime.Now;
            ctx.SaveChanges();
            newcomerId = newcomer.Id;
        }
        var leftId = first.Members[2].RecipientId;

        var second = await service.CreateAsync(new DutyRosterCreateRequest(From.AddDays(10), From.AddDays(19), seeded.IntakeId, null, true, WeekendsExempt: false));

        Assert.Equal(moved.RecipientId, second.Members[0].RecipientId);
        Assert.True(second.Members[1].ExemptWholePeriod);
        Assert.DoesNotContain(second.Members, m => m.RecipientId == removed.RecipientId);
        Assert.Contains(removed.RecipientId!.Value, second.ExcludedRecipientIds);
        Assert.DoesNotContain(second.Members, m => m.RecipientId == leftId);
        Assert.Contains(second.Members, m => m.RecipientId is null && m.DisplayName == "ВПИСАНИЙ В.В.");
        Assert.Contains(second.Members, m => m.RecipientId == seeded.Staff.Id);
        Assert.Equal(newcomerId, second.Members[^1].RecipientId);
        Assert.Equal("НОВЕНЬКИЙ П.", second.Members[^1].DisplayName);
        Assert.Equal("Графік чергувань\nкурсу", second.Heading);
        Assert.False(second.CarryOverBalance);
        Assert.Equal(8 - 1 - 1 + 1 + 2, second.Members.Count);

        var reloaded = (await service.LoadAsync(second.Id))!;
        Assert.Contains(removed.RecipientId.Value, reloaded.ExcludedRecipientIds);
    }

    [Fact]
    public async Task RosterWithoutIntakeRepeatsTypedNames()
    {
        using var db = new TestDb();
        var service = Service(db);

        var first = await service.CreateAsync(new DutyRosterCreateRequest(From, From.AddDays(4), null, null, true, WeekendsExempt: false));
        first.Members.Add(new DutyRosterMemberData { DisplayName = "ПЕРШИЙ А.А." });
        first.Members.Add(new DutyRosterMemberData { DisplayName = "ДРУГИЙ Б.Б.", ExemptWholePeriod = true });
        await service.SaveAsync(first);

        var second = await service.CreateAsync(new DutyRosterCreateRequest(From.AddDays(5), From.AddDays(9), null, null, true, WeekendsExempt: false));

        Assert.Equal(new[] { "ПЕРШИЙ А.А.", "ДРУГИЙ Б.Б." }, second.Members.Select(m => m.DisplayName));
        Assert.True(second.Members[1].ExemptWholePeriod);
    }

    [Fact]
    public async Task WeekendsAreGreenRightAwayAndGetNoDuty()
    {
        using var db = new TestDb();
        var seeded = Seed(db);

        var roster = await Service(db).CreateAsync(new DutyRosterCreateRequest(From, From.AddDays(13), seeded.IntakeId, null, true));

        var weekends = roster.Days.Where(d => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday).ToList();
        Assert.Equal(4, weekends.Count);
        Assert.True(roster.WeekendsExempt);
        Assert.All(weekends, d => Assert.Contains(d, roster.ExemptDays));
        Assert.All(weekends, d => Assert.DoesNotContain(roster.Members, m => m.Duties.ContainsKey(d)));
        Assert.All(roster.Days.Except(weekends), d => Assert.Equal(3, roster.Members.Count(m => m.Duties.ContainsKey(d))));
        Assert.True((await Service(db).LoadAsync(roster.Id))!.WeekendsExempt);
    }

    [Fact]
    public async Task SettingsAreKeptPerCourseOfficer()
    {
        using var db = new TestDb();
        var seeded = Seed(db);
        int a, b;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var first = new UserProfile { FullName = "Перший Курсовий Офіцер", PasswordHash = "x", CreatedAt = DateTime.Now };
            var second = new UserProfile { FullName = "Другий Курсовий Офіцер", PasswordHash = "x", CreatedAt = DateTime.Now };
            ctx.Users.AddRange(first, second);
            ctx.SaveChanges();
            a = first.Id;
            b = second.Id;
        }

        var officerA = Service(db, userId: a);
        var rosterA = await officerA.CreateAsync(new DutyRosterCreateRequest(From, From.AddDays(9), seeded.IntakeId, null, true));
        rosterA.DutyMarkOnly = true;
        rosterA.WeekendsExempt = false;
        rosterA.Heading = "Шапка першого";
        rosterA.Footer.Changeover = "07:00";
        await officerA.SaveAsync(rosterA);
        await officerA.RememberPrefsAsync(rosterA);

        var officerB = Service(db, userId: b);
        Assert.Equal("Шапка першого", (await officerB.GetUserPrefsAsync()).Heading);
        var rosterB = await officerB.CreateAsync(new DutyRosterCreateRequest(From.AddDays(10), From.AddDays(19), seeded.IntakeId, null, true));
        rosterB.DutyMarkOnly = false;
        rosterB.WeekendsExempt = true;
        rosterB.Heading = "Шапка другого";
        await officerB.SaveAsync(rosterB);
        await officerB.RememberPrefsAsync(rosterB);

        var prefsA = await officerA.GetUserPrefsAsync();
        Assert.True(prefsA.DutyMarkOnly);
        Assert.False(prefsA.WeekendsExempt);
        Assert.Equal("Шапка першого", prefsA.Heading);
        Assert.Equal("07:00", prefsA.Footer.Changeover);

        var prefsB = await officerB.GetUserPrefsAsync();
        Assert.False(prefsB.DutyMarkOnly);
        Assert.True(prefsB.WeekendsExempt);
        Assert.Equal("Шапка другого", prefsB.Heading);

        var nextA = await officerA.CreateAsync(new DutyRosterCreateRequest(From.AddDays(20), From.AddDays(29), seeded.IntakeId, null, true, prefsA.WeekendsExempt));
        Assert.True(nextA.DutyMarkOnly);
        Assert.Equal("Шапка першого", nextA.Heading);
        Assert.Equal("07:00", nextA.Footer.Changeover);
    }

    [Fact]
    public async Task ListShowsNewestFirstAndDeleteHidesTheRoster()    {
        using var db = new TestDb();
        var seeded = Seed(db);
        var audit = new FakeAuditLog();
        var service = Service(db, audit);
        var older = await service.CreateAsync(new DutyRosterCreateRequest(From, To, seeded.IntakeId, null, true, WeekendsExempt: false));
        var newer = await service.CreateAsync(new DutyRosterCreateRequest(To.AddDays(1), To.AddDays(30), seeded.IntakeId, null, true, WeekendsExempt: false));

        var list = await service.GetListAsync();
        Assert.Equal(new[] { newer.Id, older.Id }, list.Select(r => r.Id));
        Assert.Equal("19.09 - 22.10.2026 · Набір №29", list[1].Label);

        await service.DeleteAsync(newer.Id);
        Assert.Equal(new[] { older.Id }, (await service.GetListAsync()).Select(r => r.Id));
        Assert.Contains(audit.Entries, e => e == $"delete:DutyRoster:{newer.Id}");
    }

    [Fact]
    public async Task PeopleToAddAreIntakeAndPermanentStaffExceptThoseInTheRoster()
    {
        using var db = new TestDb();
        var seeded = Seed(db, people: 3);

        var options = await Service(db).GetPeopleToAddAsync(seeded.IntakeId, new[] { seeded.People[0].Id });

        Assert.Equal(3, options.Count);
        Assert.DoesNotContain(options, o => o.RecipientId == seeded.People[0].Id);
        Assert.Equal(seeded.Staff.Id, options[^1].RecipientId);
        Assert.Equal("постійний склад", options[^1].Hint);
    }

    [Fact]
    public async Task CreateRejectsReversedOrTooLongPeriods()
    {
        using var db = new TestDb();
        var service = Service(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(new DutyRosterCreateRequest(To, From, null, null, true, WeekendsExempt: false)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(new DutyRosterCreateRequest(From, From.AddDays(DutyRosterService.MaxDays), null, null, true, WeekendsExempt: false)));
    }

    [Fact]
    public void EnsureDutyRosterTables_FreshDatabase_CreatesTablesAndIsIdempotent()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        DatabaseSchemaInitializer.EnsureDutyRosterTables((DbConnection)connection);
        DatabaseSchemaInitializer.EnsureDutyRosterTables((DbConnection)connection);

        var rosterColumns = DatabaseSchemaInitializer.GetExistingColumns((DbConnection)connection, "DutyRosters");
        Assert.Contains("Heading", rosterColumns);
        Assert.Contains("CarryOverBalance", rosterColumns);
        Assert.Contains("DeletedAt", rosterColumns);
        var memberColumns = DatabaseSchemaInitializer.GetExistingColumns((DbConnection)connection, "DutyRosterMembers");
        Assert.Contains("ExemptWholePeriod", memberColumns);
        var cellColumns = DatabaseSchemaInitializer.GetExistingColumns((DbConnection)connection, "DutyRosterCells");
        Assert.Contains("Source", cellColumns);
    }

    [Fact]
    public void EnsureDutyRosterTables_ExistingTableMissingColumns_AddsThem()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE "DutyRosters" ("Id" INTEGER NOT NULL CONSTRAINT "PK_DutyRosters" PRIMARY KEY AUTOINCREMENT,
                    "DateFrom" TEXT NOT NULL, "DateTo" TEXT NOT NULL, "CreatedAt" TEXT NOT NULL);
                INSERT INTO "DutyRosters" ("DateFrom", "DateTo", "CreatedAt") VALUES ('2026-09-19', '2026-10-22', '2026-09-18');
                """;
            command.ExecuteNonQuery();
        }

        DatabaseSchemaInitializer.EnsureDutyRosterTables((DbConnection)connection);

        using var check = connection.CreateCommand();
        check.CommandText = """SELECT "PeoplePerDay" || ':' || "UseEventExemptions" || ':' || "Heading" FROM "DutyRosters";""";
        Assert.Equal("3:1:", check.ExecuteScalar());
    }

    [Fact]
    public void DaysRoundTripThroughText()
    {
        var days = new[] { new DateOnly(2026, 10, 1), new DateOnly(2026, 9, 24) };

        var text = DutyRosterService.FormatDays(days);

        Assert.Equal("2026-09-24,2026-10-01", text);
        Assert.Equal(days.OrderBy(d => d), DutyRosterService.ParseDays(text));
        Assert.Null(DutyRosterService.FormatDays(Array.Empty<DateOnly>()));
    }
}
