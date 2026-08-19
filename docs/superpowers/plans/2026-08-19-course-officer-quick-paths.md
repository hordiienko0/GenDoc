# Очима курсового офіцера (варіант А) — план реалізації

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Закрити п'ять вад і шість швидких шляхів зі спеки — «Запуски» знову працюють, падіння логуються, групові шаблони не маскуються під персональні, форма «Вимоги» читається, а курсовий генерує документ на 1–3 людей у два кліки, друкує з архіву й бачить підсумок без MessageBox.

**Architecture:** WPF (.NET 8) + CommunityToolkit.Mvvm, EF Core на SQLite/SQLCipher. Сервіси (`GenDoc/Services`) — чиста логіка з тестами на in-memory SQLite (`TestDb`); в'ю-моделі тонкі; XAML без code-behind-логіки. Нова поведінка генерації будується на вже наявній фазі `RunDocxPhase`, нові колонки — через `DatabaseSchemaInitializer` (v24), нові діалоги — через `DialogService`.

**Tech Stack:** C# 12, WPF, CommunityToolkit.Mvvm 8, EF Core 8 (Sqlite), xUnit, ClosedXML/OpenXML (уже є).

**Spec:** `docs/superpowers/specs/2026-08-19-course-officer-quick-paths-design.md`

## Global Constraints

- Нічого з функціоналу не прибирається — лише ховається, підставляється, спрощується.
- Міграції: нову колонку дописувати в ОБИДВА місця `DatabaseSchemaInitializer` (гілка версії + безумовний хвіст); `NOT NULL` лише з `DEFAULT`; перебудова таблиць — патерн create-copy-drop-rename з `PRAGMA foreign_keys=OFF`, ідемпотентно.
- `.ps1` — лише ASCII; кирилицю передавати з UTF-8-файлу.
- Кожен пункт — окремий коміт; тести запускати `dotnet test GenDoc.Tests\GenDoc.Tests.csproj -c Release` (Debug-теку може тримати запущений застосунок).
- Живий прогін — на робочій базі `GenDoc/bin/Debug/net8.0-windows/gendoc.db` (резервна копія `gendoc.db.bak-2026-08-19` є); скрипти знімків — у scratchpad сесії (`shot.ps1`, `nav.ps1`, `uia-login.ps1`, `screen.ps1`), паролі: БД `12345`, профіль `1234`/`1234`.
- Коміти завершувати рядком `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- Тексти інтерфейсу — українською, без довгого тире «—» (використовувати «-» або «·»).

---

## File Structure

| Файл | Відповідальність |
|---|---|
| `GenDoc/Services/Generation/RunIntakeResolver.cs` (new) | Чиста функція: набір прогону з набору осіб/документів (найчастіший не-null). |
| `GenDoc/Services/Generation/GenerationService.cs` | `RunPackage` пише `IntakeId`; новий `GenerateTemplatesForRecipients`. |
| `GenDoc/Services/Generation/IGenerationService.cs` | Сигнатура нового методу; `RunResult` отримує `RunId` та `Issues`. |
| `GenDoc/Services/DatabaseSchemaInitializer.cs` | Бекфіл `IntakeId` запусків; v24 (`AppSettings.DefaultOutputFolder`, перебудова `GenerationPackageRuns`). |
| `GenDoc/Services/Documents/DocumentArchiveService.cs` | `GetRunsAsync` з запусками без набору; `PrintAsync`/`PrintGroupAsync`. |
| `GenDoc/Services/Documents/SecureTempFileService.cs` | `PrintAsync` (shell-verb `print`). |
| `GenDoc/Services/ErrorLog.cs` (new) | Формат і запис журналу помилок. |
| `GenDoc/App.xaml.cs` | Обробники необроблених винятків. |
| `GenDoc/Services/Completeness/*` | `MatrixTemplateInfo.IsGroup`; групові поза матрицею/статусами; `GetPackageGroupDocumentsAsync`. |
| `GenDoc/Services/Generation/OutputFolderResolver.cs` (new) | Чиста функція типової теки. |
| `GenDoc/Services/Generation/OutputFolderService.cs` (new) | Читання/запис `AppSettings.DefaultOutputFolder`, `ResolveOnDisk`. |
| `GenDoc/Services/ShellCommands.cs` (new) | Аргументи `explorer /select`. |
| `GenDoc/ViewModels/Generation/GenerationResultViewModel.cs` (new) | Картка підсумку прогону. |
| `GenDoc/ViewModels/Personnel/GenerateDocumentsDialogViewModel.cs` (new) + `Views/Personnel/GenerateDocumentsDialog.xaml(.cs)` (new) | Діалог «Згенерувати документ…». |
| `GenDoc/Views/Completeness/PackageRequirementsWindow.xaml` | Розкладка, гліфи, підсумок. |
| `GenDoc.Tests/...` | Тести до кожного пункту (перелічені в задачах). |

---

### Task 1: 1.1 — «Запуски» отримують набір (RunIntakeResolver + бекфіл + фільтр)

**Files:**
- Create: `GenDoc/Services/Generation/RunIntakeResolver.cs`
- Modify: `GenDoc/Services/Generation/GenerationService.cs:355-360`
- Modify: `GenDoc/Services/DatabaseSchemaInitializer.cs` (хвіст після `MigrateGeneratedGroupDocumentsForDocxSupport(db);`, ~рядок 532)
- Modify: `GenDoc/Services/Documents/DocumentArchiveService.cs:643-670`
- Modify: `GenDoc/ViewModels/Archive/RunGroupViewModel.cs`, `GenDoc/Views/Archive/ArchiveView.xaml` (чіп набору в рядку запуску)
- Test: `GenDoc.Tests/Generation/RunIntakeResolverTests.cs` (new), `GenDoc.Tests/Generation/RunPackageTests.cs`, `GenDoc.Tests/DatabaseSchemaInitializerTests.cs`, `GenDoc.Tests/Archive/ArchiveRunsTests.cs` (new)

**Interfaces:**
- Produces: `public static class RunIntakeResolver { public static int? Resolve(IEnumerable<int?> intakeIds); }`
- Produces: `internal static void DatabaseSchemaInitializer.BackfillRunIntakeIds(System.Data.Common.DbConnection connection)`
- Changes: `GetRunsAsync(int? intakeId, int? year)` — при заданому наборі повертає і запуски з `IntakeId IS NULL`.

- [ ] **Step 1: Тест резолвера**

`GenDoc.Tests/Generation/RunIntakeResolverTests.cs`:
```csharp
using GenDoc.Services.Generation;

namespace GenDoc.Tests.Generation;

// Набір прогону виводиться з людей, яким реально генерували: найчастіший
// не-null; усі null (лише постійний склад) - null; нічия - менший Id.
public class RunIntakeResolverTests
{
    [Fact]
    public void Resolve_AllNull_ReturnsNull()
        => Assert.Null(RunIntakeResolver.Resolve(new int?[] { null, null }));

    [Fact]
    public void Resolve_Empty_ReturnsNull()
        => Assert.Null(RunIntakeResolver.Resolve(Array.Empty<int?>()));

    [Fact]
    public void Resolve_MostFrequentWins()
        => Assert.Equal(4, RunIntakeResolver.Resolve(new int?[] { 4, 4, 5, null }));

    [Fact]
    public void Resolve_Tie_PicksSmallerId()
        => Assert.Equal(3, RunIntakeResolver.Resolve(new int?[] { 7, 3 }));
}
```

- [ ] **Step 2: Запустити — має впасти (клас не існує)**

Run: `dotnet test GenDoc.Tests\GenDoc.Tests.csproj -c Release --filter "FullyQualifiedName~RunIntakeResolverTests"`
Expected: build error `RunIntakeResolver` not found.

- [ ] **Step 3: Реалізація резолвера**

`GenDoc/Services/Generation/RunIntakeResolver.cs`:
```csharp
namespace GenDoc.Services.Generation
{
    // Набір, до якого належить прогін: виводиться з людей (або документів), яким
    // реально генерували, - найчастіший не-null IntakeId. Прогін «лише постійний
    // склад» дає null. Те саме правило використовує бекфіл старих запусків.
    public static class RunIntakeResolver
    {
        public static int? Resolve(IEnumerable<int?> intakeIds)
        {
            var counts = new Dictionary<int, int>();
            foreach (var id in intakeIds)
            {
                if (id is int i) counts[i] = counts.GetValueOrDefault(i) + 1;
            }

            if (counts.Count == 0) return null;

            return counts
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key)
                .First().Key;
        }
    }
}
```

- [ ] **Step 4: Тест RunPackage пише IntakeId**

Додати в `GenDoc.Tests/Generation/RunPackageTests.cs` (поруч з іншими `[Fact]`):
```csharp
    // Вада 1.1: запис прогону не мав IntakeId, і вкладка «Запуски», що фільтрує за
    // набором, завжди була порожня.
    [Fact]
    public void RunPackage_RecordsIntakeOfRoster()
    {
        using var db = new TestDb();
        int intakeId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var intake = new Intake { Number = 15, DisplayNumber = "Набір №15" };
            ctx.Intakes.Add(intake);
            ctx.SaveChanges();
            intakeId = intake.Id;
        }
        var people = TemplateFixtures.Roster(2);
        foreach (var p in people) p.IntakeId = intakeId;
        var (packageId, _) = SeedPackage(db, people);

        Run(db, packageId);

        using var check = db.Factory.CreateDbContext();
        var run = check.GenerationPackageRuns.Single();
        Assert.Equal(intakeId, run.IntakeId);
    }

    [Fact]
    public void RunPackage_PermanentStaffOnly_RecordsNullIntake()
    {
        using var db = new TestDb();
        var (packageId, _) = SeedPackage(db, TemplateFixtures.Roster(2)); // без IntakeId = постійний склад

        Run(db, packageId, selection: RosterSelection.Everyone with { PermanentStaffOnly = true });

        using var check = db.Factory.CreateDbContext();
        Assert.Null(check.GenerationPackageRuns.Single().IntakeId);
    }
```

- [ ] **Step 5: Запустити — перший тест падає (IntakeId null)**

Run: `dotnet test GenDoc.Tests\GenDoc.Tests.csproj -c Release --filter "FullyQualifiedName~RunPackage_RecordsIntakeOfRoster"`
Expected: FAIL `Assert.Equal() Failure: Expected 1, Actual (null)`.

- [ ] **Step 6: RunPackage заповнює IntakeId**

У `GenerationService.RunPackage` замінити створення запису:
```csharp
            var run = new GenerationPackageRun
            {
                GenerationPackageId = packageId,
                RunAt = DateTime.Now,
                RunByUserId = _currentUserContext.CurrentUserId ?? 0,
                // Вада 1.1: без IntakeId вкладка «Запуски» (фільтр за набором) була порожня.
                IntakeId = RunIntakeResolver.Resolve(recipients.Select(r => r.IntakeId))
            };
```
(`recipients` уже завантажені рядком вище через `LoadRosterRecipients`.)

- [ ] **Step 7: Тести зелені**

Run: `dotnet test GenDoc.Tests\GenDoc.Tests.csproj -c Release --filter "FullyQualifiedName~RunPackageTests|FullyQualifiedName~RunIntakeResolverTests"`
Expected: PASS.

- [ ] **Step 8: Тест бекфілу на сирому SQLite**

Додати в `GenDoc.Tests/DatabaseSchemaInitializerTests.cs`:
```csharp
    // Вада 1.1, старі дані: запуски до виправлення мають IntakeId = NULL. Бекфіл
    // бере найчастіший IntakeId серед документів прогону; без документів лишає NULL.
    [Fact]
    public void BackfillRunIntakeIds_TakesMostFrequentIntakeOfRunDocuments()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        Exec(connection, """
            CREATE TABLE "GenerationPackageRuns" ("Id" INTEGER PRIMARY KEY, "IntakeId" INTEGER NULL);
            CREATE TABLE "GeneratedDocuments" ("Id" INTEGER PRIMARY KEY, "RunId" INTEGER NULL, "IntakeId" INTEGER NULL);
            CREATE TABLE "GeneratedGroupDocuments" ("Id" INTEGER PRIMARY KEY, "RunId" INTEGER NULL, "IntakeId" INTEGER NULL);
            INSERT INTO "GenerationPackageRuns" VALUES (1, NULL), (2, NULL), (3, 9);
            INSERT INTO "GeneratedDocuments" VALUES (1, 1, 4), (2, 1, 4), (3, 1, 5), (4, 3, 2);
            INSERT INTO "GeneratedGroupDocuments" VALUES (1, 1, 5);
            """);

        DatabaseSchemaInitializer.BackfillRunIntakeIds((DbConnection)connection);
        DatabaseSchemaInitializer.BackfillRunIntakeIds((DbConnection)connection); // ідемпотентно

        Assert.Equal(4L, Scalar(connection, """SELECT "IntakeId" FROM "GenerationPackageRuns" WHERE "Id" = 1"""));
        Assert.Equal(DBNull.Value, Scalar(connection, """SELECT "IntakeId" FROM "GenerationPackageRuns" WHERE "Id" = 2"""));
        Assert.Equal(9L, Scalar(connection, """SELECT "IntakeId" FROM "GenerationPackageRuns" WHERE "Id" = 3""")); // вже заповнений - не чіпати
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static object Scalar(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar()!;
    }
```
(Якщо у файлі вже є помічники з такими іменами — перевикористати їх, не дублювати.)

- [ ] **Step 9: Запустити — падає (метод не існує)**

Run: `dotnet test GenDoc.Tests\GenDoc.Tests.csproj -c Release --filter "FullyQualifiedName~BackfillRunIntakeIds"`
Expected: build error.

- [ ] **Step 10: Реалізація бекфілу**

У `DatabaseSchemaInitializer` додати (поруч із `MigrateGeneratedGroupDocumentsForDocxSupport`):
```csharp
    private static void BackfillRunIntakeIds(AppDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) connection.Open();
        try { BackfillRunIntakeIds(connection); }
        finally { if (wasClosed) connection.Close(); }
    }

    // Вада 1.1: запуски до виправлення мають IntakeId = NULL, і «Запуски» їх не показували.
    // Те саме правило, що й RunIntakeResolver: найчастіший IntakeId серед документів прогону
    // (персональних і групових); без документів - лишається NULL. Ідемпотентно: чіпає лише NULL.
    internal static void BackfillRunIntakeIds(System.Data.Common.DbConnection connection)
    {
        if (!TableExists(connection, "GenerationPackageRuns")) return;
        if (!GetExistingColumns(connection, "GenerationPackageRuns").Contains("IntakeId")) return;
        var hasGroup = TableExists(connection, "GeneratedGroupDocuments")
            && GetExistingColumns(connection, "GeneratedGroupDocuments").Contains("RunId");

        var union = hasGroup
            ? """
              SELECT "IntakeId" FROM "GeneratedDocuments" WHERE "RunId" = "GenerationPackageRuns"."Id" AND "IntakeId" IS NOT NULL
              UNION ALL
              SELECT "IntakeId" FROM "GeneratedGroupDocuments" WHERE "RunId" = "GenerationPackageRuns"."Id" AND "IntakeId" IS NOT NULL
              """
            : """SELECT "IntakeId" FROM "GeneratedDocuments" WHERE "RunId" = "GenerationPackageRuns"."Id" AND "IntakeId" IS NOT NULL""";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            UPDATE "GenerationPackageRuns" SET "IntakeId" = (
                SELECT "IntakeId" FROM ({union}) GROUP BY "IntakeId" ORDER BY COUNT(*) DESC, "IntakeId" ASC LIMIT 1)
            WHERE "IntakeId" IS NULL
              AND EXISTS (SELECT 1 FROM ({union}));
            """;
        cmd.ExecuteNonQuery();
    }
```
У безумовному хвості `EnsureSchema` (після `MigrateGeneratedGroupDocumentsForDocxSupport(db);`) додати рядок `BackfillRunIntakeIds(db);`.

Перевірити, що `TableExists(DbConnection, string)` існує (використовується в `MigrateGeneratedGroupDocumentsForDocxSupport`); якщо приймає лише `AppDbContext` — додати перевантаження на з'єднанні за зразком `GetExistingColumns`.

- [ ] **Step 11: Тест бекфілу зелений**

Run: `dotnet test GenDoc.Tests\GenDoc.Tests.csproj -c Release --filter "FullyQualifiedName~BackfillRunIntakeIds"`
Expected: PASS.

- [ ] **Step 12: Тест фільтра «Запусків»**

`GenDoc.Tests/Archive/ArchiveRunsTests.cs`:
```csharp
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
```

- [ ] **Step 13: Запустити — падає (повертає 1)**

Run: `dotnet test GenDoc.Tests\GenDoc.Tests.csproj -c Release --filter "FullyQualifiedName~ArchiveRunsTests"`
Expected: FAIL `Expected 2, Actual 1`.

- [ ] **Step 14: Фільтр + підпис «Постійний склад»**

У `DocumentArchiveService.GetRunsAsync` замінити умову:
```csharp
            // Запуски без набору (постійний склад) показуємо разом із набором - інакше їх не видно ніде.
            if (intakeId is int i) query = query.Where(r => r.IntakeId == i || r.IntakeId == null);
```
У `RunGroupViewModel`:
```csharp
        public string IntakeChip => Dto.IntakeNumber is int n ? $"Набір №{n}" : "Постійний склад";
```
і в `ArchiveView.xaml` (рядок запуску, ~рядок 454) прибрати `Visibility="{Binding HasIntake, ...}"` з `Border` чіпа — чіп показується завжди. Властивість `HasIntake` у в'ю-моделі лишити (нічого не ламає).

- [ ] **Step 15: Усі тести + живий прогін**

Run: `dotnet test GenDoc.Tests\GenDoc.Tests.csproj -c Release`
Expected: усі PASS.
Живий прогін: зібрати Debug (`dotnet build GenDoc\GenDoc.csproj`), запустити, увійти, «Архів → Запуски» з «Набір №4»: видно запуск «орири 12.08.2026» (бекфіл) — зняти знімок.

- [ ] **Step 16: Коміт**

```bash
git add -A GenDoc GenDoc.Tests
git commit -m "Вада 1.1: запуски отримують набір - RunIntakeResolver, бекфіл, «Запуски» знову показують прогони

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: 1.3 — журнал помилок і обробник необроблених винятків

**Files:**
- Create: `GenDoc/Services/ErrorLog.cs`
- Modify: `GenDoc/App.xaml.cs:40-48`
- Test: `GenDoc.Tests/ErrorLogTests.cs` (new)

**Interfaces:**
- Produces: `public static class ErrorLog { static string DefaultDirectory {get;} static string FilePathFor(string directory, DateTime at); static string Format(DateTime at, string? user, Exception ex); static string Write(Exception ex, string? user, string? directory = null); }`

- [ ] **Step 1: Тести**

`GenDoc.Tests/ErrorLogTests.cs`:
```csharp
using GenDoc.Services;

namespace GenDoc.Tests;

// Вада 1.3: застосунок зникав без сліду. Журнал - один файл на день, запис із
// часом, користувачем, типом, повідомленням, стеком і внутрішніми винятками.
public class ErrorLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"gendoc-log-{Guid.NewGuid():N}");
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public void FilePathFor_OneFilePerDay()
        => Assert.Equal(Path.Combine(_dir, "error-2026-08-19.log"), ErrorLog.FilePathFor(_dir, new DateTime(2026, 8, 19, 14, 5, 0)));

    [Fact]
    public void Format_ContainsTypeMessageStackAndInner()
    {
        Exception ex;
        try { throw new InvalidOperationException("зовнішній", new ArgumentException("внутрішній")); }
        catch (Exception e) { ex = e; }

        var text = ErrorLog.Format(new DateTime(2026, 8, 19, 14, 5, 0), "1234", ex);

        Assert.Contains("2026-08-19 14:05:00", text);
        Assert.Contains("користувач: 1234", text);
        Assert.Contains("InvalidOperationException", text);
        Assert.Contains("зовнішній", text);
        Assert.Contains("ArgumentException", text);
        Assert.Contains("внутрішній", text);
        Assert.Contains(nameof(Format_ContainsTypeMessageStackAndInner), text); // стек
    }

    [Fact]
    public void Write_CreatesDirectoryAndAppendsToDailyFile()
    {
        var first = ErrorLog.Write(new Exception("раз"), "1234", _dir);
        var second = ErrorLog.Write(new Exception("два"), null, _dir);

        Assert.Equal(first, second);
        var text = File.ReadAllText(first);
        Assert.Contains("раз", text);
        Assert.Contains("два", text);
        Assert.Contains("користувач: -", text);
    }
}
```

- [ ] **Step 2: Запустити — падає (клас не існує)**

Run: `dotnet test GenDoc.Tests\GenDoc.Tests.csproj -c Release --filter "FullyQualifiedName~ErrorLogTests"`
Expected: build error.

- [ ] **Step 3: Реалізація**

`GenDoc/Services/ErrorLog.cs`:
```csharp
using System.IO;
using System.Text;

namespace GenDoc.Services
{
    // Журнал необроблених помилок: %LOCALAPPDATA%\GenDoc\logs\error-YYYY-MM-DD.log.
    // Чисті Format/FilePathFor покриті тестами; Write - тонка обгортка.
    public static class ErrorLog
    {
        public static string DefaultDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GenDoc", "logs");

        public static string FilePathFor(string directory, DateTime at)
            => Path.Combine(directory, $"error-{at:yyyy-MM-dd}.log");

        public static string Format(DateTime at, string? user, Exception ex)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== " + at.ToString("yyyy-MM-dd HH:mm:ss") + " · користувач: " + (string.IsNullOrWhiteSpace(user) ? "-" : user));
            var current = ex;
            var depth = 0;
            while (current is not null)
            {
                var indent = new string(' ', depth * 2);
                sb.AppendLine($"{indent}{current.GetType().FullName}: {current.Message}");
                if (!string.IsNullOrWhiteSpace(current.StackTrace))
                    sb.AppendLine(indent + current.StackTrace.Replace("\n", "\n" + indent));
                current = current.InnerException;
                depth++;
            }
            sb.AppendLine();
            return sb.ToString();
        }

        // Повертає шлях до файлу, в який записано. Ніколи не кидає: якщо не вдалося
        // записати - повертає шлях, а помилку ковтає (ми вже всередині обробника помилки).
        public static string Write(Exception ex, string? user, string? directory = null)
        {
            var dir = directory ?? DefaultDirectory;
            var now = DateTime.Now;
            var path = FilePathFor(dir, now);
            try
            {
                Directory.CreateDirectory(dir);
                File.AppendAllText(path, Format(now, user, ex), Encoding.UTF8);
            }
            catch { }
            return path;
        }
    }
}
```

- [ ] **Step 4: Тести зелені**

Run: `dotnet test GenDoc.Tests\GenDoc.Tests.csproj -c Release --filter "FullyQualifiedName~ErrorLogTests"`
Expected: PASS.

- [ ] **Step 5: Підписки в App**

У `App.xaml.cs` `OnStartup` одразу після `base.OnStartup(e);`:
```csharp
            // Вада 1.3: без обробника застосунок зникав мовчки. Диспетчерські помилки
            // логуємо й показуємо, не завершуючи роботу; решту - лише логуємо.
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception ex) ErrorLog.Write(ex, CurrentUserName());
            };
            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                ErrorLog.Write(args.Exception, CurrentUserName());
                args.SetObserved();
            };
```
і методи в класі `App`:
```csharp
        private static string? CurrentUserName()
        {
            try { return Services?.GetService<ICurrentUserContext>()?.CurrentUserFullName; }
            catch { return null; }
        }

        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            var path = ErrorLog.Write(e.Exception, CurrentUserName());
            e.Handled = true;

            var answer = MessageBox.Show(
                "Сталася помилка. Застосунок продовжує роботу.\n\n" +
                $"{e.Exception.Message}\n\nДеталі збережено в\n{path}\n\nВідкрити теку журналів?",
                "GenDoc - помилка", MessageBoxButton.YesNo, MessageBoxImage.Error);
            if (answer == MessageBoxResult.Yes)
            {
                try { System.Diagnostics.Process.Start("explorer.exe", $"\"{System.IO.Path.GetDirectoryName(path)}\""); }
                catch { }
            }
        }
```
(`Services` оголошено `= null!` — перевірка `Services?.` захищає від виклику до `BuildServiceProvider`.)

- [ ] **Step 6: Збірка + усі тести**

Run: `dotnet build GenDoc\GenDoc.csproj && dotnet test GenDoc.Tests\GenDoc.Tests.csproj -c Release`
Expected: 0 errors, усі PASS.

- [ ] **Step 7: Коміт**

```bash
git add -A GenDoc GenDoc.Tests
git commit -m "Вада 1.3: журнал помилок і обробник необроблених винятків - застосунок більше не зникає мовчки

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: 1.4 — групові шаблони поза персональними статусами; підвал картки

**Files:**
- Modify: `GenDoc/Services/Completeness/ICompletenessService.cs` (MatrixTemplateInfo, новий рекорд і метод)
- Modify: `GenDoc/Services/Completeness/CompletenessService.cs:76-122, 337-351, 405-420`
- Modify: `GenDoc/ViewModels/Personnel/PersonCardViewModel.cs:148-193`
- Modify: `GenDoc/Views/Personnel/PersonnelView.xaml` (підвал вкладки «Документи», після `DocumentsEmptyNote`)
- Test: `GenDoc.Tests/Completeness/GroupTemplatesOutsideMatrixTests.cs` (new)

**Interfaces:**
- Changes: `public record MatrixTemplateInfo(int LinkId, int TemplateId, string Name, string? ShortName, int SortOrder, TemplateRequirement RequirementRegular, TemplateRequirement RequirementLimited, bool IsGroup = false);`
- Produces: `public record PackageGroupDocumentStatus(int TemplateId, string TemplateName, int? GroupDocumentId, int Version);`
- Produces: `Task<List<PackageGroupDocumentStatus>> ICompletenessService.GetPackageGroupDocumentsAsync(int packageId, int? intakeId);`
- Changes: `GetPackageLinksAsync(packageId)` повертає всі зв'язки з `IsGroup`; `BuildAsync` і `GetRecipientStatusAsync` — лише `IsGroup == false`.

- [ ] **Step 1: Тести**

`GenDoc.Tests/Completeness/GroupTemplatesOutsideMatrixTests.cs`:
```csharp
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Completeness;

// Вада 1.4: груповий шаблон (один документ на весь склад) показувався в картці особи
// як персональний «Немає · Згенерувати» і давав порожню колонку в матриці.
public class GroupTemplatesOutsideMatrixTests
{
    private static (int PackageId, int IntakeId, int PersonId, int GroupTemplateId) Seed(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
        var intake = new Intake { Number = 1, DisplayNumber = "Набір №1" };
        ctx.Intakes.Add(intake);
        var person = TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас");
        ctx.Recipients.Add(person);
        var personal = new Template { Name = "Рапорт ІНДИВІДУАЛЬНИЙ", OriginalFileName = "a.docx", Content = new byte[] { 1 }, UploadedAt = DateTime.Now, Kind = TemplateKind.PerRecipient };
        var group = new Template { Name = "Рапорт ГРУПОВИЙ", OriginalFileName = "b.docx", Content = new byte[] { 1 }, UploadedAt = DateTime.Now, Kind = TemplateKind.Group };
        ctx.Templates.AddRange(personal, group);
        ctx.SaveChanges();
        person.IntakeId = intake.Id;

        var package = new GenerationPackage { Name = "П" };
        package.Templates.Add(new GenerationPackageTemplate { TemplateId = personal.Id, SortOrder = 0 });
        package.Templates.Add(new GenerationPackageTemplate { TemplateId = group.Id, SortOrder = 1 });
        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();

        ctx.GeneratedGroupDocuments.Add(new GeneratedGroupDocument
        {
            TemplateId = group.Id, IntakeId = intake.Id, GeneratedAt = DateTime.Now, GeneratedByUserId = 1,
            FileName = "group.docx", Version = 8, IsCurrent = true, HasContent = true, RecipientCount = 3
        });
        ctx.SaveChanges();
        return (package.Id, intake.Id, person.Id, group.Id);
    }

    [Fact]
    public async Task Matrix_ExcludesGroupTemplates()
    {
        using var db = new TestDb();
        var (packageId, intakeId, _, _) = Seed(db);

        var data = await TestServices.Completeness(db).BuildAsync(intakeId, packageId);

        Assert.Single(data.Templates);
        Assert.Equal("Рапорт ІНДИВІДУАЛЬНИЙ", data.Templates[0].Name);
    }

    [Fact]
    public async Task RecipientStatus_ExcludesGroupTemplates()
    {
        using var db = new TestDb();
        var (packageId, _, personId, _) = Seed(db);

        var statuses = await TestServices.Completeness(db).GetRecipientStatusAsync(personId, packageId);

        Assert.Single(statuses);
        Assert.Equal("Рапорт ІНДИВІДУАЛЬНИЙ", statuses[0].TemplateName);
    }

    [Fact]
    public async Task PackageLinks_KeepGroupTemplatesWithFlag()
    {
        using var db = new TestDb();
        var (packageId, _, _, groupTemplateId) = Seed(db);

        var links = await TestServices.Completeness(db).GetPackageLinksAsync(packageId);

        Assert.Equal(2, links.Count);
        Assert.True(links.Single(l => l.TemplateId == groupTemplateId).IsGroup);
        Assert.False(links.Single(l => l.TemplateId != groupTemplateId).IsGroup);
    }

    [Fact]
    public async Task PackageGroupDocuments_ReturnCurrentVersionForIntake()
    {
        using var db = new TestDb();
        var (packageId, intakeId, _, groupTemplateId) = Seed(db);

        var docs = await TestServices.Completeness(db).GetPackageGroupDocumentsAsync(packageId, intakeId);

        var doc = Assert.Single(docs);
        Assert.Equal(groupTemplateId, doc.TemplateId);
        Assert.Equal(8, doc.Version);
        Assert.NotNull(doc.GroupDocumentId);
    }
}
```

- [ ] **Step 2: Запустити — падає**

Run: `dotnet test GenDoc.Tests\GenDoc.Tests.csproj -c Release --filter "FullyQualifiedName~GroupTemplatesOutsideMatrixTests"`
Expected: build error (`IsGroup`, `GetPackageGroupDocumentsAsync` не існують).

- [ ] **Step 3: Інтерфейс**

У `ICompletenessService.cs`:
```csharp
    public record MatrixTemplateInfo(
        int LinkId, int TemplateId, string Name, string? ShortName, int SortOrder,
        TemplateRequirement RequirementRegular, TemplateRequirement RequirementLimited,
        bool IsGroup = false);

    // Групові відомості пакета (один документ на весь склад) - показуються в підвалі
    // картки особи, а не серед її персональних документів.
    public record PackageGroupDocumentStatus(int TemplateId, string TemplateName, int? GroupDocumentId, int Version);
```
і в інтерфейс: `Task<List<PackageGroupDocumentStatus>> GetPackageGroupDocumentsAsync(int packageId, int? intakeId);`

- [ ] **Step 4: Сервіс**

У `CompletenessService`:
```csharp
        private static async Task<List<MatrixTemplateInfo>> GetPackageLinksInternalAsync(AppDbContext db, int packageId, bool includeGroup)
        {
            var query = db.GenerationPackageTemplates
                .Where(pt => pt.GenerationPackageId == packageId && pt.Template != null && pt.Template.DeletedAt == null);
            // Вада 1.4: груповий шаблон у персональній матриці давав порожню колонку в кожного.
            if (!includeGroup) query = query.Where(pt => pt.Template!.Kind != TemplateKind.Group);

            return await query
                .OrderBy(pt => pt.SortOrder)
                .Select(pt => new MatrixTemplateInfo(
                    pt.Id, pt.TemplateId, pt.Template!.Name, pt.Template.ShortName,
                    pt.SortOrder, pt.RequirementRegular, pt.RequirementLimited,
                    pt.Template.Kind == TemplateKind.Group))
                .ToListAsync();
        }
```
- `LoadAsync` викликає `GetPackageLinksInternalAsync(db, packageId, includeGroup: false)`.
- `GetRecipientStatusAsync`: замість `GetPackageLinksAsync(packageId)` — `using var db = _dbFactory.CreateDbContext(); var templates = await GetPackageLinksInternalAsync(db, packageId, includeGroup: false);` (решта без змін).
- `GetPackageLinksAsync` (вимоги) — `includeGroup: true`.
- Новий метод:
```csharp
        public async Task<List<PackageGroupDocumentStatus>> GetPackageGroupDocumentsAsync(int packageId, int? intakeId)
        {
            using var db = _dbFactory.CreateDbContext();
            var groupTemplates = (await GetPackageLinksInternalAsync(db, packageId, includeGroup: true))
                .Where(t => t.IsGroup).ToList();
            if (groupTemplates.Count == 0) return new List<PackageGroupDocumentStatus>();

            var ids = groupTemplates.Select(t => t.TemplateId).ToList();
            var docs = await db.GeneratedGroupDocuments
                .Where(g => g.TemplateId != null && ids.Contains(g.TemplateId.Value) && g.IsCurrent && g.IntakeId == intakeId)
                .Select(g => new { g.TemplateId, g.Id, g.Version })
                .ToListAsync();

            return groupTemplates.Select(t =>
            {
                var doc = docs.FirstOrDefault(d => d.TemplateId == t.TemplateId);
                return new PackageGroupDocumentStatus(t.TemplateId, t.Name, doc?.Id, doc?.Version ?? 0);
            }).ToList();
        }
```
Додати `using GenDoc.Models.Enums;` за потреби. Перевірити, що `GeneratedGroupDocument.TemplateId` — `int?` (так, після v12).

- [ ] **Step 5: Тести зелені**

Run: `dotnet test GenDoc.Tests\GenDoc.Tests.csproj -c Release --filter "FullyQualifiedName~GroupTemplatesOutsideMatrixTests|FullyQualifiedName~Completeness|FullyQualifiedName~RequirementTemplateRow"`
Expected: PASS (наявні тести комплектності теж).

- [ ] **Step 6: Підвал картки**

`PersonCardViewModel`:
```csharp
        public ObservableCollection<GroupDocumentRowViewModel> GroupDocumentRows { get; } = new();
        [ObservableProperty] private bool hasGroupDocuments;
```
у `RefreshDocumentsAsync` після `DocumentsFooterNote = ...`:
```csharp
                var groupDocs = await _completenessService.GetPackageGroupDocumentsAsync(packageId.Value, IntakeId);
                GroupDocumentRows.Clear();
                foreach (var g in groupDocs) GroupDocumentRows.Add(new GroupDocumentRowViewModel(g));
                HasGroupDocuments = GroupDocumentRows.Count > 0;
```
команда:
```csharp
        [RelayCommand]
        private async Task OpenGroupDocumentAsync(GroupDocumentRowViewModel? row)
        {
            if (row?.GroupDocumentId is not int id) return;
            var result = await _archiveService.OpenGroupAsync(id);
            if (!result.Success)
                MessageBox.Show(result.ErrorMessage, "Відкриття відомості", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
```
Новий файл `GenDoc/ViewModels/Personnel/GroupDocumentRowViewModel.cs`:
```csharp
using GenDoc.Services.Completeness;

namespace GenDoc.ViewModels.Personnel
{
    // Рядок підвалу «Групові відомості пакета» у картці особи.
    public class GroupDocumentRowViewModel
    {
        public GroupDocumentRowViewModel(PackageGroupDocumentStatus status)
        {
            TemplateName = status.TemplateName;
            GroupDocumentId = status.GroupDocumentId;
            StateText = status.GroupDocumentId is null ? "ще не сформовано" : $"є, в.{status.Version}";
        }

        public string TemplateName { get; }
        public int? GroupDocumentId { get; }
        public string StateText { get; }
        public bool CanOpen => GroupDocumentId is not null;
    }
}
```
`PersonnelView.xaml`, після `TextBlock` з `DocumentsEmptyNote` і перед `DocumentsFooterNote`:
```xml
                            <StackPanel Margin="0,14,0,0"
                                        Visibility="{Binding HasGroupDocuments, Converter={StaticResource BoolToVisibility}}">
                                <TextBlock Text="ГРУПОВІ ВІДОМОСТІ ПАКЕТА" FontSize="10.5" FontWeight="Bold"
                                           Foreground="{StaticResource TextSecondaryBrush}" Margin="0,0,0,4"/>
                                <TextBlock Text="Формуються на весь склад на екрані «Генерація», не для однієї особи."
                                           FontSize="11" TextWrapping="Wrap" Foreground="{StaticResource TextSecondaryBrush}" Margin="0,0,0,6"/>
                                <ItemsControl ItemsSource="{Binding GroupDocumentRows}">
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate>
                                            <StackPanel Orientation="Horizontal" Margin="0,2">
                                                <TextBlock Text="{Binding TemplateName}" FontSize="12" Foreground="{StaticResource TextPrimaryBrush}"/>
                                                <TextBlock Text="{Binding StateText, StringFormat=' · {0}'}" FontSize="12" Foreground="{StaticResource TextSecondaryBrush}"/>
                                                <TextBlock Text=" · Відкрити" FontSize="12" FontWeight="Bold" Cursor="Hand"
                                                           Foreground="{StaticResource AccentBrush}"
                                                           Visibility="{Binding CanOpen, Converter={StaticResource BoolToVisibility}}">
                                                    <TextBlock.InputBindings>
                                                        <MouseBinding MouseAction="LeftClick"
                                                                      Command="{Binding DataContext.OpenGroupDocumentCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                                                      CommandParameter="{Binding}"/>
                                                    </TextBlock.InputBindings>
                                                </TextBlock>
                                            </StackPanel>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </StackPanel>
```

- [ ] **Step 7: Збірка, тести, живий прогін**

Run: `dotnet build GenDoc\GenDoc.csproj && dotnet test GenDoc.Tests\GenDoc.Tests.csproj -c Release`
Живий прогін: картка БЕНІШ → «Документи»: лише «Рапорт котлове ІНДИВІДУАЛЬНИЙ», нижче підвал «Групові відомості пакета: Рапорт котлове ГРУПОВИЙ (3) · є, в.8 · Відкрити»; «Комплектність»: одна колонка, без «(-)». Знімки.

- [ ] **Step 8: Коміт**

```bash
git add -A GenDoc GenDoc.Tests
git commit -m "Вада 1.4: групові шаблони поза персональними статусами; підвал «Групові відомості пакета» в картці

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: 1.5 — форма «Вимоги пакета»: гліфи, розкладка, підсумок, груповий рядок

**Files:**
- Modify: `GenDoc/Views/Completeness/PackageRequirementsWindow.xaml`
- Modify: `GenDoc/ViewModels/Completeness/PackageRequirementsViewModel.cs`
- Test: `GenDoc.Tests/IconGlyphScopeTests.cs` (new), `GenDoc.Tests/RequirementTemplateRowViewModelTests.cs`, `GenDoc.Tests/Completeness/RequirementsSummaryTests.cs` (new)

**Interfaces:**
- Produces: `public static string PackageRequirementsViewModel.BuildPreviewText(int intakeNumber, int requiredRegular, int optionalRegular, int requiredLimited, int optionalLimited)` (internal static, покрито тестом)
- Changes: `RequirementTemplateRowViewModel.IsGroup` (bool); групові рядки не беруть участі у `Validate`/підсумку.

- [ ] **Step 1: Тест-сканер гліфів**

`GenDoc.Tests/IconGlyphScopeTests.cs`:
```csharp
using System.Text.RegularExpressions;

namespace GenDoc.Tests;

/// <summary>
/// RowIconButtonStyle примушує шрифт Segoe MDL2 Assets. У ньому немає звичайних
/// символів («▲», «✕», літери) - такий вміст малюється порожнім квадратом. Саме так
/// зникли кнопки «вгору/вниз/прибрати» у вікні «Вимоги пакета» (вада 1.5).
/// Тест текстовий, як StaticResourceScopeTests: вміст кнопки з цим стилем має бути
/// посиланням на символ Private Use Area (&#xE000;-&#xF8FF;).
/// </summary>
public class IconGlyphScopeTests
{
    private static readonly Regex ButtonTag = new(@"<Button\b[^>]*?/?>", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex ContentAttr = new(@"\bContent=""([^""]*)""", RegexOptions.Compiled);
    private static readonly Regex PuaRef = new(@"^&#x[EeFf][0-9A-Fa-f]{3};$", RegexOptions.Compiled);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "GenDoc", "Views")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Не знайдено корінь репозиторію");
    }

    [Fact]
    public void ButtonsWithMdl2IconStyleUsePrivateUseAreaGlyphs()
    {
        var viewsDir = Path.Combine(RepoRoot(), "GenDoc", "Views");
        var offenders = new List<string>();

        foreach (var file in Directory.GetFiles(viewsDir, "*.xaml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (Match tag in ButtonTag.Matches(text))
            {
                if (!tag.Value.Contains("RowIconButtonStyle", StringComparison.Ordinal)) continue;
                var content = ContentAttr.Match(tag.Value);
                if (!content.Success) continue; // вміст задано вкладено - не цей випадок
                var value = content.Groups[1].Value;
                var isPua = PuaRef.IsMatch(value) || (value.Length == 1 && value[0] >= '\uE000' && value[0] <= '\uF8FF');
                if (!isPua) offenders.Add($"{Path.GetRelativePath(viewsDir, file)}: Content=\"{value}\"");
            }
        }

        Assert.True(offenders.Count == 0,
            "Кнопки зі стилем RowIconButtonStyle (шрифт Segoe MDL2 Assets) мають текстовий вміст, який цей шрифт не малює:\n"
            + string.Join("\n", offenders));
    }
}
```

- [ ] **Step 2: Запустити — падає з чотирма порушниками**

Run: `dotnet test GenDoc.Tests\GenDoc.Tests.csproj -c Release --filter "FullyQualifiedName~IconGlyphScopeTests"`
Expected: FAIL, у повідомленні `PackageRequirementsWindow.xaml: Content="▲"`, `"▼"`, `"✕"` ×2.

- [ ] **Step 3: Гліфи**

У `PackageRequirementsWindow.xaml` замінити чотири кнопки:
- `Content="▲"` → `Content="&#xE70E;" ToolTip="Вище"`
- `Content="▼"` → `Content="&#xE70D;" ToolTip="Нижче"`
- обидва `Content="✕"` → `Content="&#xE711;" ToolTip="Прибрати з пакета"`

Run: тест зелений.

- [ ] **Step 4: Тест підсумку й групового рядка**

`GenDoc.Tests/Completeness/RequirementsSummaryTests.cs`:
```csharp
using GenDoc.Models.Enums;
using GenDoc.Services.Completeness;
using GenDoc.ViewModels.Completeness;

namespace GenDoc.Tests.Completeness;

public class RequirementsSummaryTests
{
    [Fact]
    public void BuildPreviewText_ReadsAsSentence()
    {
        var text = PackageRequirementsViewModel.BuildPreviewText(4, requiredRegular: 1, optionalRegular: 1, requiredLimited: 2, optionalLimited: 0);
        Assert.Equal("Для набору №4: звичайні - 1 обов'язковий, 1 опційний · обмежено придатні - 2 обов'язкових, 0 опційних", text);
    }

    [Fact]
    public void GroupRow_HasNoRequirementAndIsFlagged()
    {
        var row = new RequirementTemplateRowViewModel(
            new MatrixTemplateInfo(3, 42, "Рапорт ГРУПОВИЙ", null, 0, TemplateRequirement.Required, TemplateRequirement.Required, IsGroup: true),
            hasDocuments: false);

        Assert.True(row.IsGroup);
        Assert.Equal(TemplateRequirement.NotApplicable, row.Regular);
        Assert.Equal(TemplateRequirement.NotApplicable, row.Limited);
    }
}
```
Відмінювання: `Plural(n, "обов'язковий", "обов'язкових")` — 1 → однина, інакше множина (2–4 теж «обов'язкових» у цьому реченні читається нормально: «2 обов'язкових»).

- [ ] **Step 5: Запустити — падає (методу й властивості немає)**

- [ ] **Step 6: В'ю-модель**

`RequirementTemplateRowViewModel`: додати `public bool IsGroup { get; }`, у конструкторі:
```csharp
            IsGroup = info.IsGroup;
            // Груповий шаблон формує один документ на весь склад - обов'язковість на особу до нього не застосовна.
            regular = info.IsGroup ? TemplateRequirement.NotApplicable : info.RequirementRegular;
            limited = info.IsGroup ? TemplateRequirement.NotApplicable : info.RequirementLimited;
```
`PackageRequirementsViewModel`:
```csharp
        internal static string BuildPreviewText(int intakeNumber, int requiredRegular, int optionalRegular, int requiredLimited, int optionalLimited)
            => $"Для набору №{intakeNumber}: звичайні - {requiredRegular} {Plural(requiredRegular, "обов'язковий", "обов'язкових")}, " +
               $"{optionalRegular} {Plural(optionalRegular, "опційний", "опційних")} · " +
               $"обмежено придатні - {requiredLimited} {Plural(requiredLimited, "обов'язковий", "обов'язкових")}, " +
               $"{optionalLimited} {Plural(optionalLimited, "опційний", "опційних")}";

        private static string Plural(int n, string one, string many) => n == 1 ? one : many;
```
`RefreshPreview` рахує лише `Rows.Where(r => !r.IsGroup)` і викликає `BuildPreviewText(_previewIntakeId.Value, ...)`.
`Validate`: `var personal = Rows.Where(r => !r.IsGroup).ToList();` — якщо `personal.Count == 0` → гілка «нема до чого застосовувати» (як зараз для `Rows.Count == 0`, з тією ж умовою про `ExportRows`); `hasAnyRegular/hasAnyLimited` — по `personal`.
У `SaveRequirementsAsync`-виклику (метод збереження у VM) групові рядки передаються з `NotApplicable` — перевірити, що сервіс це приймає (він пише значення як є).

- [ ] **Step 7: Розкладка вікна**

У `PackageRequirementsWindow.xaml`:
- `Width="620"` → `Width="860"`, `Height="760"` → `Height="720"`, `ResizeMode="NoResize"` → `ResizeMode="CanResize"`, `MinWidth="760"`.
- Колонки шапки й рядка: `ColumnDefinition Width="188"` (обидві) → `Width="258"`; остання `70` → `96`. У `RequirementSegmentStyle` `Padding="0,4"` → `Padding="6,4"`.
- Назва шаблону: додати `ToolTip="{Binding Name}"`.
- Вміст: обгорнути весь `<Grid Margin="16,12">` у `<ScrollViewer VerticalScrollBarVisibility="Auto">`, а в ньому `RowDefinition Height="*"` (рядок 1) → `Height="Auto"`; внутрішній `ScrollViewer` навколо `ItemsControl` прибрати (лишити сам `ItemsControl`). Тепер «+ Додати…» іде одразу під списком.
- Комбобокси додавання: додати атрибути `IsEditable="False"` і підказку через `Tag`/стандартний патерн проєкту для placeholder (знайти, як зроблено в інших ComboBox із підказкою, напр. `ArchiveView.xaml` «Шаблон: усі»; якщо готового патерну немає — перед ComboBox поставити `TextBlock Text="Оберіть шаблон…"` з `IsHitTestVisible="False"`, `Visibility` за `SelectedItem == null` через `DataTrigger`). Кнопки «+ Додати…» — `IsEnabled="{Binding SelectedTemplateToAdd, Converter={StaticResource NullToFalse}}"` (перевірити наявність конвертера; якщо немає — у VM додати `public bool CanAddTemplate => SelectedTemplateToAdd is not null` з `NotifyPropertyChangedFor` і прив'язати до нього; аналогічно для експорту).
- Груповий рядок: у `DataTemplate` для `RequirementTemplateRowViewModel` `UniformGrid`-и отримують `Visibility` прихованою при `IsGroup` (через `Style.Triggers` `DataTrigger Binding="{Binding IsGroup}" Value="True" → Collapsed`), а замість них показується `TextBlock Grid.Column="1" Grid.ColumnSpan="2" Text="груповий - формується на весь склад, обов'язковість на особу не застосовна" FontSize="11" Foreground="{StaticResource TextSecondaryBrush}" VerticalAlignment="Center"` з протилежним тригером.

- [ ] **Step 8: Тести + живий прогін**

Run: `dotnet test GenDoc.Tests\GenDoc.Tests.csproj -c Release`
Живий: «Комплектність → Вимоги»: стрілки й хрестик видно, «Не потрібен» не обрізаний, без порожнього простору, підсумок читається, груповий рядок із позначкою. Знімок повним екраном (`screen.ps1`).

- [ ] **Step 9: Коміт**

```bash
git add -A GenDoc GenDoc.Tests
git commit -m "Вада 1.5: форма «Вимоги пакета» - гліфи MDL2 (+тест-сканер), ширини, підсумок, груповий рядок без вимоги

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: 1.2 — колонки «ВЕР.» і «НАБІР» в «Архіві»

**Files:**
- Modify: `GenDoc/Views/Archive/ArchiveView.xaml:312, 345` (і дзеркальна колонка ~602, якщо вона в тій самій таблиці)

- [ ] **Step 1: Відтворити**

Запустити застосунок, «Архів документів», знімок: колонка «ВЕР.» показує лише «в.», заголовок «НАБІF» обрізано.

- [ ] **Step 2: Діагностика**

Тимчасово задати `Width="72" MinWidth="72"` на «ВЕР.» і `Width="84" MinWidth="84"` на «НАБІР», перезібрати, подивитись. Якщо допомогло — лишити так (пояснення: у DataGrid зі змішаними `*`/фіксованими колонками й `MinWidth` на зіркових фіксована колонка без `MinWidth` може стискатись нижче заданої ширини при нестачі місця). Якщо ні — перевести обидві на `Width="0.5*" MinWidth="72"` / `Width="0.6*" MinWidth="84"` (усі текстові колонки зірками — робоча комбінація зі спеки 17.08).

- [ ] **Step 3: Перевірити наживо**

Знімок: «в.8» (або «в.1») видно цілком, заголовки «ВЕР.» і «НАБІР» цілі, горизонтальний прокрутник не з'явився там, де його не було. Перевірити, що «Групові» й «Запуски» не постраждали.

- [ ] **Step 4: Тести + коміт**

Run: `dotnet test GenDoc.Tests\GenDoc.Tests.csproj -c Release` (регресії розмітки).
```bash
git add -A GenDoc
git commit -m "Вада 1.2: в «Архіві» видно номер версії й заголовок «НАБІР»

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: 2.1 — міграція v24 і тека генерації за замовчуванням

**Files:**
- Create: `GenDoc/Services/Generation/OutputFolderResolver.cs`, `GenDoc/Services/Generation/OutputFolderService.cs`
- Modify: `GenDoc/Models/AppSettings.cs`, `GenDoc/Models/GenerationPackageRun.cs` (`int? GenerationPackageId`)
- Modify: `GenDoc/Services/DatabaseSchemaInitializer.cs` (v24, хвіст, перебудова)
- Modify: `GenDoc/Services/Generation/GenerationService.cs:293-308` (`GetLastRun` — join лишається, компілюється з `int?` через `.Where(r => r.GenerationPackageId != null)` перед Join і `run.GenerationPackageId!.Value`)
- Modify: `GenDoc/Services/Documents/DocumentArchiveService.cs:657` («-» → «Вибірково»)
- Modify: `GenDoc/ViewModels/Generation/GenerationViewModel.cs:94-98, 353, 440-449`, `GenDoc/Views/Generation/GenerationView.xaml:416-421`
- Modify: `GenDoc/App.xaml.cs` (DI)
- Test: `GenDoc.Tests/Generation/OutputFolderResolverTests.cs` (new), `GenDoc.Tests/DatabaseSchemaInitializerTests.cs`, `GenDoc.Tests/Generation/OutputFolderServiceTests.cs` (new)

**Interfaces:**
- Produces: `public static class OutputFolderResolver { public static string Resolve(string? configured, string documentsRoot); public static string Resolve(string? configured); }`
- Produces: `public interface IOutputFolderService { Task<string> GetDefaultAsync(); Task SaveDefaultAsync(string folder); Task<string?> ResolveOnDiskAsync(string relativeFileName); }` + `OutputFolderService : IOutputFolderService` (ctor `IDbContextFactory<AppDbContext>`).
- Produces: `internal static void DatabaseSchemaInitializer.MigrateGenerationPackageRunsForAdHocRuns(DbConnection)`.
- Changes: `GenerationPackageRun.GenerationPackageId` → `int?`.

- [ ] **Step 1: Тест резолвера**

`GenDoc.Tests/Generation/OutputFolderResolverTests.cs`:
```csharp
using GenDoc.Services.Generation;

namespace GenDoc.Tests.Generation;

public class OutputFolderResolverTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_Empty_FallsBackToDocumentsGenDoc(string? configured)
        => Assert.Equal(Path.Combine(@"C:\Users\x\Documents", "GenDoc"), OutputFolderResolver.Resolve(configured, @"C:\Users\x\Documents"));

    [Fact]
    public void Resolve_Configured_ReturnsItTrimmed()
        => Assert.Equal(@"D:\Док", OutputFolderResolver.Resolve(@"  D:\Док  ", @"C:\Users\x\Documents"));
}
```

- [ ] **Step 2: Запустити — падає; реалізація**

`GenDoc/Services/Generation/OutputFolderResolver.cs`:
```csharp
using System.IO;

namespace GenDoc.Services.Generation
{
    // Тека, куди лягають документи, якщо оператор не обрав іншої: налаштування або
    // «Документи\GenDoc». Чиста функція - перевантаження з явним коренем для тестів.
    public static class OutputFolderResolver
    {
        public const string DefaultSubfolder = "GenDoc";

        public static string Resolve(string? configured)
            => Resolve(configured, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

        public static string Resolve(string? configured, string documentsRoot)
            => string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(documentsRoot, DefaultSubfolder)
                : configured.Trim();
    }
}
```
Run: PASS.

- [ ] **Step 3: Тест перебудови `GenerationPackageRuns`**

У `DatabaseSchemaInitializerTests`:
```csharp
    // v24: запуск «Вибірково» (2.2) не має пакета, а колонка була NOT NULL. SQLite не
    // знімає NOT NULL через ALTER - перебудова таблиці; дані й зовнішні ключі документів
    // (RunId → Id) лишаються.
    [Fact]
    public void MigrateGenerationPackageRunsForAdHocRuns_MakesPackageNullableAndKeepsRows()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        Exec(connection, """
            CREATE TABLE "GenerationPackages" ("Id" INTEGER NOT NULL CONSTRAINT "PK_GenerationPackages" PRIMARY KEY AUTOINCREMENT, "Name" TEXT NOT NULL);
            CREATE TABLE "Users" ("Id" INTEGER NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY AUTOINCREMENT, "FullName" TEXT NOT NULL);
            CREATE TABLE "GenerationPackageRuns" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_GenerationPackageRuns" PRIMARY KEY AUTOINCREMENT,
                "GenerationPackageId" INTEGER NOT NULL,
                "RunAt" TEXT NOT NULL,
                "RunByUserId" INTEGER NOT NULL,
                "GeneratedCount" INTEGER NOT NULL,
                "SkippedCount" INTEGER NOT NULL,
                "ErrorCount" INTEGER NOT NULL,
                "Summary" TEXT NULL,
                "IntakeId" INTEGER NULL,
                "BranchName" TEXT NULL,
                CONSTRAINT "FK_GenerationPackageRuns_GenerationPackages_GenerationPackageId" FOREIGN KEY ("GenerationPackageId") REFERENCES "GenerationPackages" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_GenerationPackageRuns_Users_RunByUserId" FOREIGN KEY ("RunByUserId") REFERENCES "Users" ("Id") ON DELETE CASCADE);
            INSERT INTO "GenerationPackages" VALUES (7, 'П'); INSERT INTO "Users" VALUES (1, 'Тест');
            INSERT INTO "GenerationPackageRuns" ("Id","GenerationPackageId","RunAt","RunByUserId","GeneratedCount","SkippedCount","ErrorCount","Summary","IntakeId","BranchName")
            VALUES (1, 7, '2026-08-12 09:35:00', 1, 4, 0, 0, NULL, 4, NULL);
            """);

        DatabaseSchemaInitializer.MigrateGenerationPackageRunsForAdHocRuns((DbConnection)connection);
        DatabaseSchemaInitializer.MigrateGenerationPackageRunsForAdHocRuns((DbConnection)connection); // ідемпотентно

        Assert.Equal(1L, Scalar(connection, """SELECT COUNT(*) FROM "GenerationPackageRuns" WHERE "Id" = 1 AND "GenerationPackageId" = 7 AND "IntakeId" = 4"""));
        Exec(connection, """INSERT INTO "GenerationPackageRuns" ("GenerationPackageId","RunAt","RunByUserId","GeneratedCount","SkippedCount","ErrorCount") VALUES (NULL, '2026-08-19', 1, 0, 0, 0);""");
        Assert.Equal(2L, Scalar(connection, """SELECT COUNT(*) FROM "GenerationPackageRuns";"""));
        Assert.False(DatabaseSchemaInitializer.ColumnIsNotNull((DbConnection)connection, "GenerationPackageRuns", "GenerationPackageId"));
    }
```

- [ ] **Step 4: Запустити — падає; реалізація міграції**

У `DatabaseSchemaInitializer`:
- `CurrentSchemaVersion = 24`;
- `private static readonly (string Name, string Type)[] AppSettingsColumnsV24 = { ("DefaultOutputFolder", "TEXT") };`
- гілка версії після `currentVersion < 23`:
```csharp
            if (currentVersion < 24)
            {
                AddMissingColumns(db, "AppSettings", AppSettingsColumnsV24);
                MigrateGenerationPackageRunsForAdHocRuns(db);

                db.SchemaVersions.Add(new SchemaVersion
                {
                    Version = 24,
                    AppliedAt = DateTime.Now,
                    Description = "Тека генерації за замовчуванням; запуски без пакета (вибіркова генерація)"
                });
                currentVersion = 24;
            }
```
- у хвості після `AddMissingColumns(db, "Templates", TemplateColumnsV23);`: `AddMissingColumns(db, "AppSettings", AppSettingsColumnsV24); MigrateGenerationPackageRunsForAdHocRuns(db);`
- методи:
```csharp
    private static void MigrateGenerationPackageRunsForAdHocRuns(AppDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) connection.Open();
        try { MigrateGenerationPackageRunsForAdHocRuns(connection); }
        finally { if (wasClosed) connection.Close(); }
    }

    internal static bool ColumnIsNotNull(System.Data.Common.DbConnection connection, string table, string column)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(reader.GetOrdinal("name")), column, StringComparison.OrdinalIgnoreCase))
                return reader.GetInt64(reader.GetOrdinal("notnull")) == 1;
        }
        return false;
    }

    // v24: GenerationPackageId стає NULL-able (запуск «Вибірково» без пакета). Патерн той
    // самий, що в MigrateGeneratedGroupDocumentsForDocxSupport. Ознака «вже зроблено» -
    // PRAGMA table_info: notnull = 0.
    internal static void MigrateGenerationPackageRunsForAdHocRuns(System.Data.Common.DbConnection connection)
    {
        if (!TableExists(connection, "GenerationPackageRuns")) return;
        if (!ColumnIsNotNull(connection, "GenerationPackageRuns", "GenerationPackageId")) return;

        using (var pragmaOff = connection.CreateCommand())
        {
            pragmaOff.CommandText = "PRAGMA foreign_keys=OFF;";
            pragmaOff.ExecuteNonQuery();
        }

        using var transaction = connection.BeginTransaction();
        try
        {
            Exec(connection, transaction, """
                CREATE TABLE "GenerationPackageRuns_New" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_GenerationPackageRuns" PRIMARY KEY AUTOINCREMENT,
                    "GenerationPackageId" INTEGER NULL,
                    "RunAt" TEXT NOT NULL,
                    "RunByUserId" INTEGER NOT NULL,
                    "GeneratedCount" INTEGER NOT NULL DEFAULT 0,
                    "SkippedCount" INTEGER NOT NULL DEFAULT 0,
                    "ErrorCount" INTEGER NOT NULL DEFAULT 0,
                    "Summary" TEXT NULL,
                    "IntakeId" INTEGER NULL,
                    "BranchName" TEXT NULL,
                    CONSTRAINT "FK_GenerationPackageRuns_GenerationPackages_GenerationPackageId" FOREIGN KEY ("GenerationPackageId") REFERENCES "GenerationPackages" ("Id") ON DELETE SET NULL,
                    CONSTRAINT "FK_GenerationPackageRuns_Users_RunByUserId" FOREIGN KEY ("RunByUserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
                );
                """);
            Exec(connection, transaction, """
                INSERT INTO "GenerationPackageRuns_New"
                    ("Id","GenerationPackageId","RunAt","RunByUserId","GeneratedCount","SkippedCount","ErrorCount","Summary","IntakeId","BranchName")
                SELECT "Id","GenerationPackageId","RunAt","RunByUserId","GeneratedCount","SkippedCount","ErrorCount","Summary","IntakeId","BranchName"
                FROM "GenerationPackageRuns";
                """);
            Exec(connection, transaction, """DROP TABLE "GenerationPackageRuns";""");
            Exec(connection, transaction, """ALTER TABLE "GenerationPackageRuns_New" RENAME TO "GenerationPackageRuns";""");
            Exec(connection, transaction, """CREATE INDEX "IX_GenerationPackageRuns_GenerationPackageId" ON "GenerationPackageRuns" ("GenerationPackageId");""");
            Exec(connection, transaction, """CREATE INDEX "IX_GenerationPackageRuns_RunByUserId" ON "GenerationPackageRuns" ("RunByUserId");""");
            transaction.Commit();
        }
        catch { transaction.Rollback(); throw; }
        finally
        {
            using var pragmaOn = connection.CreateCommand();
            pragmaOn.CommandText = "PRAGMA foreign_keys=ON;";
            pragmaOn.ExecuteNonQuery();
        }
    }
```
Перевірити наявні імена індексів/FK у реальній базі (`PRAGMA index_list("GenerationPackageRuns")` через тест або `sqlite3` не потрібні — EF за конвенцією створює саме такі; якщо в базі колонки `IntakeId`/`BranchName` відсутні до перебудови (дуже стара БД), `AddMissingColumns(..., RunColumnsV6)` у хвості вже додає їх раніше — перебудова йде після цього рядка, тому порядок у хвості: після `RunColumnsV6`).

Модель: `GenerationPackageRun.GenerationPackageId` → `public int? GenerationPackageId { get; set; }`; `AppSettings`: `public string? DefaultOutputFolder { get; set; }` з коментарем «Тека, куди лягають документи, якщо не обрано іншої; NULL = Документи\GenDoc».
`GenerationService.GetLastRun`: перед `.Join` додати `.Where(r => r.GenerationPackageId != null)`, у лямбді `run => run.GenerationPackageId!.Value`.
`DocumentArchiveService.GetRunsAsync`: `PackageName = r.GenerationPackage != null ? r.GenerationPackage.Name : "Вибірково"`.
Інші місця з `GenerationPackageId` запуску (grep `run.GenerationPackageId`, `Runs.Where`) — поправити під `int?`.

- [ ] **Step 5: Усі тести зелені**

Run: `dotnet test GenDoc.Tests\GenDoc.Tests.csproj -c Release`

- [ ] **Step 6: Тест сервісу теки**

`GenDoc.Tests/Generation/OutputFolderServiceTests.cs`:
```csharp
using GenDoc.Models;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Generation;

public class OutputFolderServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"gendoc-out-{Guid.NewGuid():N}");
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public async Task SaveDefault_ThenGetDefault_RoundTrips()
    {
        using var db = new TestDb();
        using (var ctx = db.Factory.CreateDbContext()) { ctx.AppSettings.Add(new AppSettings()); ctx.SaveChanges(); }
        var svc = new OutputFolderService(db.Factory);

        await svc.SaveDefaultAsync(_dir);

        Assert.Equal(_dir, await svc.GetDefaultAsync());
    }

    [Fact]
    public async Task ResolveOnDisk_ReturnsPathOnlyWhenFileExists()
    {
        using var db = new TestDb();
        using (var ctx = db.Factory.CreateDbContext()) { ctx.AppSettings.Add(new AppSettings { DefaultOutputFolder = _dir }); ctx.SaveChanges(); }
        var svc = new OutputFolderService(db.Factory);
        Directory.CreateDirectory(Path.Combine(_dir, "Набір"));
        var relative = Path.Combine("Набір", "ШЕВЧЕНКО Тарас.docx");
        File.WriteAllText(Path.Combine(_dir, relative), "x");

        Assert.Equal(Path.Combine(_dir, relative), await svc.ResolveOnDiskAsync(relative));
        Assert.Null(await svc.ResolveOnDiskAsync(Path.Combine("Набір", "немає.docx")));
    }
}
```
(Перевірити, чи `AppSettings` має обов'язкові поля без дефолтів; якщо так — задати їх у тесті так само, як у інших тестах, що створюють `AppSettings`.)

- [ ] **Step 7: Реалізація сервісу**

`GenDoc/Services/Generation/OutputFolderService.cs`:
```csharp
using System.IO;
using GenDoc.Data;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services.Generation
{
    public interface IOutputFolderService
    {
        Task<string> GetDefaultAsync();
        Task SaveDefaultAsync(string folder);
        // Абсолютний шлях документа в типовій теці, якщо файл там є; інакше null.
        Task<string?> ResolveOnDiskAsync(string relativeFileName);
    }

    public class OutputFolderService : IOutputFolderService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        public OutputFolderService(IDbContextFactory<AppDbContext> dbFactory) => _dbFactory = dbFactory;

        public async Task<string> GetDefaultAsync()
        {
            using var db = _dbFactory.CreateDbContext();
            var configured = await db.AppSettings.Select(s => s.DefaultOutputFolder).FirstOrDefaultAsync();
            return OutputFolderResolver.Resolve(configured);
        }

        public async Task SaveDefaultAsync(string folder)
        {
            using var db = _dbFactory.CreateDbContext();
            var settings = await db.AppSettings.FirstOrDefaultAsync();
            if (settings is null) { settings = new Models.AppSettings(); db.AppSettings.Add(settings); }
            settings.DefaultOutputFolder = folder.Trim();
            await db.SaveChangesAsync();
        }

        public async Task<string?> ResolveOnDiskAsync(string relativeFileName)
        {
            if (string.IsNullOrWhiteSpace(relativeFileName)) return null;
            var path = Path.Combine(await GetDefaultAsync(), relativeFileName);
            return File.Exists(path) ? path : null;
        }
    }
}
```
DI в `App.ConfigureServices`: `services.AddTransient<IOutputFolderService, OutputFolderService>();`

- [ ] **Step 8: Екран «Генерація»**

`GenerationViewModel`: додати залежність `IOutputFolderService outputFolderService` у конструктор (і поле), у конструкторі наприкінці `_ = LoadDefaultOutputFolderAsync();`:
```csharp
    private async Task LoadDefaultOutputFolderAsync()
        => OutputFolder = await _outputFolderService.GetDefaultAsync();
```
- У `SelectPackageAsync` рядок `OutputFolder = null;` замінити на `if (string.IsNullOrWhiteSpace(OutputFolder)) await LoadDefaultOutputFolderAsync();`.
- `PickOutputFolder` → `async Task PickOutputFolderAsync()`: після вибору `OutputFolder = dialog.FolderName; await _outputFolderService.SaveDefaultAsync(dialog.FolderName);` (початкова тека діалогу — `InitialDirectory = OutputFolder`, якщо тека існує).
- XAML (рядки 416-421): напис кнопки «Обрати папку…» → «Змінити…»; перед нею `TextBlock Text="Тека документів:"`; тека показана як зараз (`TextBlock` з `OutputFolder`).
- Перевірити `App.xaml.cs`/DI: `GenerationViewModel` створюється контейнером — новий параметр підхопиться. Тести в'ю-моделі, якщо є (`grep -rn "new GenerationViewModel(" GenDoc.Tests`), доповнити фейком `IOutputFolderService` (`FakeOutputFolder : IOutputFolderService`, що повертає фіксовану теку) у `Fakes.cs`.

- [ ] **Step 9: Тести, живий прогін, коміт**

Run: `dotnet test GenDoc.Tests\GenDoc.Tests.csproj -c Release`
Живий: «Генерація» → обрати пакет: тека «…\Документи\GenDoc» уже підставлена, кнопка «Згенерувати всім» активна; «Змінити…» → обрати іншу → перемкнути пакет → тека лишилась.
```bash
git add -A GenDoc GenDoc.Tests
git commit -m "2.1: міграція v24 (тека за замовчуванням, запуски без пакета); «Генерація» не вимагає обирати теку щоразу

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: 2.2 (сервіс) — `GenerateTemplatesForRecipients`

**Files:**
- Modify: `GenDoc/Services/Generation/IGenerationService.cs` (RunResult + метод)
- Modify: `GenDoc/Services/Generation/GenerationService.cs` (новий метод поруч із `RunPackage`; `RunPackage` заповнює `RunId`/`Issues` у `RunResult`)
- Test: `GenDoc.Tests/Generation/GenerateTemplatesForRecipientsTests.cs` (new)

**Interfaces:**
- Changes: `public record RunResult(int Generated, int Skipped, int Errors, int GroupGenerated, int GroupSkipped, int GroupErrors, int DocxGroupGenerated, int DocxGroupSkipped, int DocxGroupErrors, int RunId = 0, IReadOnlyList<RunIssue>? Issues = null);`
- Produces: `RunResult IGenerationService.GenerateTemplatesForRecipients(IReadOnlyList<int> templateIds, IReadOnlyList<int> recipientIds, string outputFolder, Dictionary<string,string> manualValues, IProgress<string> progress);`

- [ ] **Step 1: Тест**

`GenDoc.Tests/Generation/GenerateTemplatesForRecipientsTests.cs`:
```csharp
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Generation;

// 2.2: документ на 1-3 людей з будь-якого шаблону - без пакета. Той самий конвеєр,
// що й RunPackage (розкладка по теках, версії, архів), але запуск без GenerationPackageId.
public class GenerateTemplatesForRecipientsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-adhoc-{Guid.NewGuid():N}");
    public void Dispose() { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }
    private static readonly IProgress<string> NoProgress = new Progress<string>(_ => { });

    private static (int TemplateId, List<int> PeopleIds, int IntakeId) Seed(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
        ctx.OrganizationSettings.Add(new OrganizationSettings
        {
            UnitNumber = "А1234", City = "Львів", CommanderRank = "полковник", CommanderFullName = "І. ПЕТРЕНКО",
            CommanderPosition = "начальник", HrOfficerFullName = "К. КАДРОВ", UnitFullName = "Коледж"
        });
        var intake = new Intake { Number = 15, DisplayNumber = "Набір №15" };
        ctx.Intakes.Add(intake);
        var template = new Template
        {
            Name = "Шаблон_Рапорт_котлове_ІНДИВІДУАЛЬНИЙ", OriginalFileName = "rapport.docx",
            Content = TemplateFixtures.Bytes(TemplateFixtures.RaportIndividualDocx),
            UploadedAt = DateTime.Now, Kind = TemplateKind.PerRecipient
        };
        ctx.Templates.Add(template);
        var people = TemplateFixtures.Roster(3);
        ctx.Recipients.AddRange(people);
        ctx.SaveChanges();
        foreach (var p in people) p.IntakeId = intake.Id;
        foreach (var tag in new[] { "{{звання_зв}}", "{{піб_зв}}", "{{прибув}}", "{{таким}}" })
        {
            var (sourceType, fieldName) = Services.Templates.PlaceholderTagMaps.Classify(tag);
            ctx.TemplateFieldMappings.Add(new TemplateFieldMapping
            { TemplateId = template.Id, PlaceholderTag = tag, SourceType = sourceType, FieldName = fieldName, IsInsideRepeatingBlock = false });
        }
        ctx.SaveChanges();
        return (template.Id, people.Select(p => p.Id).ToList(), intake.Id);
    }

    [Fact]
    public void GeneratesOneFilePerPersonAndRecordsRunWithoutPackage()
    {
        using var db = new TestDb();
        var (templateId, peopleIds, intakeId) = Seed(db);

        var result = TestServices.Generation(db).GenerateTemplatesForRecipients(
            new[] { templateId }, peopleIds.Take(2).ToList(), _folder, new Dictionary<string, string>(), NoProgress);

        Assert.Equal(2, result.Generated);
        Assert.Equal(0, result.Errors);
        Assert.Equal(2, Directory.GetFiles(_folder, "*.docx", SearchOption.AllDirectories).Length);

        using var check = db.Factory.CreateDbContext();
        var run = check.GenerationPackageRuns.Single();
        Assert.Null(run.GenerationPackageId);
        Assert.Equal(intakeId, run.IntakeId);
        Assert.Equal(run.Id, result.RunId);
        Assert.Equal(2, check.GeneratedDocuments.Count(g => g.RunId == run.Id));
    }

    [Fact]
    public async Task AdHocRun_IsVisibleInArchiveRunsAsSelective()
    {
        using var db = new TestDb();
        var (templateId, peopleIds, intakeId) = Seed(db);
        TestServices.Generation(db).GenerateTemplatesForRecipients(
            new[] { templateId }, peopleIds.Take(1).ToList(), _folder, new Dictionary<string, string>(), NoProgress);

        var runs = await TestServices.Archive(db).GetRunsAsync(intakeId, null);

        var run = Assert.Single(runs);
        Assert.Equal("Вибірково", run.PackageName);
    }

    [Fact]
    public void SecondCall_BumpsVersion()
    {
        using var db = new TestDb();
        var (templateId, peopleIds, _) = Seed(db);
        var svc = TestServices.Generation(db);
        svc.GenerateTemplatesForRecipients(new[] { templateId }, peopleIds.Take(1).ToList(), _folder, new(), NoProgress);
        svc.GenerateTemplatesForRecipients(new[] { templateId }, peopleIds.Take(1).ToList(), _folder, new(), NoProgress);

        using var check = db.Factory.CreateDbContext();
        Assert.Equal(2, check.GeneratedDocuments.IgnoreQueryFilters().Count());
        Assert.Equal(2, check.GeneratedDocuments.Single(g => g.IsCurrent).Version);
    }
}
```
(`IgnoreQueryFilters` потребує `using Microsoft.EntityFrameworkCore;`.)

- [ ] **Step 2: Запустити — падає (метод не існує)**

- [ ] **Step 3: Реалізація**

`IGenerationService.cs`: змінити `RunResult` (додати `int RunId = 0, IReadOnlyList<RunIssue>? Issues = null` наприкінці; `using GenDoc.Services.Generation;` вже в namespace) і додати в інтерфейс:
```csharp
        /// <summary>Вибіркова генерація: обрані шаблони × обрані особи, поза пакетом.
        /// Той самий конвеєр, що й RunPackage (розкладка, версії, архів, RunIssue);
        /// запуск пишеться з GenerationPackageId = null і підписується «Вибірково».
        /// Наявні документи завжди перегенеровуються (оператор попросив явно).</summary>
        RunResult GenerateTemplatesForRecipients(
            IReadOnlyList<int> templateIds,
            IReadOnlyList<int> recipientIds,
            string outputFolder,
            Dictionary<string, string> manualValues,
            IProgress<string> progress);
```
`GenerationService`:
```csharp
        public RunResult GenerateTemplatesForRecipients(
            IReadOnlyList<int> templateIds,
            IReadOnlyList<int> recipientIds,
            string outputFolder,
            Dictionary<string, string> manualValues,
            IProgress<string> progress)
        {
            using var db = _dbFactory.CreateDbContext();

            var templates = db.Templates
                .Where(t => templateIds.Contains(t.Id) && t.Kind == TemplateKind.PerRecipient)
                .ToList();
            var recipients = LoadRosterRecipients(db, new RosterSelection(
                false, recipientIds, FitnessFilter.All, false, Array.Empty<RankCategory>(), Array.Empty<string>()));
            var orgSettings = db.OrganizationSettings.FirstOrDefault();

            var run = new GenerationPackageRun
            {
                GenerationPackageId = null,
                RunAt = DateTime.Now,
                RunByUserId = _currentUserContext.CurrentUserId ?? 0,
                IntakeId = RunIntakeResolver.Resolve(recipients.Select(r => r.IntakeId))
            };
            db.GenerationPackageRuns.Add(run);
            db.SaveChanges();

            Directory.CreateDirectory(outputFolder);
            var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var runStamp = ResolveRunStamp(outputFolder);

            var docx = RunDocxPhase(db, templates, recipients, orgSettings, run, manualValues, outputFolder,
                usedFileNames, runStamp, regenerateExisting: true, progress);

            run.GeneratedCount = docx.Generated;
            run.SkippedCount = docx.Skipped;
            run.ErrorCount = docx.Errors;
            run.Summary = RunIssue.Serialize(docx.Issues);

            _auditLogService.LogGenerate(db, "GenerationPackageRun", run.Id,
                $"Вибірково: шаблонів {templates.Count}, осіб {recipients.Count}; згенеровано {docx.Generated}, помилок {docx.Errors}");
            db.SaveChanges();

            return new RunResult(docx.Generated, docx.Skipped, docx.Errors, 0, 0, 0, 0, 0, 0, run.Id, docx.Issues);
        }
```
У `RunPackage` повертати `new RunResult(..., run.Id, issues)` (дев'ять чисел як зараз, плюс два нові).

- [ ] **Step 4: Тести зелені + коміт**

Run: `dotnet test GenDoc.Tests\GenDoc.Tests.csproj -c Release`
```bash
git add -A GenDoc GenDoc.Tests
git commit -m "2.2 (сервіс): вибіркова генерація шаблонів для обраних осіб без пакета

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 8: 2.2 (інтерфейс) — діалог «Згенерувати документ…» у картці та для обраних

**Files:**
- Create: `GenDoc/ViewModels/Generation/GenerationResultViewModel.cs`
- Create: `GenDoc/ViewModels/Personnel/GenerateDocumentsDialogViewModel.cs`, `GenDoc/Views/Personnel/GenerateDocumentsDialog.xaml`, `.xaml.cs`
- Modify: `GenDoc/Services/DialogService.cs` (реєстрація), `GenDoc/Services/Navigation/SectionNavigationMessages.cs` (`ArchiveRunNavigationPayload`)
- Modify: `GenDoc/ViewModels/Personnel/PersonCardViewModel.cs`, `GenDoc/ViewModels/Personnel/PersonnelViewModel.cs`, `GenDoc/Views/Personnel/PersonnelView.xaml`
- Modify: `GenDoc/ViewModels/Archive/ArchiveViewModel.cs` (`INavigationTarget`)
- Test: `GenDoc.Tests/Generation/GenerationResultViewModelTests.cs` (new), `GenDoc.Tests/Personnel/GenerateDocumentsDialogViewModelTests.cs` (new)

**Interfaces:**
- Produces: `public sealed record ArchiveRunNavigationPayload(int RunId);` (namespace `GenDoc.Services.Navigation`)
- Produces: `public partial class GenerationResultViewModel : ObservableObject` — ctor `(RunResult result, string outputFolder)`; властивості `SummaryText`, `Issues: IReadOnlyList<RunIssue>`, `HasIssues`, `RunId`, `OutputFolder`; команди `OpenFolderCommand`, `ShowInArchiveCommand` (надсилає `NavigateToSectionMessage("Архів документів", new ArchiveRunNavigationPayload(RunId))`).
- Produces: `public partial class GenerateDocumentsDialogViewModel : DialogViewModelBase` — ctor `(IGenerationService, ICompletenessService, IDocumentArchiveService, IManualTagFormBuilder, IDialogService, IOutputFolderService, IReadOnlyList<(int Id, string DisplayName)> recipients)`; `Task InitializeAsync()`; `ObservableCollection<TemplateChoiceItem> Templates`; `int SelectedCount`; `GenerateCommand`; `GenerationResultViewModel? Result`; `TemplateChoiceItem(int Id, string Name, string Group)` з `IsChecked`.
- `ArchiveViewModel : INavigationTarget` — `ApplyNavigationPayloadAsync(ArchiveRunNavigationPayload)` перемикає на «Запуски» й розгортає запуск.

- [ ] **Step 1: Тести**

`GenDoc.Tests/Generation/GenerationResultViewModelTests.cs`:
```csharp
using GenDoc.Services.Generation;
using GenDoc.ViewModels.Generation;

namespace GenDoc.Tests.Generation;

public class GenerationResultViewModelTests
{
    [Fact]
    public void SummaryText_CountsAllThreePhases()
    {
        var vm = new GenerationResultViewModel(new RunResult(31, 2, 1, 1, 0, 0, 0, 0, 1, RunId: 7,
            Issues: new[] { new RunIssue(RunIssue.PhaseDocx, "ШЕВЧЕНКО Тарас", "Рапорт", "немає тегу") }), @"D:\out");

        Assert.Equal("Згенеровано 32 · Пропущено 2 · Помилок 2", vm.SummaryText);
        Assert.True(vm.HasIssues);
        Assert.Single(vm.Issues);
        Assert.Equal(7, vm.RunId);
    }

    [Fact]
    public void SummaryText_WithoutIssues()
    {
        var vm = new GenerationResultViewModel(new RunResult(3, 0, 0, 0, 0, 0, 0, 0, 0), @"D:\out");
        Assert.Equal("Згенеровано 3 · Пропущено 0 · Помилок 0", vm.SummaryText);
        Assert.False(vm.HasIssues);
    }
}
```
`GenDoc.Tests/Personnel/GenerateDocumentsDialogViewModelTests.cs` — перевіряє лише чисту частину (порядок і фільтр шаблонів), винесену в статичний метод:
```csharp
using GenDoc.ViewModels.Personnel;

namespace GenDoc.Tests.Personnel;

public class GenerateDocumentsDialogViewModelTests
{
    [Fact]
    public void OrderTemplates_DefaultPackageFirst_ThenOthers_NoGroup()
    {
        var all = new List<(int Id, string Name)> { (1, "Довідка"), (2, "Рапорт"), (3, "Допуск") };
        var packageIds = new List<int> { 2 };

        var ordered = GenerateDocumentsDialogViewModel.OrderTemplates(all, packageIds);

        Assert.Equal(new[] { 2, 1, 3 }, ordered.Select(t => t.Id));
        Assert.Equal("Типовий пакет", ordered[0].Group);
        Assert.Equal("Інші шаблони", ordered[1].Group);
    }

    [Fact]
    public void RecipientsSummary_OneOrMany()
    {
        Assert.Equal("ШЕВЧЕНКО Т.Г.", GenerateDocumentsDialogViewModel.BuildRecipientsSummary(new[] { "ШЕВЧЕНКО Т.Г." }));
        Assert.Equal("ШЕВЧЕНКО Т.Г. та ще 2", GenerateDocumentsDialogViewModel.BuildRecipientsSummary(new[] { "ШЕВЧЕНКО Т.Г.", "А", "Б" }));
    }
}
```

- [ ] **Step 2: Запустити — падає**

- [ ] **Step 3: Навігаційний payload і `GenerationResultViewModel`**

`SectionNavigationMessages.cs`: додати `public sealed record ArchiveRunNavigationPayload(int RunId);`

`GenDoc/ViewModels/Generation/GenerationResultViewModel.cs`:
```csharp
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Services.Generation;
using GenDoc.Services.Navigation;

namespace GenDoc.ViewModels.Generation;

// Картка підсумку прогону (2.4): замість двох MessageBox - числа, список проблем
// і дві дії. Використовується на екрані «Генерація» і в діалозі «Згенерувати документ…».
public partial class GenerationResultViewModel : ObservableObject
{
    public GenerationResultViewModel(RunResult result, string outputFolder)
    {
        RunId = result.RunId;
        OutputFolder = outputFolder;
        Issues = result.Issues ?? Array.Empty<RunIssue>();
        var generated = result.Generated + result.GroupGenerated + result.DocxGroupGenerated;
        var skipped = result.Skipped + result.GroupSkipped + result.DocxGroupSkipped;
        var errors = result.Errors + result.GroupErrors + result.DocxGroupErrors;
        SummaryText = $"Згенеровано {generated} · Пропущено {skipped} · Помилок {errors}";
        HasErrors = errors > 0;
    }

    public int RunId { get; }
    public string OutputFolder { get; }
    public string SummaryText { get; }
    public bool HasErrors { get; }
    public IReadOnlyList<RunIssue> Issues { get; }
    public bool HasIssues => Issues.Count > 0;
    public bool CanShowInArchive => RunId > 0;

    [RelayCommand]
    private void OpenFolder()
    {
        try { Process.Start("explorer.exe", $"\"{OutputFolder}\""); } catch { }
    }

    [RelayCommand]
    private void ShowInArchive()
        => WeakReferenceMessenger.Default.Send(new NavigateToSectionMessage("Архів документів", new ArchiveRunNavigationPayload(RunId)));
}
```
(Перевірити точну назву розділу архіву в `MainViewModel` — `"Архів документів"` — і за потреби винести константу `ArchiveSectionTitle` поруч із `GenerationSectionTitle`.)

`ArchiveViewModel`: реалізувати `INavigationTarget`:
```csharp
        public async Task ApplyNavigationPayloadAsync(object payload)
        {
            if (payload is not ArchiveRunNavigationPayload nav) return;
            SelectedTabIndex = 1;
            await ReloadRunsAsync();
            var run = Runs.FirstOrDefault(r => r.Id == nav.RunId);
            if (run is not null && !run.IsExpanded) await ToggleRunAsync(run);
        }
```
(`using GenDoc.Services.Navigation;`; клас оголосити `: ObservableObject, INavigationTarget`.) Якщо запуск не проходить фільтр набору — перед пошуком скинути `SelectedIntake` на «усі» (`SelectedIntake = IntakeOptions.FirstOrDefault(o => o.Id is null)`) і перезавантажити.

- [ ] **Step 4: Діалог — в'ю-модель**

`GenDoc/ViewModels/Personnel/GenerateDocumentsDialogViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Services;
using GenDoc.Services.Completeness;
using GenDoc.Services.Documents;
using GenDoc.Services.Generation;
using GenDoc.ViewModels.Archive;
using GenDoc.ViewModels.Generation;

namespace GenDoc.ViewModels.Personnel
{
    public partial class TemplateChoiceItem : ObservableObject
    {
        public TemplateChoiceItem(int id, string name, string group) { Id = id; Name = name; Group = group; }
        public int Id { get; }
        public string Name { get; }
        public string Group { get; }
        [ObservableProperty] private bool isChecked;
    }

    // «Згенерувати документ…» з картки особи або для обраних у списку (2.2):
    // обрати шаблони → ручні поля (наявна форма) → генерація в типову теку → підсумок.
    public partial class GenerateDocumentsDialogViewModel : DialogViewModelBase
    {
        private const string ManualTagContextKey = "generate-documents-dialog";

        private readonly IGenerationService _generationService;
        private readonly ICompletenessService _completenessService;
        private readonly IDocumentArchiveService _archiveService;
        private readonly IManualTagFormBuilder _manualTagFormBuilder;
        private readonly IDialogService _dialogService;
        private readonly IOutputFolderService _outputFolderService;
        private readonly IReadOnlyList<(int Id, string DisplayName)> _recipients;

        public GenerateDocumentsDialogViewModel(
            IGenerationService generationService, ICompletenessService completenessService,
            IDocumentArchiveService archiveService, IManualTagFormBuilder manualTagFormBuilder,
            IDialogService dialogService, IOutputFolderService outputFolderService,
            IReadOnlyList<(int Id, string DisplayName)> recipients)
        {
            _generationService = generationService;
            _completenessService = completenessService;
            _archiveService = archiveService;
            _manualTagFormBuilder = manualTagFormBuilder;
            _dialogService = dialogService;
            _outputFolderService = outputFolderService;
            _recipients = recipients;
            RecipientsSummary = BuildRecipientsSummary(recipients.Select(r => r.DisplayName).ToList());
        }

        public ObservableCollection<TemplateChoiceItem> Templates { get; } = new();
        public string RecipientsSummary { get; }
        public int RecipientCount => _recipients.Count;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanGenerate))]
        [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
        private int selectedCount;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanGenerate))]
        [NotifyPropertyChangedFor(nameof(NotBusy))]
        [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
        private bool isBusy;

        [ObservableProperty] private string progressText = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasResult))]
        [NotifyPropertyChangedFor(nameof(IsChoosing))]
        private GenerationResultViewModel? result;

        public bool NotBusy => !IsBusy;
        public bool HasResult => Result is not null;
        public bool IsChoosing => Result is null;
        public bool CanGenerate => SelectedCount > 0 && !IsBusy;
        public string GenerateButtonText => RecipientCount == 1 ? "Згенерувати" : $"Згенерувати для {RecipientCount} осіб";

        public async Task InitializeAsync()
        {
            var all = _generationService.GetPerRecipientTemplates(Models.Enums.TemplateAudience.Intake);
            var packageId = await _completenessService.GetDefaultPackageIdAsync();
            var packageTemplateIds = packageId is int pid
                ? (await _completenessService.GetPackageLinksAsync(pid)).Where(l => !l.IsGroup).Select(l => l.TemplateId).ToList()
                : new List<int>();

            Templates.Clear();
            foreach (var item in OrderTemplates(all, packageTemplateIds))
            {
                item.PropertyChanged += OnTemplateChanged;
                Templates.Add(item);
            }
        }

        // Спочатку шаблони типового пакета (у його порядку), далі решта за назвою; групових
        // тут нема - GetPerRecipientTemplates повертає лише персональні.
        internal static List<TemplateChoiceItem> OrderTemplates(
            IReadOnlyList<(int Id, string Name)> all, IReadOnlyList<int> defaultPackageTemplateIds)
        {
            var byId = all.ToDictionary(t => t.Id);
            var result = new List<TemplateChoiceItem>();
            foreach (var id in defaultPackageTemplateIds)
                if (byId.TryGetValue(id, out var t)) result.Add(new TemplateChoiceItem(t.Id, t.Name, "Типовий пакет"));
            var rest = all.Where(t => !defaultPackageTemplateIds.Contains(t.Id))
                .OrderBy(t => t.Name, UkrainianCollation.Surname);
            foreach (var t in rest) result.Add(new TemplateChoiceItem(t.Id, t.Name, "Інші шаблони"));
            return result;
        }

        internal static string BuildRecipientsSummary(IReadOnlyList<string> names)
            => names.Count <= 1 ? (names.Count == 1 ? names[0] : "") : $"{names[0]} та ще {names.Count - 1}";

        private void OnTemplateChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TemplateChoiceItem.IsChecked))
                SelectedCount = Templates.Count(t => t.IsChecked);
        }

        [RelayCommand(CanExecute = nameof(CanGenerate))]
        private async Task GenerateAsync()
        {
            var templateIds = Templates.Where(t => t.IsChecked).Select(t => t.Id).ToList();
            var manualTags = await _archiveService.GetManualTagsAsync(templateIds);
            var manualValues = new Dictionary<string, string>();
            if (manualTags.Count > 0)
            {
                var form = await _manualTagFormBuilder.BuildAsync(manualTags, ManualTagContextKey);
                var dialog = new ManualValuesDialogViewModel(form);
                if (_dialogService.ShowDialog(dialog, Application.Current.MainWindow) != true) return;
                await _manualTagFormBuilder.SaveAsync(ManualTagContextKey, form);
                manualValues = dialog.GetValues();
            }

            var folder = await _outputFolderService.GetDefaultAsync();
            var recipientIds = _recipients.Select(r => r.Id).ToList();
            var progress = new Progress<string>(m => ProgressText = m);

            IsBusy = true;
            try
            {
                var runResult = await Task.Run(() =>
                    _generationService.GenerateTemplatesForRecipients(templateIds, recipientIds, folder, manualValues, progress));
                Result = new GenerationResultViewModel(runResult, folder);
            }
            finally
            {
                IsBusy = false;
                ProgressText = string.Empty;
            }
        }

        [RelayCommand] private void Cancel() => CloseDialog(false);
        [RelayCommand] private void Done() => CloseDialog(true);
    }
}
```
(`UkrainianCollation.Surname` — наявний компаратор у `GenDoc.Services`; якщо він типу `IComparer<string>` — `OrderBy(t => t.Name, UkrainianCollation.Surname)` працює.)

- [ ] **Step 5: Діалог — вікно**

`GenDoc/Views/Personnel/GenerateDocumentsDialog.xaml.cs`:
```csharp
using System.Windows;
using GenDoc.ViewModels.Personnel;

namespace GenDoc.Views.Personnel;

public partial class GenerateDocumentsDialog : Window
{
    public GenerateDocumentsDialog(GenerateDocumentsDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += (_, _) => { DialogResult = viewModel.DialogResultValue; Close(); };
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
```
`GenDoc/Views/Personnel/GenerateDocumentsDialog.xaml`:
```xml
<Window x:Class="GenDoc.Views.Personnel.GenerateDocumentsDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Style="{StaticResource {x:Type Window}}"
        Title="Згенерувати документ" Width="560" Height="620"
        Background="{StaticResource PanelBrush}"
        WindowStartupLocation="CenterOwner" ResizeMode="CanResize" MinWidth="480" MinHeight="420">
    <DockPanel Margin="20">
        <!-- Вибір шаблонів -->
        <StackPanel Visibility="{Binding IsChoosing, Converter={StaticResource BoolToVisibility}}" DockPanel.Dock="Top">
            <TextBlock Text="{Binding RecipientsSummary, StringFormat='Для: {0}'}" FontSize="13" FontWeight="SemiBold"
                       Foreground="{StaticResource TextPrimaryBrush}"/>
            <TextBlock Text="Оберіть один або кілька шаблонів. Ручні поля запитаються наступним кроком, документи ляжуть у типову теку."
                       FontSize="11.5" TextWrapping="Wrap" Foreground="{StaticResource TextSecondaryBrush}" Margin="0,4,0,10"/>
        </StackPanel>

        <StackPanel DockPanel.Dock="Bottom" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,14,0,0">
            <TextBlock Text="{Binding ProgressText}" VerticalAlignment="Center" Margin="0,0,12,0" FontSize="12"
                       Foreground="{StaticResource NavyBrush}"/>
            <Button Content="Скасувати" Style="{StaticResource SecondaryButtonStyle}" Command="{Binding CancelCommand}"
                    Visibility="{Binding IsChoosing, Converter={StaticResource BoolToVisibility}}"/>
            <Button Content="{Binding GenerateButtonText}" Margin="8,0,0,0" MinWidth="130" Command="{Binding GenerateCommand}"
                    Visibility="{Binding IsChoosing, Converter={StaticResource BoolToVisibility}}"/>
            <Button Content="Готово" MinWidth="110" Command="{Binding DoneCommand}"
                    Visibility="{Binding HasResult, Converter={StaticResource BoolToVisibility}}"/>
        </StackPanel>

        <Grid>
            <ScrollViewer VerticalScrollBarVisibility="Auto" Visibility="{Binding IsChoosing, Converter={StaticResource BoolToVisibility}}">
                <ItemsControl ItemsSource="{Binding Templates}">
                    <ItemsControl.GroupStyle>
                        <GroupStyle>
                            <GroupStyle.HeaderTemplate>
                                <DataTemplate>
                                    <TextBlock Text="{Binding Name}" FontSize="10.5" FontWeight="Bold" Margin="0,10,0,4"
                                               Foreground="{StaticResource TextSecondaryBrush}"/>
                                </DataTemplate>
                            </GroupStyle.HeaderTemplate>
                        </GroupStyle>
                    </ItemsControl.GroupStyle>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <CheckBox IsChecked="{Binding IsChecked, Mode=TwoWay}" Content="{Binding Name}" Padding="8,5" Margin="0,2"/>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </ScrollViewer>

            <!-- Підсумок -->
            <StackPanel DataContext="{Binding Result}" Visibility="{Binding DataContext.HasResult, RelativeSource={RelativeSource AncestorType=Window}, Converter={StaticResource BoolToVisibility}}">
                <TextBlock Text="{Binding SummaryText}" FontSize="14" FontWeight="Bold" Foreground="{StaticResource TextPrimaryBrush}"/>
                <ItemsControl ItemsSource="{Binding Issues}" Margin="0,10,0,0"
                              Visibility="{Binding HasIssues, Converter={StaticResource BoolToVisibility}}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <TextBlock FontSize="12" TextWrapping="Wrap" Margin="0,2" Foreground="{StaticResource WarnTextBrush}">
                                <Run Text="{Binding Person, Mode=OneWay}"/><Run Text=" · "/><Run Text="{Binding TemplateName, Mode=OneWay}"/><Run Text=" - "/><Run Text="{Binding Message, Mode=OneWay}"/>
                            </TextBlock>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <StackPanel Orientation="Horizontal" Margin="0,16,0,0">
                    <Button Content="Відкрити теку" Style="{StaticResource SecondaryButtonStyle}" Command="{Binding OpenFolderCommand}"/>
                    <Button Content="Показати в архіві" Style="{StaticResource SecondaryButtonStyle}" Margin="8,0,0,0"
                            Command="{Binding ShowInArchiveCommand}" IsEnabled="{Binding CanShowInArchive}"/>
                </StackPanel>
            </StackPanel>
        </Grid>
    </DockPanel>
</Window>
```
Групування `ItemsControl` за `Group`: у code-behind після `DataContext = viewModel` — `var view = System.Windows.Data.CollectionViewSource.GetDefaultView(viewModel.Templates); view.GroupDescriptions.Add(new System.ComponentModel.PropertyGroupDescription(nameof(TemplateChoiceItem.Group)));` (`GroupStyle` на `ItemsControl` працює лише з групованим view). «Показати в архіві» з діалогу: команда надсилає повідомлення, але діалог модальний — тому в `GenerateDocumentsDialogViewModel` обгорнути: `Result.ShowInArchive` → спершу `CloseDialog(true)`, потім повідомлення. Реалізація: у XAML прив'язати кнопку до `DataContext.ShowInArchiveAndCloseCommand` вікна (`RelativeSource AncestorType=Window`), а у VM:
```csharp
        [RelayCommand]
        private void ShowInArchiveAndClose()
        {
            var result = Result;
            CloseDialog(true);
            result?.ShowInArchiveCommand.Execute(null);
        }
```
`DialogService`: додати `[typeof(GenerateDocumentsDialogViewModel)] = typeof(GenerateDocumentsDialog),` (і `using GenDoc.Views.Personnel;` уже є).

- [ ] **Step 6: Виклики з картки й панелі виділення**

`PersonCardViewModel` (потрібні `IOutputFolderService` і `IServiceProvider` — або отримати сервіси через `App.Services`; у проєкті в'ю-моделі картки створюються вручну в `PersonnelViewModel.OpenCard`, тож додати `IOutputFolderService outputFolderService` у конструктор картки та в `PersonnelViewModel` (DI)):
```csharp
        [RelayCommand]
        private async Task GenerateAnyDocumentAsync()
        {
            if (IsNew) return;
            var dialog = new GenerateDocumentsDialogViewModel(
                _generationService, _completenessService, _archiveService, _manualTagFormBuilder,
                _dialogService, _outputFolderService,
                new[] { (Id, $"{LastName} {FirstMiddle}".Trim()) });
            _dialogService.ShowDialog(dialog, Application.Current.MainWindow);
            await RefreshDocumentsAsync();
        }
```
`PersonnelViewModel`:
```csharp
        [RelayCommand]
        private void GenerateForChecked()
        {
            var people = Rows.Where(r => r.IsChecked).Select(r => (r.Id, r.ShortName)).ToList();
            if (people.Count == 0) return;
            var dialog = new GenerateDocumentsDialogViewModel(
                _generationService, _completenessService, _archiveService, _manualTagFormBuilder,
                _dialogService, _outputFolderService, people);
            _dialogService.ShowDialog(dialog, Application.Current.MainWindow);
        }
```
(`PersonRowViewModel.ShortName` — перевірити назву властивості з ПІБ у рядку; якщо інша — підставити її.)
XAML `PersonnelView.xaml`:
- панель виділення: після «Перемістити до…» додати `<Button Content="Згенерувати документ…" Margin="8,0,0,0" Style="{StaticResource SecondaryButtonStyle}" Command="{Binding GenerateForCheckedCommand}"/>`;
- картка, вкладка «Документи», одразу після блоку «Сформувати повний пакет»/«Пакет повний»: `<Button Content="Згенерувати документ…" Style="{StaticResource SecondaryButtonStyle}" HorizontalAlignment="Stretch" Margin="0,0,0,10" Command="{Binding GenerateAnyDocumentCommand}"/>`.

- [ ] **Step 7: Збірка, тести, живий прогін**

Run: `dotnet build GenDoc\GenDoc.csproj && dotnet test GenDoc.Tests\GenDoc.Tests.csproj -c Release`
Живий: картка БЕНІШ → «Згенерувати документ…» → шаблони згруповані → обрати → ручні поля → підсумок «Згенеровано 1…» → «Показати в архіві» відкриває «Запуски» з розгорнутим «Вибірково». У списку позначити 2 людей → «Згенерувати документ…» → 2 документи. Знімки.

- [ ] **Step 8: Коміт**

```bash
git add -A GenDoc GenDoc.Tests
git commit -m "2.2: діалог «Згенерувати документ…» з картки особи й для обраних; підсумок замість MessageBox; перехід в архів до запуску

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 9: 2.3 — пошук у «Вибрані», підписи кнопки й радіо

**Files:**
- Modify: `GenDoc/ViewModels/Generation/RecipientCheckRowViewModel.cs`, `GenDoc/ViewModels/Generation/GenerationViewModel.cs`
- Modify: `GenDoc/Views/Generation/GenerationView.xaml:203-206, 246-262, 426`
- Test: `GenDoc.Tests/Generation/RecipientSearchFilterTests.cs` (new)

**Interfaces:**
- Produces: `RecipientCheckRowViewModel.MatchesSearch` (bool, default true), `IsShown => IsVisible && MatchesSearch`.
- Produces: `GenerationViewModel.RecipientSearchText` (string), `GenerateButtonText` (string); `internal static bool RecipientCheckRowViewModel.Matches(string fullName, string? query)`; `internal static string GenerationViewModel.BuildGenerateButtonText(bool useAll, int all, int selected)`.

- [ ] **Step 1: Тести**

`GenDoc.Tests/Generation/RecipientSearchFilterTests.cs`:
```csharp
using GenDoc.ViewModels.Generation;

namespace GenDoc.Tests.Generation;

public class RecipientSearchFilterTests
{
    [Theory]
    [InlineData("ШЕВЧЕНКО Тарас", null, true)]
    [InlineData("ШЕВЧЕНКО Тарас", "", true)]
    [InlineData("ШЕВЧЕНКО Тарас", "шевч", true)]
    [InlineData("ШЕВЧЕНКО Тарас", "тарас", true)]
    [InlineData("ШЕВЧЕНКО Тарас", "Іван", false)]
    public void Matches_IsCaseInsensitiveSubstring(string fullName, string? query, bool expected)
        => Assert.Equal(expected, RecipientCheckRowViewModel.Matches(fullName, query));

    [Fact]
    public void GenerateButtonText_ReflectsModeAndCount()
    {
        Assert.Equal("Згенерувати всім (34)", GenerationViewModel.BuildGenerateButtonText(true, 34, 0));
        Assert.Equal("Згенерувати обраним (3)", GenerationViewModel.BuildGenerateButtonText(false, 34, 3));
    }
}
```

- [ ] **Step 2: Запустити — падає; реалізація**

`RecipientCheckRowViewModel`:
```csharp
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShown))]
    private bool isVisible = true;

    // Пошук за ПІБ (2.3): окремо від фільтра звань, обидва мають збігтися.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShown))]
    private bool matchesSearch = true;

    public bool IsShown => IsVisible && MatchesSearch;

    internal static bool Matches(string fullName, string? query)
        => string.IsNullOrWhiteSpace(query)
           || fullName.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase);
```
(замінити наявне оголошення `isVisible`.)

`GenerationViewModel`:
```csharp
    [ObservableProperty] private string recipientSearchText = string.Empty;

    partial void OnRecipientSearchTextChanged(string value)
    {
        foreach (var row in RecipientOptions) row.MatchesSearch = RecipientCheckRowViewModel.Matches(row.FullName, value);
        OnPropertyChanged(nameof(SelectedRecipientsCountLabel));
    }

    public string GenerateButtonText => BuildGenerateButtonText(UseAllRecipients, RecipientCount, SelectedRecipientsCount);

    internal static string BuildGenerateButtonText(bool useAll, int all, int selected)
        => useAll ? $"Згенерувати всім ({all})" : $"Згенерувати обраним ({selected})";
```
Додати `[NotifyPropertyChangedFor(nameof(GenerateButtonText))]` до полів `useAllRecipients`, `selectedRecipientsCount`, `recipientCount`. Позначки при пошуку не скидаються (лічильник «Обрано» рахує `IsChecked && IsVisible`, як і раніше — пошук на відбір не впливає, лише на показ).

XAML:
- радіо «Весь склад»: `Content="{Binding RecipientCount, StringFormat=...}"` → `Content="{Binding RecipientCount}" ContentStringFormat="Усі ({0})"` (причина «34» без підпису: `StringFormat` на `Content` типу `object` ігнорується).
- кнопка: `Content="Згенерувати всім"` → `Content="{Binding GenerateButtonText}"`.
- над списком «Вибрані» (перед `Border` зі списком): `<TextBox Text="{Binding RecipientSearchText, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,6" Tag="Пошук за ПІБ…"/>` (підказка — за патерном інших полів пошуку в проєкті, напр. у `PersonnelView.xaml` «Пошук за ПІБ або посадою…» — скопіювати той самий спосіб).
- у `DataTemplate` рядка: `Visibility="{Binding IsShown, ...}"` замість `IsVisible`.

- [ ] **Step 3: Тести, живий прогін, коміт**

Run: `dotnet test GenDoc.Tests\GenDoc.Tests.csproj -c Release`
Живий: «Генерація» → пакет → «Усі (34)»; «Вибрані» → набрати «бен» → лишається БЕНІШ; позначити → кнопка «Згенерувати обраним (1)».
```bash
git add -A GenDoc GenDoc.Tests
git commit -m "2.3: пошук у списку «Вибрані», підписи «Усі (N)» і «Згенерувати обраним (N)»

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 10: 2.4 — підсумок генерації на екрані «Генерація»

**Files:**
- Modify: `GenDoc/ViewModels/Generation/GenerationViewModel.cs:484-505`
- Modify: `GenDoc/Views/Generation/GenerationView.xaml:426-434`
- Test: покрито `GenerationResultViewModelTests` (Task 8); тут — лише подача.

- [ ] **Step 1: В'ю-модель**

У `GenerationViewModel` додати `[ObservableProperty] private GenerationResultViewModel? lastResult;` і в `GenerateAllAsync` замінити блок від `var summary = ...` до кінця методу на:
```csharp
        LastResult = new GenerationResultViewModel(result, outputFolderPath);
```
(обидва `MessageBox.Show` прибрати; `using System.Windows;` лишити, якщо використовується деінде). При виборі іншого пакета `LastResult = null;` (у `SelectPackageAsync`).

- [ ] **Step 2: XAML**

Після `Border` з `ProgressText` (рядок ~434) додати:
```xml
                        <Border Background="{StaticResource PanelBrush}" BorderBrush="{StaticResource BorderLineBrush}"
                                BorderThickness="1" Padding="14,12" Margin="0,14,0,0" DataContext="{Binding LastResult}">
                            <Border.Style>
                                <Style TargetType="Border">
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding}" Value="{x:Null}">
                                            <Setter Property="Visibility" Value="Collapsed"/>
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </Border.Style>
                            <StackPanel>
                                <TextBlock Text="{Binding SummaryText}" FontSize="13.5" FontWeight="Bold"
                                           Foreground="{StaticResource TextPrimaryBrush}"/>
                                <ItemsControl ItemsSource="{Binding Issues}" Margin="0,8,0,0"
                                              Visibility="{Binding HasIssues, Converter={StaticResource BoolToVisibility}}">
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate>
                                            <TextBlock FontSize="12" TextWrapping="Wrap" Margin="0,2" Foreground="{StaticResource WarnTextBrush}">
                                                <Run Text="{Binding Person, Mode=OneWay}"/><Run Text=" · "/><Run Text="{Binding TemplateName, Mode=OneWay}"/><Run Text=" - "/><Run Text="{Binding Message, Mode=OneWay}"/>
                                            </TextBlock>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                                <StackPanel Orientation="Horizontal" Margin="0,12,0,0">
                                    <Button Content="Відкрити теку" Style="{StaticResource SecondaryButtonStyle}" Command="{Binding OpenFolderCommand}"/>
                                    <Button Content="Показати в архіві" Style="{StaticResource SecondaryButtonStyle}" Margin="8,0,0,0"
                                            Command="{Binding ShowInArchiveCommand}" IsEnabled="{Binding CanShowInArchive}"/>
                                </StackPanel>
                            </StackPanel>
                        </Border>
```

- [ ] **Step 3: Збірка, тести, живий прогін, коміт**

Живий: запустити пакет «Котлове» на 2 обраних → картка підсумку під кнопкою, без MessageBox; «Показати в архіві» → «Запуски» з розгорнутим запуском; «Відкрити теку» → Провідник.
```bash
git add -A GenDoc
git commit -m "2.4: підсумок генерації картка на екрані замість двох MessageBox

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 11: 2.5 — «Друк» і «Показати в теці»

**Files:**
- Create: `GenDoc/Services/ShellCommands.cs`
- Modify: `GenDoc/Services/Documents/SecureTempFileService.cs` (`PrintAsync`), `GenDoc.Tests/Infrastructure/Fakes.cs` (`FakeTempFiles.PrintAsync`)
- Modify: `GenDoc/Services/Documents/IDocumentArchiveService.cs`, `DocumentArchiveService.cs` (`PrintAsync`, `PrintGroupAsync`)
- Modify: `GenDoc/ViewModels/Archive/ArchiveViewModel.cs`, `GenDoc/Views/Archive/ArchiveView.xaml` (обидві панелі дій)
- Modify: `GenDoc/ViewModels/Personnel/PersonCardViewModel.cs`, `GenDoc/Views/Personnel/PersonnelView.xaml` (рядок документа: «Друк»)
- Test: `GenDoc.Tests/ShellCommandsTests.cs` (new), `GenDoc.Tests/Archive/ArchivePrintTests.cs` (new)

**Interfaces:**
- Produces: `public static class ShellCommands { public static string ExplorerSelectArguments(string path); }` → `"/select,\"<path>\""`.
- Produces: `Task ISecureTempFileService.PrintAsync(string fileName, byte[] content)`.
- Produces: `Task<ArchiveOpResult> IDocumentArchiveService.PrintAsync(int documentId)`, `Task<ArchiveOpResult> PrintGroupAsync(int groupDocumentId)`.
- `ArchiveViewModel`: `PrintCommand`, `ShowInFolderCommand`, `PrintGroupCommand`, `ShowGroupInFolderCommand`; `CanPrint`, `CanShowInFolder` (async-обчислювана через `ResolveOnDiskAsync` при зміні виділення), `ShowInFolderTooltip`.

- [ ] **Step 1: Тести**

`GenDoc.Tests/ShellCommandsTests.cs`:
```csharp
using GenDoc.Services;

namespace GenDoc.Tests;

public class ShellCommandsTests
{
    [Fact]
    public void ExplorerSelectArguments_QuotesPath()
        => Assert.Equal("/select,\"D:\\Док\\Набір 4\\файл.docx\"", ShellCommands.ExplorerSelectArguments(@"D:\Док\Набір 4\файл.docx"));
}
```
`GenDoc.Tests/Archive/ArchivePrintTests.cs` (через `FakeTempFiles.Printed`):
```csharp
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Documents;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Archive;

public class ArchivePrintTests
{
    [Fact]
    public async Task Print_SendsDocumentBytesToShellPrint()
    {
        using var db = new TestDb();
        int docId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
            var person = TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас");
            var template = new Template { Name = "Рапорт", OriginalFileName = "a.docx", Content = new byte[] { 1 }, UploadedAt = DateTime.Now, Kind = TemplateKind.PerRecipient };
            ctx.Recipients.Add(person); ctx.Templates.Add(template); ctx.SaveChanges();
            var doc = new GeneratedDocument
            {
                RecipientId = person.Id, TemplateId = template.Id, GeneratedAt = DateTime.Now, GeneratedByUserId = 1,
                FileName = "ШЕВЧЕНКО Тарас Рапорт.docx", Version = 1, IsCurrent = true, HasContent = true,
                SourceType = DocumentSourceType.Generated, Content = new GeneratedDocumentContent { Content = new byte[] { 1, 2, 3 } }
            };
            ctx.GeneratedDocuments.Add(doc); ctx.SaveChanges();
            docId = doc.Id;
        }
        var temp = new FakeTempFiles();
        var service = new DocumentArchiveService(db.Factory, new FakeAuditLog(), new FakeCurrentUser(), temp,
            new NoOpWatermarkService(), new DocumentGenerationService(), new DocumentHashService());

        var result = await service.PrintAsync(docId);

        Assert.True(result.Success);
        var printed = Assert.Single(temp.Printed);
        Assert.Equal("ШЕВЧЕНКО Тарас Рапорт.docx", printed.FileName);
        Assert.Equal(new byte[] { 1, 2, 3 }, printed.Content);
    }
}
```
(Звірити конструктор `DocumentArchiveService` з `TestServices.Archive` і поправити за потреби.)

- [ ] **Step 2: Запустити — падає; реалізація**

`GenDoc/Services/ShellCommands.cs`:
```csharp
namespace GenDoc.Services
{
    public static class ShellCommands
    {
        // explorer.exe /select,"шлях" - відкриває теку з виділеним файлом.
        public static string ExplorerSelectArguments(string path) => $"/select,\"{path}\"";
    }
}
```
`ISecureTempFileService`: `Task PrintAsync(string fileName, byte[] content);` — реалізація в `SecureTempFileService` дублює `OpenAsync`, але `ProcessStartInfo { FileName = path, UseShellExecute = true, Verb = "print" }`. Спільний приватний `WriteTempAsync(fileName, content) → path`. `FakeTempFiles`: `public List<(string FileName, byte[] Content)> Printed { get; } = new();` + `PrintAsync` додає туди.
`IDocumentArchiveService` + `DocumentArchiveService`: `PrintAsync(int documentId)` — копія `OpenAsync` з `_tempFileService.PrintAsync(...)` і аудитом «Надруковано документ»; `PrintGroupAsync(int groupDocumentId)` — аналогічно до `OpenGroupAsync`.

`ArchiveViewModel`:
- залежність `IOutputFolderService _outputFolderService` (конструктор + DI вже є через контейнер — `ArchiveViewModel` singleton у DI).
- `public bool CanPrint => CheckedCount >= 1 && CheckedRows.All(r => r.HasContent);` додати в `NotifyPropertyChangedFor` списку `checkedCount`.
- `[ObservableProperty] private string? checkedRowDiskPath;` + `public bool CanShowInFolder => CheckedRowDiskPath is not null;` + `public string ShowInFolderTooltip => CheckedCount != 1 ? "Оберіть один документ" : CanShowInFolder ? CheckedRowDiskPath! : "Файл не збережено на диску в типовій теці";` У `RefreshCheckedState()` наприкінці: `_ = RefreshDiskPathAsync();`:
```csharp
        private async Task RefreshDiskPathAsync()
        {
            var row = CheckedCount == 1 ? CheckedRows.FirstOrDefault() : null;
            CheckedRowDiskPath = row is null ? null : await _outputFolderService.ResolveOnDiskAsync(row.Dto.FileName);
            OnPropertyChanged(nameof(CanShowInFolder));
            OnPropertyChanged(nameof(ShowInFolderTooltip));
        }
```
- команди:
```csharp
        [RelayCommand]
        private async Task PrintAsync()
        {
            foreach (var row in CheckedRows.Where(r => r.HasContent))
            {
                try
                {
                    var result = await _archiveService.PrintAsync(row.Id);
                    if (!result.Success) { MessageBox.Show(result.ErrorMessage, "Друк", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    MessageBox.Show("Не вдалося надрукувати: немає програми для .docx.", "Друк", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
        }

        [RelayCommand]
        private void ShowInFolder()
        {
            if (CheckedRowDiskPath is null) return;
            try { System.Diagnostics.Process.Start("explorer.exe", ShellCommands.ExplorerSelectArguments(CheckedRowDiskPath)); } catch { }
        }
```
Аналогічно для групових: `PrintGroupAsync` (по `CheckedGroupRows`), `GroupDiskPath`/`CanShowGroupInFolder`/`ShowGroupInFolder` з `RefreshGroupCheckedState`.
- XAML: у `WrapPanel` документів після «Відкрити» — `<Button Content="Друк" Style="{StaticResource ArchiveActionButtonStyle}" IsEnabled="{Binding CanPrint}" ToolTip="Оберіть документи з файлом" Command="{Binding PrintCommand}"/>` і `<Button Content="Показати в теці" Style="{StaticResource ArchiveActionButtonStyle}" IsEnabled="{Binding CanShowInFolder}" ToolTip="{Binding ShowInFolderTooltip}" Command="{Binding ShowInFolderCommand}"/>`; те саме у панелі «Групових» з `PrintGroupCommand`/`ShowGroupInFolderCommand`.
- Картка особи: у рядку документа поруч із «Відкрити» — `TextBlock Text="Друк"` з `MouseBinding` на `DataContext.PrintDocumentCommand` (видимий за `HasContent`); команда в `PersonCardViewModel`:
```csharp
        [RelayCommand]
        private async Task PrintDocumentAsync(RecipientDocRowViewModel? row)
        {
            if (row?.DocumentId is not int documentId) return;
            var result = await _archiveService.PrintAsync(documentId);
            if (!result.Success)
                MessageBox.Show(result.ErrorMessage, "Друк", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
```

- [ ] **Step 3: Тести, живий прогін, коміт**

Run: `dotnet test GenDoc.Tests\GenDoc.Tests.csproj -c Release`
Живий: в архіві позначити документ → «Друк» відкриває діалог друку Word; «Показати в теці» активна лише для документа, який є в типовій теці (згенерований у Task 8/10) і відкриває Провідник з виділеним файлом; для старих — неактивна з підказкою.
```bash
git add -A GenDoc GenDoc.Tests
git commit -m "2.5: «Друк» і «Показати в теці» в архіві та картці особи

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 12: 2.6 — порожні стани й дрібний шум

**Files:**
- Modify: `GenDoc/Views/Archive/ArchiveView.xaml:540-542`, `GenDoc/ViewModels/Archive/ArchiveViewModel.cs` (команда переходу)
- Modify: `GenDoc/Views/Personnel/PersonnelView.xaml:607-610`, `GenDoc/ViewModels/Personnel/PersonCardViewModel.cs` (`RoomDisplay`)
- Test: `GenDoc.Tests/Personnel/RoomDisplayTests.cs` (new)

- [ ] **Step 1: Тест відображення кімнати**

```csharp
using GenDoc.ViewModels.Personnel;

namespace GenDoc.Tests.Personnel;

public class RoomDisplayTests
{
    [Theory]
    [InlineData(null, "307", "307")]
    [InlineData("", "307", "307")]
    [InlineData("Б", "307", "Б / 307")]
    [InlineData(null, null, "-")]
    public void RoomDisplay_OmitsEmptyBuilding(string? building, string? number, string expected)
        => Assert.Equal(expected, PersonCardViewModel.FormatRoom(building, number));
}
```

- [ ] **Step 2: Реалізація**

`PersonCardViewModel`:
```csharp
        internal static string FormatRoom(string? building, string? number)
        {
            var b = building?.Trim(); var n = number?.Trim();
            if (string.IsNullOrEmpty(b) && string.IsNullOrEmpty(n)) return "-";
            if (string.IsNullOrEmpty(b)) return n!;
            if (string.IsNullOrEmpty(n)) return b;
            return $"{b} / {n}";
        }

        public string RoomDisplay => FormatRoom(RoomBuilding, RoomNumber);
```
і `[NotifyPropertyChangedFor(nameof(RoomDisplay))]` на `roomBuilding` та `roomNumber`. XAML: три `Run` (рядок 609) → `<TextBlock Text="{Binding RoomDisplay}" Style="{StaticResource CardReadValueStyle}"/>`.

«Запуски»: `TextBlock` «Запусків ще не було» замінити на `StackPanel` (центрований, `Visibility` той самий) із текстом і кнопкою `<Button Content="Перейти до генерації" Style="{StaticResource SecondaryButtonStyle}" Margin="0,10,0,0" HorizontalAlignment="Center" Command="{Binding GoToGenerationCommand}"/>`; у VM:
```csharp
        [RelayCommand]
        private void GoToGeneration()
            => WeakReferenceMessenger.Default.Send(new Services.Navigation.NavigateToSectionMessage(ViewModels.Shell.MainViewModel.GenerationSectionTitle, null));
```
(`using CommunityToolkit.Mvvm.Messaging;`.) Порожній архів за фільтрами вже має «Скинути фільтри» — без змін.

- [ ] **Step 3: Тести, живий прогін, коміт**

Живий: картка БЕНІШ → «Кімната» показує «307»; «Запуски» з фільтром, де запусків немає (напр. рік без запусків) → кнопка веде на «Генерацію».
```bash
git add -A GenDoc GenDoc.Tests
git commit -m "2.6: порожній стан «Запусків» веде до генерації; кімната без корпусу показується без «/»

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 13: Фінальна перевірка і спека

- [ ] **Step 1: Повний прогін тестів**

Run: `dotnet test GenDoc.Tests\GenDoc.Tests.csproj -c Release` — усі PASS; кількість тестів зафіксувати.

- [ ] **Step 2: Живий прохід чотирма сценаріями курсового** (на робочій базі): документ на 1 людину з картки; пакет на набір із підсумком; відомість (пакет «Зброя»/«Котлове») — результат у «Запусках»; знайти в архіві → «Друк» / «Показати в теці». Знімки в scratchpad.

- [ ] **Step 3: Дописати підсумок у спеку** `docs/superpowers/specs/2026-08-19-course-officer-quick-paths-design.md`: розділ «Підсумок реалізації» з хешами комітів по пунктах і тим, що лишилося живою перевіркою (друк — Word/Excel у системі).

- [ ] **Step 4: Коміт спеки**

```bash
git add docs/superpowers/specs/2026-08-19-course-officer-quick-paths-design.md
git commit -m "Спека: підсумок реалізації варіанта А

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```
