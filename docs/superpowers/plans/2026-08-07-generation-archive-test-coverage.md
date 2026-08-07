# Покриття тестами генерації шаблонів і архіву — план реалізації

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Покрити тестами генерацію шаблонів і архів документів, після чого виправити сім дефектів, які пояснюють «непонятні помилки».

**Architecture:** Спершу з'являється тестова інфраструктура на in-memory SQLite (`TestDb`) і фікстури зі справжніх шаблонів з теки `шаблони/`. Далі йдуть зелені тести — сітка безпеки, яка не змінює продакшн-код. Тести, що падають через дефект, додаються з `[Fact(Skip = "…")]` і посиланням на задачу-виправлення; зняття `Skip` є критерієм готовності тієї задачі. Наприкінці йдуть шість виправлень, кожне знімає свої `Skip`.

**Tech Stack:** .NET 8 (`net8.0-windows`), xUnit 2.5.3, EF Core + `Microsoft.Data.Sqlite` (провайдер `e_sqlcipher` без пароля працює як звичайний SQLite), ClosedXML, DocumentFormat.OpenXml.

## Global Constraints

- Цільовий фреймворк тестового проєкту — `net8.0-windows`; нових NuGet-пакетів не додавати.
- Коментарі й повідомлення для користувача — українською; назви типів, методів і тестів — англійською, як у решті кодової бази.
- Справжні шаблони читаються з теки `шаблони/` у корені репозиторію, без копіювання в `bin`.
- Ніколи не чіпати `AppDbContext.OnConfiguring` у продакшн-коді, `ShutdownMode`, пароль бази.
- Тести на міграції лишаються на рівні `DbConnection` (як `DatabaseSchemaInitializerTests`); `TestDb` створює схему через `EnsureCreated()` і для міграцій не використовується.
- Прогін усіх тестів: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj -v q --nologo`. Базовий стан до початку роботи — 185 passed, 0 failed.
- Кожна задача завершується комітом; набір тестів має бути зеленим на кожному коміті.

## Довідка: фактичний вміст справжніх шаблонів

Здобуто зондуванням файлів, використовувати як джерело істини для очікуваних значень.

| Файл | Рядок-шаблон | Теги в рядку-шаблоні |
|---|---|---|
| `Шаблон_Допуск_Додаток_5.xlsx` | **7** | `{{№}}`, `{{звання}}`, `{{піб}}`, `{{оцінка_1}}`…`{{оцінка_4}}`, `{{оцінка_загальна}}` |
| `Шаблон_Залік_Додаток_8.xlsx` | **10** | `{{№}}`, `{{звання}}`, `{{піб}}`, `{{дата_аркуша}}`, `{{причина_інструктажу}}`, `{{курсовий_офіцер}}` |
| `Шаблон_Роздавальна_відомість.xlsx` | **9** | `{{піб_ініціали}}`, `{{калібр}}`, `{{кількість_патронів}}`, `{{дата_аркуша}}` |

Теги поза рядком-шаблоном: Допуск — `{{номери_вправ}}`, `{{номер_вч}}`, `{{звання_начальника}}`, `{{піб_начальника}}`, `{{дата_аркуша}}`; Залік — `{{номер_вч}}` (рядок 5), `{{опис_підрозділу}}` (рядок 6), `{{звання_начальника}}`, `{{піб_начальника}}`, `{{дата_аркуша}}`; Роздавальна — `{{номер_відомості}}` (рядок 1).

`Шаблон_Рапорт_котлове_ГРУПОВИЙ (3).docx` — блок `{{#список}}` / `{{/список}}`, тіло блоку рівно один абзац `{{звання}}{{піб}}{{роздільник}}`. Поза блоком: `{{дата_прибуття}}`, `{{дата_зарахування}}`, `{{звання_підписанта}}`, `{{піб_підписанта}}`, `{{дата_рапорту}}`. Тегів `{{номер}}` і `{{кількість_осіб}}` у цьому файлі немає — їх тестувати на синтетичному документі.

`Шаблон_Рапорт_котлове_ІНДИВІДУАЛЬНИЙ.docx` — без блоку. Теги: `{{дата_прибуття}}`, `{{дата_зарахування}}`, `{{дата_рапорту}}`, `{{дата_посвідчення}}`, `{{номер_посвідчення}}`, `{{прод_атестат}}`, `{{звання_зв}}`, `{{піб_зв}}`, `{{прибув}}`, `{{таким}}`, `{{звання_підписанта}}`, `{{піб_підписанта}}`.

**Білий список тегів, які легітимно класифікуються як `Manual`** (використовується у тестах на класифікацію):
`{{дата_прибуття}}`, `{{дата_зарахування}}`, `{{дата_рапорту}}`, `{{звання_підписанта}}`, `{{піб_підписанта}}`, `{{дата_аркуша}}`, `{{період}}`, `{{номери_вправ}}`, `{{звання_начальника}}`, `{{піб_начальника}}`, `{{опис_підрозділу}}`, `{{причина_інструктажу}}`, `{{калібр}}`, `{{кількість_патронів}}`, `{{номер_відомості}}`.

## Структура файлів

**Створюються (тести):**
- `GenDoc.Tests/Infrastructure/TestDb.cs` — in-memory база + `IDbContextFactory<AppDbContext>`.
- `GenDoc.Tests/Infrastructure/Fakes.cs` — заглушки сервісів.
- `GenDoc.Tests/Infrastructure/TemplateFixtures.cs` — шляхи до справжніх шаблонів, білдери сутностей.
- `GenDoc.Tests/Archive/DocumentVersionChainTests.cs` — ланцюг версій персональних документів.
- `GenDoc.Tests/Archive/ArchiveExportAndFilterTests.cs` — експорт, фільтри, перегенерація.
- `GenDoc.Tests/Archive/GroupDocumentArchiveTests.cs` — групові документи, зелені й червоні.
- `GenDoc.Tests/Templates/TemplateScanRealFilesTests.cs` — скан справжніх DOCX.
- `GenDoc.Tests/Templates/ExportTemplateScanRealFilesTests.cs` — скан справжніх XLSX.
- `GenDoc.Tests/Generation/DocxRealTemplateTests.cs` — наскрізна генерація DOCX.
- `GenDoc.Tests/Generation/XlsxRealTemplateTests.cs` — наскрізна генерація XLSX.
- `GenDoc.Tests/Generation/PeriodSheetsTests.cs` — `ParsePeriodDates` і `repeatSheetPerDate`.
- `GenDoc.Tests/Generation/RunPackageTests.cs` — оркестрація, зелені.
- `GenDoc.Tests/Generation/RunPackageReportingTests.cs` — лічильники й помилки, червоні.

**Створюються (продакшн):**
- `GenDoc/Services/Generation/RunIssue.cs` — структурований запис про помилку запуску.

**Змінюються (продакшн, у задачах-виправленнях):**
- `GenDoc/Services/Documents/DocumentArchiveService.cs` — задачі 12, 13, 14.
- `GenDoc/Services/Documents/IDocumentArchiveService.cs` — задачі 12, 13.
- `GenDoc/Services/Documents/ArchiveModels.cs` — задача 12.
- `GenDoc/Services/Generation/GenerationService.cs` — задача 14.
- `GenDoc/Services/Generation/XlsxGenerationService.cs` — задача 15.
- `GenDoc/Services/Templates/TemplateService.cs` — задачі 16, 17.
- `GenDoc/Services/Generation/DocumentGenerationService.cs` — задача 17.
- `GenDoc/ViewModels/Archive/ArchiveViewModel.cs`, `VersionHistoryViewModel.cs`, `GroupVersionHistoryViewModel.cs` — задача 13.

---

### Task 1: Тестова інфраструктура

Знімає єдиний архітектурний ризик специфікації: чи піднімається `AppDbContext` на незашифрованому in-memory SQLite. Якщо `EnsureCreated()` не спрацює, решта задач архіву мусить перейти на рівень `DbConnection` — тому ця задача йде першою і окремо.

**Files:**
- Create: `GenDoc.Tests/Infrastructure/TestDb.cs`
- Create: `GenDoc.Tests/Infrastructure/Fakes.cs`
- Create: `GenDoc.Tests/Infrastructure/TemplateFixtures.cs`
- Test: `GenDoc.Tests/Infrastructure/TestDbSmokeTests.cs`

**Interfaces:**
- Consumes: `GenDoc.Data.AppDbContext`, `GenDoc.Data.IDbPasswordProvider`.
- Produces:
  - `TestDb : IDisposable` з `public IDbContextFactory<AppDbContext> Factory { get; }` і `public AppDbContext NewContext()`.
  - `FakeAuditLog : IAuditLogService` з `public List<string> Entries { get; }`.
  - `FakeCurrentUser : ICurrentUserContext` з конструктором `FakeCurrentUser(int userId = 1, string fullName = "Тест Тестович")`.
  - `FakeTempFiles : ISecureTempFileService` з `public List<(string FileName, byte[] Content)> Opened { get; }`.
  - `TemplateFixtures` — статичні властивості `DopuskXlsx`, `ZalikXlsx`, `RozdavalnaXlsx`, `AnketniXlsx`, `RaportGroupDocx`, `RaportIndividualDocx` (усі `string` — абсолютні шляхи), метод `static byte[] Bytes(string path)`, білдери `static Recipient Person(int id, string lastName, string firstName, string rank = "майор")` і `static List<Recipient> Roster(int count)`.

- [ ] **Step 1: Написати `TestDb`**

Create `GenDoc.Tests/Infrastructure/TestDb.cs`:

```csharp
using GenDoc.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Tests.Infrastructure;

// Незашифрована in-memory база для тестів сервісів. З'єднання мусить лишатись
// відкритим весь час життя TestDb — SQLite знищує in-memory базу, щойно
// закривається останнє з'єднання до неї.
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public TestDb()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var seed = new TestAppDbContext(_connection);
        seed.Database.EnsureCreated();

        Factory = new TestFactory(_connection);
    }

    public IDbContextFactory<AppDbContext> Factory { get; }

    public AppDbContext NewContext() => new TestAppDbContext(_connection);

    public void Dispose() => _connection.Dispose();

    private sealed class TestFactory : IDbContextFactory<AppDbContext>
    {
        private readonly SqliteConnection _connection;
        public TestFactory(SqliteConnection connection) => _connection = connection;
        public AppDbContext CreateDbContext() => new TestAppDbContext(_connection);
    }

    // Перевизначає OnConfiguring і НЕ викликає base — так гілка з паролем
    // і DbPaths.DatabasePath не виконується взагалі.
    private sealed class TestAppDbContext : AppDbContext
    {
        private readonly SqliteConnection _connection;

        public TestAppDbContext(SqliteConnection connection)
            : base(new NullPasswordProvider()) => _connection = connection;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseSqlite(_connection);
    }

    private sealed class NullPasswordProvider : IDbPasswordProvider
    {
        public string? Password => null;
        public void SetPassword(string password) { }
        public void Clear() { }
    }
}
```

- [ ] **Step 2: Написати смоук-тест**

Create `GenDoc.Tests/Infrastructure/TestDbSmokeTests.cs`:

```csharp
using GenDoc.Models;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests;

public class TestDbSmokeTests
{
    [Fact]
    public void TestDb_WritesAndReadsAcrossSeparateContexts()
    {
        using var db = new TestDb();

        using (var write = db.Factory.CreateDbContext())
        {
            write.Recipients.Add(new Recipient
            {
                LastName = "ШЕВЧЕНКО", FirstName = "Тарас",
                Rank = "майор", Position = "слухач", ServiceNumber = "12345"
            });
            write.SaveChanges();
        }

        using var read = db.Factory.CreateDbContext();
        var person = Assert.Single(read.Recipients.ToList());
        Assert.Equal("ШЕВЧЕНКО", person.LastName);
    }

    // Soft-delete query filter має працювати так само, як у продакшні.
    [Fact]
    public void TestDb_AppliesSoftDeleteQueryFilter()
    {
        using var db = new TestDb();

        using (var write = db.Factory.CreateDbContext())
        {
            write.Recipients.Add(new Recipient
            {
                LastName = "ВИДАЛЕНИЙ", FirstName = "Іван",
                Rank = "капітан", Position = "слухач", ServiceNumber = "1",
                DeletedAt = DateTime.Now
            });
            write.SaveChanges();
        }

        using var read = db.Factory.CreateDbContext();
        Assert.Empty(read.Recipients.ToList());
        Assert.Single(read.Recipients.IgnoreQueryFilters().ToList());
    }
}
```

- [ ] **Step 3: Прогнати смоук-тест — має пройти**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj --filter "FullyQualifiedName~TestDbSmokeTests" -v q --nologo`
Expected: PASS, 2 passed.

Якщо падає з помилкою про провайдер SQLite або про відсутню таблицю — це спрацював ризик зі специфікації. Зупинитись і повідомити; запасний план — тести архіву на рівні `DbConnection`, як у `DatabaseSchemaInitializerTests`.

- [ ] **Step 4: Написати заглушки**

Create `GenDoc.Tests/Infrastructure/Fakes.cs`:

```csharp
using GenDoc.Data;
using GenDoc.Services;
using GenDoc.Services.Documents;

namespace GenDoc.Tests.Infrastructure;

// Пише назви дій у список — тест може перевірити, що аудит-запис зроблено,
// не тягнучи справжню таблицю AuditLog.
public sealed class FakeAuditLog : IAuditLogService
{
    public List<string> Entries { get; } = new();

    public void LogCreate(AppDbContext db, string entityName, int entityId, string? newValue = null, string? details = null)
        => Entries.Add($"create:{entityName}:{entityId}");

    public void LogUpdate(AppDbContext db, string entityName, int entityId, string? oldValue, string? newValue, string? details = null)
        => Entries.Add($"update:{entityName}:{entityId}");

    public void LogDelete(AppDbContext db, string entityName, int entityId, string? oldValue = null, string? details = null)
        => Entries.Add($"delete:{entityName}:{entityId}");

    public void LogExport(AppDbContext db, string entityName, int count, string? details = null)
        => Entries.Add($"export:{entityName}:{count}");

    public void LogImport(AppDbContext db, string entityName, int count, string? details = null)
        => Entries.Add($"import:{entityName}:{count}");

    public void LogGenerate(AppDbContext db, string entityName, int entityId, string? details = null)
        => Entries.Add($"generate:{entityName}:{entityId}");

    public void Log(AppDbContext db, string action, string entityName, int entityId, string? oldValue = null, string? newValue = null, string? details = null)
        => Entries.Add($"{action}:{entityName}:{entityId}");
}

public sealed class FakeCurrentUser : ICurrentUserContext
{
    public FakeCurrentUser(int userId = 1, string fullName = "Тест Тестович")
    {
        CurrentUserId = userId;
        CurrentUserFullName = fullName;
    }

    public int? CurrentUserId { get; private set; }
    public string? CurrentUserFullName { get; private set; }

    public void SetCurrentUser(int userId, string fullName)
    {
        CurrentUserId = userId;
        CurrentUserFullName = fullName;
    }

    public void Clear()
    {
        CurrentUserId = null;
        CurrentUserFullName = null;
    }
}

// Нічого не пише на диск і нічого не запускає — лише запам'ятовує, що просили відкрити.
public sealed class FakeTempFiles : ISecureTempFileService
{
    public List<(string FileName, byte[] Content)> Opened { get; } = new();

    public Task OpenAsync(string fileName, byte[] content)
    {
        Opened.Add((fileName, content));
        return Task.CompletedTask;
    }

    public Task CleanupAsync() => Task.CompletedTask;
}
```

Водяний знак заглушувати не потрібно — у продакшні вже є `NoOpWatermarkService`, тести використовують його.

- [ ] **Step 5: Написати фікстури**

Create `GenDoc.Tests/Infrastructure/TemplateFixtures.cs`:

```csharp
using System.IO;
using GenDoc.Models;

namespace GenDoc.Tests.Infrastructure;

// Шляхи до справжніх шаблонів з теки «шаблони» в корені репозиторію.
// Свідомо БЕЗ копіювання у bin: якщо шаблон перейменували чи прибрали,
// тест має впасти одразу, а не мовчки працювати зі старою копією.
public static class TemplateFixtures
{
    public static string DopuskXlsx => Path(@"Шаблон_Допуск_Додаток_5.xlsx");
    public static string ZalikXlsx => Path(@"Шаблон_Залік_Додаток_8.xlsx");
    public static string RozdavalnaXlsx => Path(@"Шаблон_Роздавальна_відомість.xlsx");
    public static string AnketniXlsx => Path(@"АНКЕТНІ_ДАНІ_29_набір_з_кімнатами_та_зброєю (1).xlsx");
    public static string RaportGroupDocx => Path(@"Шаблон_Рапорт_котлове_ГРУПОВИЙ (3).docx");
    public static string RaportIndividualDocx => Path(@"Шаблон_Рапорт_котлове_ІНДИВІДУАЛЬНИЙ.docx");

    public static byte[] Bytes(string path)
    {
        Assert.True(File.Exists(path), $"Немає файлу шаблону: {path}");
        return File.ReadAllBytes(path);
    }

    private static string Path(string fileName)
        => System.IO.Path.Combine(RepoRoot(), "шаблони", fileName);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(System.IO.Path.Combine(dir.FullName, "шаблони")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Не знайдено теку «шаблони»");
    }

    public static Recipient Person(int id, string lastName, string firstName, string rank = "майор") => new()
    {
        Id = id,
        LastName = lastName,
        FirstName = firstName,
        MiddleName = "Петрович",
        Rank = rank,
        Position = "слухач",
        ServiceNumber = $"СН{id:0000}"
    };

    // Детермінований ростер: прізвища за абеткою, звання чергуються.
    public static List<Recipient> Roster(int count)
    {
        var ranks = new[] { "полковник", "майор", "капітан", "старший лейтенант" };
        return Enumerable.Range(1, count)
            .Select(i => Person(i, $"ПРІЗВИЩЕ{i:00}", $"Ім'я{i:00}", ranks[(i - 1) % ranks.Length]))
            .ToList();
    }
}
```

- [ ] **Step 6: Прогнати весь набір**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj -v q --nologo`
Expected: PASS, 187 passed (185 наявних + 2 смоук).

- [ ] **Step 7: Коміт**

```bash
git add GenDoc.Tests/Infrastructure
git commit -m "test: add in-memory SQLite harness, service fakes and template fixtures"
```

---

### Task 2: Тести ланцюга версій персональних документів

**Files:**
- Create: `GenDoc.Tests/Archive/DocumentVersionChainTests.cs`

**Interfaces:**
- Consumes: `TestDb`, `FakeAuditLog`, `FakeCurrentUser`, `FakeTempFiles` з Task 1; `DocumentArchiveService`, `NoOpWatermarkService`, `DocumentGenerationService`, `DocumentHashService`.
- Produces: приватний хелпер `BuildService(TestDb)` — повторюється в задачах 3 і 4 повним текстом, бо задачі можуть виконуватись різними людьми поза порядком.

- [ ] **Step 1: Написати тести**

Create `GenDoc.Tests/Archive/DocumentVersionChainTests.cs`:

```csharp
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Documents;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Tests.Archive;

// Інваріант усього архіву: для пари (людина, шаблон) серед ЖИВИХ записів
// має бути рівно один IsCurrent. Кожна операція перевіряється саме на це.
public class DocumentVersionChainTests
{
    private static DocumentArchiveService BuildService(TestDb db) => new(
        db.Factory,
        new FakeAuditLog(),
        new FakeCurrentUser(),
        new FakeTempFiles(),
        new NoOpWatermarkService(),
        new DocumentGenerationService(),
        new DocumentHashService());

    // Створює людину, шаблон і `versions` версій документа для їхньої пари.
    // Актуальною лишається найвища версія.
    private static (int RecipientId, int TemplateId, List<int> DocumentIds) Seed(TestDb db, int versions)
    {
        using var ctx = db.Factory.CreateDbContext();

        var person = new Recipient
        {
            LastName = "ШЕВЧЕНКО", FirstName = "Тарас",
            Rank = "майор", Position = "слухач", ServiceNumber = "1"
        };
        var template = new Template
        {
            Name = "Рапорт", OriginalFileName = "r.docx",
            Content = Array.Empty<byte>(), UploadedAt = DateTime.Now
        };
        ctx.Recipients.Add(person);
        ctx.Templates.Add(template);
        ctx.SaveChanges();

        var ids = new List<int>();
        for (var v = 1; v <= versions; v++)
        {
            var doc = new GeneratedDocument
            {
                RecipientId = person.Id,
                TemplateId = template.Id,
                GeneratedAt = DateTime.Now.AddMinutes(v),
                GeneratedByUserId = 1,
                FileName = $"rapport-v{v}.docx",
                SizeBytes = 10,
                Version = v,
                IsCurrent = v == versions,
                SourceType = DocumentSourceType.Generated,
                HasContent = true,
                Content = new GeneratedDocumentContent { Content = new byte[] { 1, 2, 3 } }
            };
            ctx.GeneratedDocuments.Add(doc);
            ctx.SaveChanges();
            ids.Add(doc.Id);
        }

        return (person.Id, template.Id, ids);
    }

    private static void AssertExactlyOneCurrent(TestDb db, int recipientId, int templateId)
    {
        using var ctx = db.Factory.CreateDbContext();
        var current = ctx.GeneratedDocuments
            .Where(g => g.RecipientId == recipientId && g.TemplateId == templateId && g.IsCurrent)
            .ToList();
        Assert.Single(current);
    }

    [Fact]
    public async Task GetVersionsAsync_ReturnsNewestFirst()
    {
        using var db = new TestDb();
        var (recipientId, templateId, _) = Seed(db, versions: 3);

        var versions = await BuildService(db).GetVersionsAsync(recipientId, templateId);

        Assert.Equal(new[] { 3, 2, 1 }, versions.Select(v => v.Version).ToArray());
        Assert.True(versions[0].IsCurrent);
        Assert.False(versions[1].IsCurrent);
    }

    [Fact]
    public async Task MakeCurrentAsync_MovesCurrentFlagToChosenVersion()
    {
        using var db = new TestDb();
        var (recipientId, templateId, ids) = Seed(db, versions: 3);

        await BuildService(db).MakeCurrentAsync(ids[0]); // версія 1

        AssertExactlyOneCurrent(db, recipientId, templateId);
        using var ctx = db.Factory.CreateDbContext();
        Assert.True(ctx.GeneratedDocuments.First(g => g.Id == ids[0]).IsCurrent);
    }

    // Видалення актуальної версії має підняти попередню живу, інакше анти-дубль
    // генерації вважатиме пару вільною і сформує документ наново.
    [Fact]
    public async Task DeleteAsync_PromotesPreviousVersionToCurrent()
    {
        using var db = new TestDb();
        var (recipientId, templateId, ids) = Seed(db, versions: 3);

        await BuildService(db).DeleteAsync(new[] { ids[2] }); // видаляємо версію 3

        AssertExactlyOneCurrent(db, recipientId, templateId);
        using var ctx = db.Factory.CreateDbContext();
        Assert.True(ctx.GeneratedDocuments.First(g => g.Id == ids[1]).IsCurrent);
        Assert.NotNull(ctx.GeneratedDocuments.IgnoreQueryFilters().First(g => g.Id == ids[2]).DeletedAt);
    }

    [Fact]
    public async Task DeleteAsync_OnlyRemainingVersion_LeavesPairWithoutCurrent()
    {
        using var db = new TestDb();
        var (recipientId, templateId, ids) = Seed(db, versions: 1);

        await BuildService(db).DeleteAsync(new[] { ids[0] });

        using var ctx = db.Factory.CreateDbContext();
        Assert.Empty(ctx.GeneratedDocuments
            .Where(g => g.RecipientId == recipientId && g.TemplateId == templateId)
            .ToList());
    }

    [Fact]
    public async Task RestoreAsync_HighestVersion_BecomesCurrentAgain()
    {
        using var db = new TestDb();
        var (recipientId, templateId, ids) = Seed(db, versions: 3);
        var service = BuildService(db);

        await service.DeleteAsync(new[] { ids[2] });
        await service.RestoreAsync(ids[2]);

        AssertExactlyOneCurrent(db, recipientId, templateId);
        using var ctx = db.Factory.CreateDbContext();
        Assert.True(ctx.GeneratedDocuments.First(g => g.Id == ids[2]).IsCurrent);
    }

    // Відновлення НЕ найвищої версії не повинно відбирати актуальність у новішої.
    [Fact]
    public async Task RestoreAsync_LowerVersion_DoesNotStealCurrentFlag()
    {
        using var db = new TestDb();
        var (recipientId, templateId, ids) = Seed(db, versions: 3);
        var service = BuildService(db);

        await service.DeleteAsync(new[] { ids[0] }); // версія 1, не актуальна
        await service.RestoreAsync(ids[0]);

        AssertExactlyOneCurrent(db, recipientId, templateId);
        using var ctx = db.Factory.CreateDbContext();
        Assert.True(ctx.GeneratedDocuments.First(g => g.Id == ids[2]).IsCurrent);
        Assert.False(ctx.GeneratedDocuments.First(g => g.Id == ids[0]).IsCurrent);
    }

    [Fact]
    public async Task UploadManualAsync_AddsVersionWithManualUploadSourceType()
    {
        using var db = new TestDb();
        var (recipientId, templateId, ids) = Seed(db, versions: 1);

        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.docx");
        await File.WriteAllBytesAsync(path, new byte[] { 9, 9, 9 });
        try
        {
            var result = await BuildService(db).UploadManualAsync(ids[0], path, "нотатка");
            Assert.True(result.Success, result.ErrorMessage);
        }
        finally
        {
            File.Delete(path);
        }

        AssertExactlyOneCurrent(db, recipientId, templateId);
        using var ctx = db.Factory.CreateDbContext();
        var newest = ctx.GeneratedDocuments
            .Where(g => g.RecipientId == recipientId && g.TemplateId == templateId)
            .OrderByDescending(g => g.Version).First();
        Assert.Equal(2, newest.Version);
        Assert.Equal(DocumentSourceType.ManualUpload, newest.SourceType);
    }

    [Fact]
    public async Task GetDeletedDocumentsAsync_ListsOnlySoftDeleted()
    {
        using var db = new TestDb();
        var (_, _, ids) = Seed(db, versions: 2);
        var service = BuildService(db);

        await service.DeleteAsync(new[] { ids[1] });

        var deleted = await service.GetDeletedDocumentsAsync();
        Assert.Single(deleted);
        Assert.Equal(2, deleted[0].Version);
    }
}
```

- [ ] **Step 2: Прогнати — усі мають пройти**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj --filter "FullyQualifiedName~DocumentVersionChainTests" -v q --nologo`
Expected: PASS, 8 passed.

Якщо `DeleteAsync_OnlyRemainingVersion_LeavesPairWithoutCurrent` падає — прочитати повідомлення і повідомити: це означає ще один дефект, не передбачений специфікацією.

- [ ] **Step 3: Прогнати весь набір і закомітити**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj -v q --nologo`
Expected: PASS, 195 passed.

```bash
git add GenDoc.Tests/Archive/DocumentVersionChainTests.cs
git commit -m "test: cover personal document version chain in the archive"
```

---

### Task 3: Тести експорту, фільтрів і перегенерації архіву

**Files:**
- Create: `GenDoc.Tests/Archive/ArchiveExportAndFilterTests.cs`

**Interfaces:**
- Consumes: `TestDb`, `FakeAuditLog`, `FakeCurrentUser`, `FakeTempFiles`, `TemplateFixtures` з Task 1.
- Produces: нічого для наступних задач.

- [ ] **Step 1: Написати тести**

Create `GenDoc.Tests/Archive/ArchiveExportAndFilterTests.cs`:

```csharp
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Documents;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Archive;

public class ArchiveExportAndFilterTests : IDisposable
{
    private readonly string _outputFolder =
        Path.Combine(Path.GetTempPath(), $"gendoc-archive-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_outputFolder)) Directory.Delete(_outputFolder, recursive: true);
    }

    private static DocumentArchiveService BuildService(TestDb db) => new(
        db.Factory,
        new FakeAuditLog(),
        new FakeCurrentUser(),
        new FakeTempFiles(),
        new NoOpWatermarkService(),
        new DocumentGenerationService(),
        new DocumentHashService());

    // Дві людини з однаковим ПІБ у різних гілках дерева — перевіряє і підтеки
    // з OrgPathSnapshot, і розв'язання колізії імен файлів.
    private static List<int> SeedTwoDocumentsWithSameName(TestDb db, string? orgPathA, string? orgPathB)
    {
        using var ctx = db.Factory.CreateDbContext();

        var template = new Template
        {
            Name = "Рапорт", OriginalFileName = "r.docx",
            Content = Array.Empty<byte>(), UploadedAt = DateTime.Now
        };
        ctx.Templates.Add(template);

        var ids = new List<int>();
        foreach (var orgPath in new[] { orgPathA, orgPathB })
        {
            var person = new Recipient
            {
                LastName = "ШЕВЧЕНКО", FirstName = "Тарас", MiddleName = "Григорович",
                Rank = "майор", Position = "слухач", ServiceNumber = Guid.NewGuid().ToString("N")[..5]
            };
            ctx.Recipients.Add(person);
            ctx.SaveChanges();

            var doc = new GeneratedDocument
            {
                RecipientId = person.Id,
                TemplateId = template.Id,
                GeneratedAt = new DateTime(2026, 3, 15),
                GeneratedByUserId = 1,
                FileName = "rapport.docx",
                SizeBytes = 3,
                Version = 1,
                IsCurrent = true,
                SourceType = DocumentSourceType.Generated,
                OrgPathSnapshot = orgPath,
                HasContent = true,
                Content = new GeneratedDocumentContent { Content = new byte[] { 1, 2, 3 } }
            };
            ctx.GeneratedDocuments.Add(doc);
            ctx.SaveChanges();
            ids.Add(doc.Id);
        }

        return ids;
    }

    [Fact]
    public async Task SaveManyAsync_LaysDocumentsOutByOrgPathSnapshot()
    {
        using var db = new TestDb();
        var ids = SeedTwoDocumentsWithSameName(db, "Курс 1 / Взвод 2", "Курс 3");

        var (saved, errors) = await BuildService(db).SaveManyAsync(ids, _outputFolder);

        Assert.Equal(2, saved);
        Assert.Empty(errors);
        Assert.True(Directory.Exists(Path.Combine(_outputFolder, "Курс 1", "Взвод 2")));
        Assert.True(Directory.Exists(Path.Combine(_outputFolder, "Курс 3")));
    }

    // Однакова підтека + однакове ім'я → друге має отримати суфікс, а не затерти перше.
    [Fact]
    public async Task SaveManyAsync_NameCollisionInSameFolder_AddsNumericSuffix()
    {
        using var db = new TestDb();
        var ids = SeedTwoDocumentsWithSameName(db, "Курс 1", "Курс 1");

        var (saved, errors) = await BuildService(db).SaveManyAsync(ids, _outputFolder);

        Assert.Equal(2, saved);
        Assert.Empty(errors);
        var files = Directory.GetFiles(Path.Combine(_outputFolder, "Курс 1"));
        Assert.Equal(2, files.Length);
        Assert.Contains(files, f => Path.GetFileNameWithoutExtension(f).EndsWith("_2", StringComparison.Ordinal));
    }

    // Легасі-запис без збереженого вмісту має дати рядок помилки, а не впасти.
    [Fact]
    public async Task SaveManyAsync_DocumentWithoutStoredContent_ReportsErrorLineInsteadOfThrowing()
    {
        using var db = new TestDb();
        int docId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var person = new Recipient
            {
                LastName = "БЕЗВМІСТУ", FirstName = "Іван",
                Rank = "капітан", Position = "слухач", ServiceNumber = "7"
            };
            var template = new Template
            {
                Name = "Рапорт", OriginalFileName = "r.docx",
                Content = Array.Empty<byte>(), UploadedAt = DateTime.Now
            };
            ctx.Recipients.Add(person);
            ctx.Templates.Add(template);
            ctx.SaveChanges();

            var doc = new GeneratedDocument
            {
                RecipientId = person.Id, TemplateId = template.Id,
                GeneratedAt = DateTime.Now, GeneratedByUserId = 1,
                FileName = "no-content.docx", SizeBytes = 0,
                Version = 1, IsCurrent = true,
                SourceType = DocumentSourceType.Generated,
                HasContent = false
            };
            ctx.GeneratedDocuments.Add(doc);
            ctx.SaveChanges();
            docId = doc.Id;
        }

        var (saved, errors) = await BuildService(db).SaveManyAsync(new[] { docId }, _outputFolder);

        Assert.Equal(0, saved);
        Assert.Single(errors);
        Assert.Contains("не збережено в архіві", errors[0]);
    }

    [Fact]
    public async Task RegenerateAsync_DeletedTemplate_ReturnsHumanReadableFailure()
    {
        using var db = new TestDb();
        int docId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var person = new Recipient
            {
                LastName = "ШЕВЧЕНКО", FirstName = "Тарас",
                Rank = "майор", Position = "слухач", ServiceNumber = "1"
            };
            var template = new Template
            {
                Name = "Рапорт", OriginalFileName = "r.docx",
                Content = Array.Empty<byte>(), UploadedAt = DateTime.Now,
                DeletedAt = DateTime.Now
            };
            ctx.Recipients.Add(person);
            ctx.Templates.Add(template);
            ctx.SaveChanges();

            var doc = new GeneratedDocument
            {
                RecipientId = person.Id, TemplateId = template.Id,
                GeneratedAt = DateTime.Now, GeneratedByUserId = 1,
                FileName = "r.docx", SizeBytes = 1, Version = 1, IsCurrent = true,
                SourceType = DocumentSourceType.Generated, HasContent = true,
                Content = new GeneratedDocumentContent { Content = new byte[] { 1 } }
            };
            ctx.GeneratedDocuments.Add(doc);
            ctx.SaveChanges();
            docId = doc.Id;
        }

        var result = await BuildService(db).RegenerateAsync(docId, new Dictionary<string, string>());

        Assert.False(result.Success);
        Assert.Equal("Шаблон видалено — перегенерація неможлива", result.ErrorMessage);
    }

    [Fact]
    public async Task QueryAsync_FiltersByYearAndTemplate()
    {
        using var db = new TestDb();
        int templateA, templateB;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var person = new Recipient
            {
                LastName = "ШЕВЧЕНКО", FirstName = "Тарас",
                Rank = "майор", Position = "слухач", ServiceNumber = "1"
            };
            var a = new Template { Name = "А", OriginalFileName = "a.docx", Content = Array.Empty<byte>(), UploadedAt = DateTime.Now };
            var b = new Template { Name = "Б", OriginalFileName = "b.docx", Content = Array.Empty<byte>(), UploadedAt = DateTime.Now };
            ctx.Recipients.Add(person);
            ctx.Templates.AddRange(a, b);
            ctx.SaveChanges();
            templateA = a.Id;
            templateB = b.Id;

            ctx.GeneratedDocuments.AddRange(
                new GeneratedDocument
                {
                    RecipientId = person.Id, TemplateId = a.Id,
                    GeneratedAt = new DateTime(2025, 5, 1), GeneratedByUserId = 1,
                    FileName = "a-2025.docx", SizeBytes = 1, Version = 1, IsCurrent = true,
                    SourceType = DocumentSourceType.Generated, HasContent = true
                },
                new GeneratedDocument
                {
                    RecipientId = person.Id, TemplateId = b.Id,
                    GeneratedAt = new DateTime(2026, 5, 1), GeneratedByUserId = 1,
                    FileName = "b-2026.docx", SizeBytes = 1, Version = 1, IsCurrent = true,
                    SourceType = DocumentSourceType.Generated, HasContent = true
                });
            ctx.SaveChanges();
        }

        var service = BuildService(db);

        var byYear = await service.QueryAsync(new ArchiveFilter(null, null, null, null, 2026, 0, 50));
        Assert.Equal("b-2026.docx", Assert.Single(byYear).FileName);

        var byTemplate = await service.QueryAsync(new ArchiveFilter(null, templateA, null, null, null, 0, 50));
        Assert.Equal("a-2025.docx", Assert.Single(byTemplate).FileName);

        var stats = await service.GetStatsAsync(new ArchiveFilter(null, templateB, null, null, null, 0, 50));
        Assert.Equal(1, stats.Count);
    }
}
```

- [ ] **Step 2: Прогнати — усі мають пройти**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj --filter "FullyQualifiedName~ArchiveExportAndFilterTests" -v q --nologo`
Expected: PASS, 5 passed.

- [ ] **Step 3: Прогнати весь набір і закомітити**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj -v q --nologo`
Expected: PASS, 200 passed.

```bash
git add GenDoc.Tests/Archive/ArchiveExportAndFilterTests.cs
git commit -m "test: cover archive export layout, filters and regeneration failure"
```

---

### Task 4: Тести групових документів архіву (зелені XLSX + червоні DOCX)

Це задача, яка фіксує дефекти A1, A2 і A3. Червоні тести додаються зі `Skip`, який знімається в задачах 12 і 13.

**Files:**
- Create: `GenDoc.Tests/Archive/GroupDocumentArchiveTests.cs`

**Interfaces:**
- Consumes: `TestDb`, `FakeAuditLog`, `FakeCurrentUser`, `FakeTempFiles` з Task 1.
- Produces: нічого для наступних задач.

- [ ] **Step 1: Написати тести**

Create `GenDoc.Tests/Archive/GroupDocumentArchiveTests.cs`:

```csharp
using GenDoc.Models;
using GenDoc.Services.Documents;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Tests.Archive;

// Групові документи бувають двох ґатунків: XLSX-відомість (ExportTemplateId
// заповнено, TemplateId — null) і груповий DOCX (навпаки). Ланцюг версій
// мусить розрізняти їх обидва.
public class GroupDocumentArchiveTests
{
    private static DocumentArchiveService BuildService(TestDb db) => new(
        db.Factory,
        new FakeAuditLog(),
        new FakeCurrentUser(),
        new FakeTempFiles(),
        new NoOpWatermarkService(),
        new DocumentGenerationService(),
        new DocumentHashService());

    private static int AddExportTemplate(TestDb db, string name)
    {
        using var ctx = db.Factory.CreateDbContext();
        var template = new ExportTemplate
        {
            Name = name, OriginalFileName = $"{name}.xlsx",
            Content = Array.Empty<byte>(), UploadedAt = DateTime.Now
        };
        ctx.ExportTemplates.Add(template);
        ctx.SaveChanges();
        return template.Id;
    }

    private static int AddDocxTemplate(TestDb db, string name)
    {
        using var ctx = db.Factory.CreateDbContext();
        var template = new Template
        {
            Name = name, OriginalFileName = $"{name}.docx",
            Content = Array.Empty<byte>(), UploadedAt = DateTime.Now,
            Kind = Models.Enums.TemplateKind.Group
        };
        ctx.Templates.Add(template);
        ctx.SaveChanges();
        return template.Id;
    }

    private static int AddGroupDocument(
        TestDb db, int? exportTemplateId, int? templateId, int version, bool isCurrent,
        bool hasContent = true)
    {
        using var ctx = db.Factory.CreateDbContext();
        var doc = new GeneratedGroupDocument
        {
            ExportTemplateId = exportTemplateId,
            TemplateId = templateId,
            GeneratedAt = DateTime.Now.AddMinutes(version),
            GeneratedByUserId = 1,
            FileName = $"group-v{version}.xlsx",
            SizeBytes = 5,
            RecipientCount = 3,
            Version = version,
            IsCurrent = isCurrent,
            HasContent = hasContent,
            Content = hasContent ? new GeneratedGroupDocumentContent { Content = new byte[] { 1, 2 } } : null
        };
        ctx.GeneratedGroupDocuments.Add(doc);
        ctx.SaveChanges();
        return doc.Id;
    }

    // ── Зелені: XLSX-відомості ──────────────────────────────────────

    [Fact]
    public async Task DeleteGroupAsync_Xlsx_PromotesPreviousVersionOfTheSameTemplate()
    {
        using var db = new TestDb();
        var templateId = AddExportTemplate(db, "Залік");
        var v1 = AddGroupDocument(db, templateId, null, version: 1, isCurrent: false);
        var v2 = AddGroupDocument(db, templateId, null, version: 2, isCurrent: true);

        await BuildService(db).DeleteGroupAsync(new[] { v2 });

        using var ctx = db.Factory.CreateDbContext();
        Assert.True(ctx.GeneratedGroupDocuments.First(g => g.Id == v1).IsCurrent);
        Assert.NotNull(ctx.GeneratedGroupDocuments.IgnoreQueryFilters().First(g => g.Id == v2).DeletedAt);
    }

    // Видалення відомості одного XLSX-шаблону не повинно чіпати інший шаблон.
    [Fact]
    public async Task DeleteGroupAsync_Xlsx_DoesNotTouchOtherTemplate()
    {
        using var db = new TestDb();
        var zalik = AddExportTemplate(db, "Залік");
        var dopusk = AddExportTemplate(db, "Допуск");
        var zalikV1 = AddGroupDocument(db, zalik, null, version: 1, isCurrent: false);
        var zalikV2 = AddGroupDocument(db, zalik, null, version: 2, isCurrent: true);
        var dopuskV1 = AddGroupDocument(db, dopusk, null, version: 1, isCurrent: true);

        await BuildService(db).DeleteGroupAsync(new[] { zalikV2 });

        using var ctx = db.Factory.CreateDbContext();
        Assert.True(ctx.GeneratedGroupDocuments.First(g => g.Id == zalikV1).IsCurrent);
        Assert.True(ctx.GeneratedGroupDocuments.First(g => g.Id == dopuskV1).IsCurrent);
    }

    [Fact]
    public async Task MakeGroupCurrentAsync_Xlsx_MovesFlagWithinTemplate()
    {
        using var db = new TestDb();
        var templateId = AddExportTemplate(db, "Залік");
        var v1 = AddGroupDocument(db, templateId, null, version: 1, isCurrent: false);
        var v2 = AddGroupDocument(db, templateId, null, version: 2, isCurrent: true);

        await BuildService(db).MakeGroupCurrentAsync(v1);

        using var ctx = db.Factory.CreateDbContext();
        Assert.True(ctx.GeneratedGroupDocuments.First(g => g.Id == v1).IsCurrent);
        Assert.False(ctx.GeneratedGroupDocuments.First(g => g.Id == v2).IsCurrent);
    }

    // ── Червоні: груповий DOCX (дефект A1) ──────────────────────────

    // ExportTemplateId у групового DOCX — NULL, і умова `g.ExportTemplateId ==
    // doc.ExportTemplateId` перекладається в `IS NULL`, тобто зачіпає всі
    // групові DOCX усіх шаблонів одразу.
    [Fact(Skip = "Червоний до Task 12 — ланцюг версій групового DOCX ключується на NULL")]
    public async Task DeleteGroupAsync_Docx_DoesNotTouchOtherDocxTemplate()
    {
        using var db = new TestDb();
        var rapportA = AddDocxTemplate(db, "Рапорт А");
        var rapportB = AddDocxTemplate(db, "Рапорт Б");
        var aV1 = AddGroupDocument(db, null, rapportA, version: 1, isCurrent: false);
        var aV2 = AddGroupDocument(db, null, rapportA, version: 2, isCurrent: true);
        var bV1 = AddGroupDocument(db, null, rapportB, version: 1, isCurrent: true);

        await BuildService(db).DeleteGroupAsync(new[] { aV2 });

        using var ctx = db.Factory.CreateDbContext();
        Assert.True(ctx.GeneratedGroupDocuments.First(g => g.Id == aV1).IsCurrent);
        Assert.True(ctx.GeneratedGroupDocuments.First(g => g.Id == bV1).IsCurrent);
    }

    [Fact(Skip = "Червоний до Task 12 — RestoreGroupAsync ключується на NULL ExportTemplateId")]
    public async Task RestoreGroupAsync_Docx_DoesNotStealCurrentFlagFromOtherTemplate()
    {
        using var db = new TestDb();
        var rapportA = AddDocxTemplate(db, "Рапорт А");
        var rapportB = AddDocxTemplate(db, "Рапорт Б");
        var aV1 = AddGroupDocument(db, null, rapportA, version: 1, isCurrent: true);
        var bV1 = AddGroupDocument(db, null, rapportB, version: 1, isCurrent: true);

        var service = BuildService(db);
        await service.DeleteGroupAsync(new[] { aV1 });
        await service.RestoreGroupAsync(aV1);

        using var ctx = db.Factory.CreateDbContext();
        Assert.True(ctx.GeneratedGroupDocuments.First(g => g.Id == aV1).IsCurrent);
        Assert.True(ctx.GeneratedGroupDocuments.First(g => g.Id == bV1).IsCurrent);
    }

    // Дефект A2: DOCX-групи віддаються з ExportTemplateId ?? 0 і назвою «—».
    [Fact(Skip = "Червоний до Task 12 — QueryGroupAsync не показує групові DOCX")]
    public async Task QueryGroupAsync_ReturnsDocxGroupsWithTheirTemplateName()
    {
        using var db = new TestDb();
        var rapport = AddDocxTemplate(db, "Рапорт котлове");
        AddGroupDocument(db, null, rapport, version: 1, isCurrent: true);

        var rows = await BuildService(db).QueryGroupAsync(new GroupArchiveFilter(null, null, 0, 50));

        var row = Assert.Single(rows);
        Assert.Equal("Рапорт котлове", row.TemplateName);
        Assert.True(row.TemplateAlive);
    }

    // Дефект A3: FirstAsync по таблиці вмісту кидає «Sequence contains no elements».
    [Fact(Skip = "Червоний до Task 13 — запис без вмісту має давати ArchiveOpResult, а не виняток")]
    public async Task OpenGroupAsync_DocumentWithoutStoredContent_ReturnsFailureInsteadOfThrowing()
    {
        using var db = new TestDb();
        var templateId = AddExportTemplate(db, "Залік");
        var docId = AddGroupDocument(db, templateId, null, version: 1, isCurrent: true, hasContent: false);

        var result = await BuildService(db).OpenGroupAsync(docId);

        Assert.False(result.Success);
        Assert.Contains("не збережено в архіві", result.ErrorMessage);
    }
}
```

- [ ] **Step 2: Прогнати — зелені проходять, червоні пропущені**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj --filter "FullyQualifiedName~GroupDocumentArchiveTests" -v q --nologo`
Expected: PASS, 3 passed, 4 skipped.

`OpenGroupAsync_DocumentWithoutStoredContent_ReturnsFailureInsteadOfThrowing` не компілюється, бо `OpenGroupAsync` наразі повертає `Task`, а не `Task<ArchiveOpResult>`. Щоб набір збирався до Task 13, тимчасово оформити тіло цього тесту так (сам тест лишається `Skip`, тому не виконується):

```csharp
        var service = BuildService(db);
        // Сигнатуру змінює Task 13; до того часу фіксуємо лише поточну поведінку-виняток.
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.OpenGroupAsync(docId));
```

Після Task 13 тіло замінюється на варіант з `ArchiveOpResult`, а `Skip` знімається.

- [ ] **Step 3: Прогнати весь набір і закомітити**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj -v q --nologo`
Expected: PASS, 203 passed, 4 skipped.

```bash
git add GenDoc.Tests/Archive/GroupDocumentArchiveTests.cs
git commit -m "test: cover group document version chain, incl. failing docx cases"
```

---

### Task 5: Тести скану справжніх DOCX-шаблонів

**Files:**
- Create: `GenDoc.Tests/Templates/TemplateScanRealFilesTests.cs`

**Interfaces:**
- Consumes: `TemplateFixtures` з Task 1; `TemplateService.ScanPlaceholders(WordprocessingDocument)` (internal, доступний тестам), `PlaceholderTagMaps.Classify(string)`.
- Produces: константа `TemplateScanRealFilesTests.ManualTagWhitelist` — тільки для цього файлу.

- [ ] **Step 1: Написати тести**

Create `GenDoc.Tests/Templates/TemplateScanRealFilesTests.cs`:

```csharp
using DocumentFormat.OpenXml.Packaging;
using GenDoc.Models.Enums;
using GenDoc.Services.Templates;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Templates;

// Пастка, через яку колонка мовчки лишалась порожньою: новий {{тег}} у шаблоні,
// якого нема в PlaceholderTagMaps, класифікується як Manual — і ніде не видно,
// що це помилка. Тест робить цю ситуацію гучною.
public class TemplateScanRealFilesTests
{
    // Теги, які СПРАВДІ заповнює людина руками — легітимний Manual.
    private static readonly HashSet<string> ManualTagWhitelist = new(StringComparer.Ordinal)
    {
        "{{дата_прибуття}}", "{{дата_зарахування}}", "{{дата_рапорту}}",
        "{{звання_підписанта}}", "{{піб_підписанта}}",
        "{{дата_аркуша}}", "{{період}}",
        "{{номери_вправ}}", "{{звання_начальника}}", "{{піб_начальника}}",
        "{{опис_підрозділу}}", "{{причина_інструктажу}}",
        "{{калібр}}", "{{кількість_патронів}}", "{{номер_відомості}}"
    };

    private static TemplateService.ScanResult Scan(string path)
    {
        using var stream = new MemoryStream(TemplateFixtures.Bytes(path));
        using var doc = WordprocessingDocument.Open(stream, false);
        return TemplateService.ScanPlaceholders(doc);
    }

    [Fact]
    public void GroupRaport_IsDetectedAsRepeatingBlockTemplate()
    {
        var scan = Scan(TemplateFixtures.RaportGroupDocx);

        Assert.True(scan.HasBlock);

        var tags = scan.Tags.ToDictionary(t => t.Tag, t => t.IsInsideBlock, StringComparer.Ordinal);

        // Тіло блоку — рівно один абзац «{{звання}}{{піб}}{{роздільник}}».
        Assert.True(tags["{{звання}}"]);
        Assert.True(tags["{{піб}}"]);

        // Ці стоять поза блоком і мають лишитись спільними для всього документа.
        Assert.False(tags["{{дата_прибуття}}"]);
        Assert.False(tags["{{дата_зарахування}}"]);
        Assert.False(tags["{{дата_рапорту}}"]);
        Assert.False(tags["{{звання_підписанта}}"]);
        Assert.False(tags["{{піб_підписанта}}"]);
    }

    // {{роздільник}} обчислює рушій блоків — у мапінг він потрапляти не має,
    // інакше з'явиться зайвий рядок у формі ручних тегів.
    [Fact]
    public void GroupRaport_DoesNotMapReservedBlockEngineTags()
    {
        var scan = Scan(TemplateFixtures.RaportGroupDocx);

        Assert.DoesNotContain(scan.Tags, t => t.Tag == "{{роздільник}}");
        Assert.DoesNotContain(scan.Tags, t => t.Tag == "{{номер}}");
        Assert.DoesNotContain(scan.Tags, t => t.Tag == "{{кількість_осіб}}");
    }

    [Fact]
    public void IndividualRaport_HasNoRepeatingBlock()
    {
        var scan = Scan(TemplateFixtures.RaportIndividualDocx);

        Assert.False(scan.HasBlock);
        Assert.All(scan.Tags, t => Assert.False(t.IsInsideBlock));
    }

    [Fact]
    public void IndividualRaport_ContainsExpectedRecipientTags()
    {
        var scan = Scan(TemplateFixtures.RaportIndividualDocx);
        var tags = scan.Tags.Select(t => t.Tag).ToHashSet(StringComparer.Ordinal);

        foreach (var expected in new[]
                 {
                     "{{звання_зв}}", "{{піб_зв}}", "{{прибув}}", "{{таким}}",
                     "{{номер_посвідчення}}", "{{прод_атестат}}", "{{дата_посвідчення}}"
                 })
        {
            Assert.Contains(expected, tags);
        }
    }

    [Theory]
    [InlineData(nameof(TemplateFixtures.RaportGroupDocx))]
    [InlineData(nameof(TemplateFixtures.RaportIndividualDocx))]
    public void EveryScannedTag_IsEitherMappedOrExplicitlyManual(string fixtureName)
    {
        var path = fixtureName == nameof(TemplateFixtures.RaportGroupDocx)
            ? TemplateFixtures.RaportGroupDocx
            : TemplateFixtures.RaportIndividualDocx;

        var unexpectedManual = Scan(path).Tags
            .Select(t => t.Tag)
            .Where(tag => PlaceholderTagMaps.Classify(tag).SourceType == MappingSourceType.Manual)
            .Where(tag => !ManualTagWhitelist.Contains(tag))
            .ToList();

        Assert.True(unexpectedManual.Count == 0,
            "Ці теги мовчки впали в Manual — додайте їх у PlaceholderTagMaps або в білий список тесту: "
            + string.Join(", ", unexpectedManual));
    }
}
```

- [ ] **Step 2: Прогнати — усі мають пройти**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj --filter "FullyQualifiedName~TemplateScanRealFilesTests" -v q --nologo`
Expected: PASS, 6 passed (4 факти + 2 випадки теорії).

Якщо `ScanResult` недоступний з тестового проєкту — додати `[assembly: InternalsVisibleTo("GenDoc.Tests")]`. Спершу перевірити, чи він уже є: `grep -rn "InternalsVisibleTo" GenDoc/`. Наявні тести вже викликають `TemplateService.ScanPlaceholders`, тож доступ, найімовірніше, вже налаштований.

- [ ] **Step 3: Прогнати весь набір і закомітити**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj -v q --nologo`
Expected: PASS, 209 passed, 4 skipped.

```bash
git add GenDoc.Tests/Templates/TemplateScanRealFilesTests.cs
git commit -m "test: cover placeholder scan of the real docx templates"
```

---

### Task 6: Наскрізні тести генерації DOCX на справжніх шаблонах

**Files:**
- Create: `GenDoc.Tests/Generation/DocxRealTemplateTests.cs`

**Interfaces:**
- Consumes: `TemplateFixtures` з Task 1; `DocumentGenerationService.GenerateOne(Template, byte[], IDictionary<string,string>, string)` і `.GenerateGroup(Template, byte[], IReadOnlyList<IDictionary<string,string>>, IDictionary<string,string>, string)`.
- Produces: нічого для наступних задач.

- [ ] **Step 1: Написати тести**

Create `GenDoc.Tests/Generation/DocxRealTemplateTests.cs`:

```csharp
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GenDoc.Models;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Generation;

public class DocxRealTemplateTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-docx-{Guid.NewGuid():N}");

    public DocxRealTemplateTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private string OutputPath(string name) => Path.Combine(_folder, name);

    private static Template TemplateStub(string name) => new()
    {
        Id = 1, Name = name, OriginalFileName = $"{name}.docx",
        Content = Array.Empty<byte>(), UploadedAt = DateTime.Now
    };

    private static string ReadAllText(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var parts = new List<string>();
        if (doc.MainDocumentPart?.Document?.Body is { } body)
            parts.Add(string.Concat(body.Descendants<Text>().Select(t => t.Text)));
        foreach (var header in doc.MainDocumentPart!.HeaderParts)
            parts.Add(string.Concat(header.Header.Descendants<Text>().Select(t => t.Text)));
        foreach (var footer in doc.MainDocumentPart!.FooterParts)
            parts.Add(string.Concat(footer.Footer.Descendants<Text>().Select(t => t.Text)));
        return string.Join("\n", parts);
    }

    private static List<string> ParagraphTexts(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        return doc.MainDocumentPart!.Document.Body!
            .Descendants<Paragraph>()
            .Select(p => string.Concat(p.Descendants<Text>().Select(t => t.Text)).Trim())
            .ToList();
    }

    [Fact]
    public void IndividualRaport_FillsEveryTag_AndLeavesNoRawPlaceholders()
    {
        var values = new Dictionary<string, string>
        {
            ["{{дата_прибуття}}"] = "01 серпня 2026 року",
            ["{{дата_зарахування}}"] = "02 серпня 2026 року",
            ["{{дата_рапорту}}"] = "07.08.2026",
            ["{{дата_посвідчення}}"] = "01 серпня 2026 року",
            ["{{номер_посвідчення}}"] = "№ 123",
            ["{{прод_атестат}}"] = "ПА-77",
            ["{{звання_зв}}"] = "майора",
            ["{{піб_зв}}"] = "ШЕВЧЕНКА Тараса Григоровича",
            ["{{прибув}}"] = "прибув",
            ["{{таким}}"] = "таким",
            ["{{звання_підписанта}}"] = "полковник",
            ["{{піб_підписанта}}"] = "І. ПЕТРЕНКО"
        };

        var path = OutputPath("individual.docx");
        var result = new DocumentGenerationService().GenerateOne(
            TemplateStub("Рапорт"), TemplateFixtures.Bytes(TemplateFixtures.RaportIndividualDocx), values, path);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(result.UnfilledTags);

        var text = ReadAllText(path);
        Assert.DoesNotContain("{{", text);
        Assert.Contains("ШЕВЧЕНКА Тараса Григоровича", text);
        Assert.Contains("ПА-77", text);
    }

    // Порожнє значення має потрапити в UnfilledTags, а тег — не лишитись сирим.
    [Fact]
    public void IndividualRaport_EmptyValue_IsReportedAsUnfilled()
    {
        var values = new Dictionary<string, string> { ["{{прод_атестат}}"] = string.Empty };

        var path = OutputPath("individual-partial.docx");
        var result = new DocumentGenerationService().GenerateOne(
            TemplateStub("Рапорт"), TemplateFixtures.Bytes(TemplateFixtures.RaportIndividualDocx), values, path);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Contains("{{прод_атестат}}", result.UnfilledTags);
    }

    [Fact]
    public void GroupRaport_ExpandsBlockOncePerPerson_WithCorrectSeparators()
    {
        var people = new[]
        {
            ("полковник ", "ШЕВЧЕНКО Т.Г."),
            ("майор ", "ФРАНКО І.Я."),
            ("капітан ", "ЛЕСЯ У.П.")
        };

        var perRecipient = people
            .Select(p => (IDictionary<string, string>)new Dictionary<string, string>
            {
                ["{{звання}}"] = p.Item1,
                ["{{піб}}"] = p.Item2
            })
            .ToList();

        var shared = new Dictionary<string, string>
        {
            ["{{дата_прибуття}}"] = "01 серпня 2026 року",
            ["{{дата_зарахування}}"] = "02 серпня 2026 року",
            ["{{дата_рапорту}}"] = "07.08.2026",
            ["{{звання_підписанта}}"] = "полковник",
            ["{{піб_підписанта}}"] = "І. ПЕТРЕНКО"
        };

        var path = OutputPath("group.docx");
        var result = new DocumentGenerationService().GenerateGroup(
            TemplateStub("Рапорт груповий"),
            TemplateFixtures.Bytes(TemplateFixtures.RaportGroupDocx),
            perRecipient, shared, path);

        Assert.True(result.Success, result.ErrorMessage);

        var paragraphs = ParagraphTexts(path);

        // Маркерні абзаци зникли повністю.
        Assert.DoesNotContain(paragraphs, p => p.StartsWith("{{#", StringComparison.Ordinal));
        Assert.DoesNotContain(paragraphs, p => p.StartsWith("{{/", StringComparison.Ordinal));

        // Кожна людина — свій абзац; останній закінчується крапкою, решта — крапкою з комою.
        var personParagraphs = paragraphs.Where(p => p.Contains("ШЕВЧЕНКО") || p.Contains("ФРАНКО") || p.Contains("ЛЕСЯ")).ToList();
        Assert.Equal(3, personParagraphs.Count);
        Assert.EndsWith(";", personParagraphs[0]);
        Assert.EndsWith(";", personParagraphs[1]);
        Assert.EndsWith(".", personParagraphs[2]);

        Assert.DoesNotContain("{{", ReadAllText(path));
    }

    [Fact]
    public void GroupRaport_EmptyRoster_RemovesBlockEntirely()
    {
        var path = OutputPath("group-empty.docx");
        var result = new DocumentGenerationService().GenerateGroup(
            TemplateStub("Рапорт груповий"),
            TemplateFixtures.Bytes(TemplateFixtures.RaportGroupDocx),
            new List<IDictionary<string, string>>(),
            new Dictionary<string, string>
            {
                ["{{дата_прибуття}}"] = "01 серпня 2026 року",
                ["{{дата_зарахування}}"] = "02 серпня 2026 року",
                ["{{дата_рапорту}}"] = "07.08.2026",
                ["{{звання_підписанта}}"] = "полковник",
                ["{{піб_підписанта}}"] = "І. ПЕТРЕНКО"
            },
            path);

        Assert.True(result.Success, result.ErrorMessage);

        var text = ReadAllText(path);
        Assert.DoesNotContain("{{звання}}", text);
        Assert.DoesNotContain("{{піб}}", text);
        Assert.DoesNotContain("{{#список}}", text);
    }

    // {{номер}} і {{кількість_осіб}} у справжньому шаблоні не трапляються —
    // перевіряємо їх на синтетичному документі з таким самим блоком.
    [Fact]
    public void GroupBlock_ProvidesRowNumberAndPeopleCount()
    {
        var bytes = BuildSyntheticGroupDocx();
        var perRecipient = new List<IDictionary<string, string>>
        {
            new Dictionary<string, string> { ["{{піб}}"] = "ПЕРШИЙ" },
            new Dictionary<string, string> { ["{{піб}}"] = "ДРУГИЙ" }
        };

        var path = OutputPath("synthetic-group.docx");
        var result = new DocumentGenerationService().GenerateGroup(
            TemplateStub("Синтетичний"), bytes, perRecipient, new Dictionary<string, string>(), path);

        Assert.True(result.Success, result.ErrorMessage);

        var paragraphs = ParagraphTexts(path);
        Assert.Contains("1. ПЕРШИЙ;", paragraphs);
        Assert.Contains("2. ДРУГИЙ.", paragraphs);
        Assert.Contains(paragraphs, p => p.Contains("Усього: 2"));
    }

    private static byte[] BuildSyntheticGroupDocx()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var body = doc.AddMainDocumentPart().Document = new Document(new Body());
            var target = doc.MainDocumentPart!.Document.Body!;

            void P(string text) => target.AppendChild(new Paragraph(new Run(new Text(text))));

            P("{{#список}}");
            P("{{номер}}. {{піб}}{{роздільник}}");
            P("{{/список}}");
            P("Усього: {{кількість_осіб}}");

            doc.MainDocumentPart.Document.Save();
        }
        return stream.ToArray();
    }
}
```

- [ ] **Step 2: Прогнати — усі мають пройти**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj --filter "FullyQualifiedName~DocxRealTemplateTests" -v q --nologo`
Expected: PASS, 5 passed.

Якщо `GroupRaport_ExpandsBlockOncePerPerson_WithCorrectSeparators` падає на перевірці розділювачів — подивитись фактичний текст абзацу; можливо, `{{роздільник}}` у справжньому файлі стоїть в окремому рані з пробілом. У такому разі порівнювати через `.TrimEnd()` і `Assert.EndsWith`, не змінюючи змісту перевірки.

- [ ] **Step 3: Прогнати весь набір і закомітити**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj -v q --nologo`
Expected: PASS, 214 passed, 4 skipped.

```bash
git add GenDoc.Tests/Generation/DocxRealTemplateTests.cs
git commit -m "test: cover end-to-end docx generation on the real raport templates"
```

---

### Task 7: Тести скану справжніх XLSX-шаблонів

**Files:**
- Create: `GenDoc.Tests/Templates/ExportTemplateScanRealFilesTests.cs`

**Interfaces:**
- Consumes: `TemplateFixtures` з Task 1; `ExportTemplateService.FindTemplateRow(IXLRange)` (internal static), `PlaceholderTagMaps.Classify(string)`.
- Produces: `static (int Row, List<ExportTemplateColumnMapping> Mappings) ExportTemplateScanRealFilesTests.ScanForGeneration(string path)` — використовується Task 8 повторно; там повторюється повним текстом.

- [ ] **Step 1: Написати тести**

Create `GenDoc.Tests/Templates/ExportTemplateScanRealFilesTests.cs`:

```csharp
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Templates;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Templates;

// Рядок-шаблон — той, що описує ОДНУ людину і клонується на кожного зі списку.
// Помилка тут дає найгучніший симптом: шапка розмножується на всіх слухачів.
public class ExportTemplateScanRealFilesTests
{
    private static readonly Regex TagRegex = new(@"\{\{[^{}]+\}\}", RegexOptions.Compiled);

    private static readonly HashSet<string> ManualTagWhitelist = new(StringComparer.Ordinal)
    {
        "{{дата_аркуша}}", "{{період}}",
        "{{номери_вправ}}", "{{звання_начальника}}", "{{піб_начальника}}",
        "{{опис_підрозділу}}", "{{причина_інструктажу}}",
        "{{калібр}}", "{{кількість_патронів}}", "{{номер_відомості}}"
    };

    private static int? FindRow(string path)
    {
        using var workbook = new XLWorkbook(new MemoryStream(TemplateFixtures.Bytes(path)));
        return ExportTemplateService.FindTemplateRow(workbook.Worksheets.First().RangeUsed()!);
    }

    [Fact]
    public void Dopusk_TemplateRowIsTheDataRow_NotTheHeader()
        => Assert.Equal(7, FindRow(TemplateFixtures.DopuskXlsx));

    [Fact]
    public void Zalik_TemplateRowIsTheDataRow_NotTheUnitNumberRow()
        => Assert.Equal(10, FindRow(TemplateFixtures.ZalikXlsx));

    // У Роздавальній лише ОДИН тег людини ({{піб_ініціали}}) — правило «найбільше
    // тегів людини» мусить упоратись і з таким випадком.
    [Fact]
    public void Rozdavalna_TemplateRowIsTheDataRow_DespiteSingleRecipientTag()
        => Assert.Equal(9, FindRow(TemplateFixtures.RozdavalnaXlsx));

    [Fact]
    public void AnketniDani_HasNoPlaceholders_SoItStaysHeaderDriven()
        => Assert.Null(FindRow(TemplateFixtures.AnketniXlsx));

    [Theory]
    [InlineData("dopusk")]
    [InlineData("zalik")]
    [InlineData("rozdavalna")]
    public void EveryTagInTemplate_IsEitherMappedOrExplicitlyManual(string which)
    {
        var path = which switch
        {
            "dopusk" => TemplateFixtures.DopuskXlsx,
            "zalik" => TemplateFixtures.ZalikXlsx,
            _ => TemplateFixtures.RozdavalnaXlsx
        };

        using var workbook = new XLWorkbook(new MemoryStream(TemplateFixtures.Bytes(path)));
        var tags = workbook.Worksheets.First().RangeUsed()!.CellsUsed()
            .SelectMany(c => TagRegex.Matches(c.GetString()).Select(m => m.Value))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var unexpectedManual = tags
            .Where(tag => PlaceholderTagMaps.Classify(tag).SourceType == MappingSourceType.Manual)
            .Where(tag => !ManualTagWhitelist.Contains(tag))
            .ToList();

        Assert.True(unexpectedManual.Count == 0,
            "Ці теги мовчки впали в Manual — додайте їх у PlaceholderTagMaps або в білий список тесту: "
            + string.Join(", ", unexpectedManual));
    }

    // Друга половина тієї самої пастки: у header-driven шаблоні колонка, чий
    // заголовок не розпізнано, тихо отримує ExportFieldKey.Empty.
    [Fact]
    public void AnketniDani_EveryHeaderColumnIsRecognised()
    {
        using var workbook = new XLWorkbook(new MemoryStream(TemplateFixtures.Bytes(TemplateFixtures.AnketniXlsx)));
        var usedRange = workbook.Worksheets.First().RangeUsed()!;
        var headerRow = usedRange.FirstRow();

        var unrecognised = new List<string>();
        for (var c = 1; c <= usedRange.ColumnCount(); c++)
        {
            var header = headerRow.Cell(c).GetString().Trim();
            if (header.Length == 0) continue;

            if (ExportTemplateService.AutoMapHeaderForTests(header) == ExportFieldKey.Empty)
                unrecognised.Add($"колонка {c}: «{header}»");
        }

        Assert.True(unrecognised.Count == 0,
            "Заголовки не розпізнано, колонка буде порожньою: " + string.Join("; ", unrecognised));
    }
}
```

- [ ] **Step 2: Відкрити `AutoMapExportHeader` для тестів**

`AutoMapExportHeader` — приватний і має `ref bool` параметр, тож викликати його напряму з тесту не вийде. Додати тонку обгортку.

Modify `GenDoc/Services/ExportTemplateService.cs` — одразу після методу `AutoMapExportHeader`:

```csharp
        // Тонка обгортка для тестів: перевіряє розпізнавання ОДНОГО заголовка
        // без стану про вже видану «Примітку».
        internal static ExportFieldKey AutoMapHeaderForTests(string header)
        {
            var noteAssigned = false;
            return AutoMapExportHeader(header, ref noteAssigned);
        }
```

- [ ] **Step 3: Прогнати**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj --filter "FullyQualifiedName~ExportTemplateScanRealFilesTests" -v q --nologo`
Expected: PASS, 8 passed (5 фактів + 3 випадки теорії).

Якщо `AnketniDani_EveryHeaderColumnIsRecognised` падає — це не помилка тесту, а справжня непокрита колонка. Записати перелік нерозпізнаних заголовків у повідомленні коміту й винести окремим рядком у розділ «Знайдене під час роботи» наприкінці плану; правило в `AutoMapExportHeader` додавати НЕ в цій задачі, щоб не змішувати сітку з правками. Тимчасово додати ці заголовки в білий список усередині тесту з коментарем `// TODO(Task 18): ...` та створити Task 18 за зразком Task 16.

- [ ] **Step 4: Прогнати весь набір і закомітити**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj -v q --nologo`
Expected: PASS, 222 passed, 4 skipped.

```bash
git add GenDoc.Tests/Templates/ExportTemplateScanRealFilesTests.cs GenDoc/Services/ExportTemplateService.cs
git commit -m "test: cover template-row detection and header mapping on real xlsx templates"
```

---

### Task 8: Наскрізні тести генерації XLSX на справжніх відомостях

**Files:**
- Create: `GenDoc.Tests/Generation/XlsxRealTemplateTests.cs`

**Interfaces:**
- Consumes: `TemplateFixtures` з Task 1; `ExportTemplateService.FindTemplateRow`, `PlaceholderTagMaps.Classify`, `XlsxGenerationService.Generate(...)`.
- Produces: нічого для наступних задач.

- [ ] **Step 1: Написати тести**

Create `GenDoc.Tests/Generation/XlsxRealTemplateTests.cs`:

```csharp
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Services.Templates;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Generation;

public class XlsxRealTemplateTests
{
    private static readonly Regex TagRegex = new(@"\{\{[^{}]+\}\}", RegexOptions.Compiled);

    // Повторює те, що ExportTemplateService робить під час завантаження шаблону:
    // знаходить рядок-шаблон і будує мапінги (у рядку — ColumnIndex клітинки,
    // поза рядком — ColumnIndex 0).
    private static (int Row, List<ExportTemplateColumnMapping> Mappings) ScanForGeneration(string path)
    {
        using var workbook = new XLWorkbook(new MemoryStream(TemplateFixtures.Bytes(path)));
        var usedRange = workbook.Worksheets.First().RangeUsed()!;
        var row = ExportTemplateService.FindTemplateRow(usedRange)
            ?? throw new InvalidOperationException($"Рядок-шаблон не знайдено у {path}");

        var mappings = new List<ExportTemplateColumnMapping>();
        var outsideTags = new HashSet<string>(StringComparer.Ordinal);

        foreach (var cell in usedRange.CellsUsed())
        {
            foreach (Match match in TagRegex.Matches(cell.GetString()))
            {
                if (cell.Address.RowNumber == row)
                {
                    var (sourceType, fieldName) = PlaceholderTagMaps.Classify(match.Value);
                    mappings.Add(new ExportTemplateColumnMapping
                    {
                        ColumnIndex = cell.Address.ColumnNumber,
                        HeaderText = string.Empty,
                        FieldKey = fieldName ?? string.Empty,
                        PlaceholderTag = match.Value,
                        SourceType = sourceType
                    });
                }
                else
                {
                    outsideTags.Add(match.Value);
                }
            }
        }

        foreach (var tag in outsideTags)
        {
            var (sourceType, fieldName) = PlaceholderTagMaps.Classify(tag);
            mappings.Add(new ExportTemplateColumnMapping
            {
                ColumnIndex = 0,
                HeaderText = string.Empty,
                FieldKey = fieldName ?? string.Empty,
                PlaceholderTag = tag,
                SourceType = sourceType
            });
        }

        return (row, mappings);
    }

    private static XLWorkbook Generate(
        string path, IReadOnlyList<Recipient> roster, Dictionary<string, string> manualValues,
        out XlsxGenerationResult result, string? courseOfficerSignature = null)
    {
        var (row, mappings) = ScanForGeneration(path);
        result = new XlsxGenerationService().Generate(
            TemplateFixtures.Bytes(path), row, usesPlaceholders: true, mappings, roster,
            org: new OrganizationSettings { UnitNumber = "А1234", City = "Львів" },
            manualValues: manualValues,
            repeatSheetPerDate: false,
            courseOfficerSignature: courseOfficerSignature);

        Assert.True(result.Success, result.ErrorMessage);
        return new XLWorkbook(new MemoryStream(result.Content!));
    }

    private static List<string> AllCellText(XLWorkbook workbook)
        => workbook.Worksheets.First().RangeUsed()!.CellsUsed().Select(c => c.GetString()).ToList();

    [Fact]
    public void Zalik_ProducesOneRowPerPerson_AndKeepsHeaderOnce()
    {
        var roster = TemplateFixtures.Roster(5);
        using var produced = Generate(TemplateFixtures.ZalikXlsx, roster,
            new Dictionary<string, string>
            {
                ["{{дата_аркуша}}"] = "07.08.2026",
                ["{{причина_інструктажу}}"] = "плановий",
                ["{{опис_підрозділу}}"] = "1 курс",
                ["{{звання_начальника}}"] = "полковник",
                ["{{піб_начальника}}"] = "І. ПЕТРЕНКО"
            },
            out _, courseOfficerSignature: "майор В. КОВАЛЕНКО");

        var cells = AllCellText(produced);

        foreach (var person in roster)
            Assert.Contains(cells, t => t.Contains(person.LastName, StringComparison.Ordinal));

        // Рядки-люди йдуть підряд від рядка-шаблону.
        var sheet = produced.Worksheets.First();
        for (var i = 0; i < roster.Count; i++)
            Assert.Contains(roster[i].LastName, sheet.Row(10 + i).CellsUsed().Select(c => c.GetString()).ToList()
                .Aggregate(string.Empty, (a, b) => a + b));

        Assert.DoesNotContain(cells, t => t.Contains("{{", StringComparison.Ordinal));
    }

    [Fact]
    public void Rozdavalna_ProducesOneRowPerPerson()
    {
        var roster = TemplateFixtures.Roster(4);
        using var produced = Generate(TemplateFixtures.RozdavalnaXlsx, roster,
            new Dictionary<string, string>
            {
                ["{{дата_аркуша}}"] = "07.08.2026",
                ["{{калібр}}"] = "5,45",
                ["{{кількість_патронів}}"] = "30",
                ["{{номер_відомості}}"] = "12"
            },
            out _);

        var cells = AllCellText(produced);
        foreach (var person in roster)
            Assert.Contains(cells, t => t.Contains(person.LastName, StringComparison.Ordinal));

        Assert.DoesNotContain(cells, t => t.Contains("{{", StringComparison.Ordinal));
    }

    // Блок підписів під таблицею мусить з'їхати рівно на кількість вставлених рядків.
    [Fact]
    public void Zalik_SignatureBlockShiftsDownByInsertedRowCount()
    {
        const int templateRow = 10;
        var roster = TemplateFixtures.Roster(6);

        int SignatureRow(XLWorkbook workbook) => workbook.Worksheets.First().RangeUsed()!.CellsUsed()
            .Where(c => c.GetString().Contains("ПЕТРЕНКО", StringComparison.Ordinal))
            .Select(c => c.Address.RowNumber)
            .DefaultIfEmpty(-1)
            .Max();

        using var single = Generate(TemplateFixtures.ZalikXlsx, TemplateFixtures.Roster(1),
            new Dictionary<string, string>
            {
                ["{{дата_аркуша}}"] = "07.08.2026",
                ["{{причина_інструктажу}}"] = "плановий",
                ["{{опис_підрозділу}}"] = "1 курс",
                ["{{звання_начальника}}"] = "полковник",
                ["{{піб_начальника}}"] = "І. ПЕТРЕНКО"
            }, out _, courseOfficerSignature: "майор В. КОВАЛЕНКО");

        using var many = Generate(TemplateFixtures.ZalikXlsx, roster,
            new Dictionary<string, string>
            {
                ["{{дата_аркуша}}"] = "07.08.2026",
                ["{{причина_інструктажу}}"] = "плановий",
                ["{{опис_підрозділу}}"] = "1 курс",
                ["{{звання_начальника}}"] = "полковник",
                ["{{піб_начальника}}"] = "І. ПЕТРЕНКО"
            }, out _, courseOfficerSignature: "майор В. КОВАЛЕНКО");

        var singleRow = SignatureRow(single);
        var manyRow = SignatureRow(many);
        Assert.True(singleRow > templateRow, "Не знайдено блок підписів у згенерованому файлі");
        Assert.Equal(singleRow + (roster.Count - 1), manyRow);
    }

    // Об'єднання нижче рядка-шаблону мають лишитись об'єднаннями після вставки рядків.
    [Fact]
    public void Zalik_MergedRangesBelowTemplateRow_SurviveRowInsertion()
    {
        using var original = new XLWorkbook(new MemoryStream(TemplateFixtures.Bytes(TemplateFixtures.ZalikXlsx)));
        var originalBelow = original.Worksheets.First().MergedRanges
            .Count(m => m.RangeAddress.FirstAddress.RowNumber > 10);

        using var produced = Generate(TemplateFixtures.ZalikXlsx, TemplateFixtures.Roster(5),
            new Dictionary<string, string>
            {
                ["{{дата_аркуша}}"] = "07.08.2026",
                ["{{причина_інструктажу}}"] = "плановий",
                ["{{опис_підрозділу}}"] = "1 курс",
                ["{{звання_начальника}}"] = "полковник",
                ["{{піб_начальника}}"] = "І. ПЕТРЕНКО"
            }, out _, courseOfficerSignature: "майор В. КОВАЛЕНКО");

        var producedBelow = produced.Worksheets.First().MergedRanges
            .Count(m => m.RangeAddress.FirstAddress.RowNumber > 10 + 5 - 1);

        Assert.Equal(originalBelow, producedBelow);
    }

    // Оцінки мусять бути стабільні: два прогони дають ті самі числа, а загальна —
    // округлене середнє сусідніх оцінок ТОГО САМОГО рядка.
    [Fact]
    public void Dopusk_GradesAreStable_AndOverallIsRoundedAverageOfTheRow()
    {
        var roster = TemplateFixtures.Roster(3);
        var manual = new Dictionary<string, string>
        {
            ["{{номери_вправ}}"] = "1, 2",
            ["{{звання_начальника}}"] = "полковник",
            ["{{піб_начальника}}"] = "І. ПЕТРЕНКО",
            ["{{дата_аркуша}}"] = "07.08.2026"
        };

        using var first = Generate(TemplateFixtures.DopuskXlsx, roster, manual, out _);
        using var second = Generate(TemplateFixtures.DopuskXlsx, roster, manual, out _);

        var (row, mappings) = ScanForGeneration(TemplateFixtures.DopuskXlsx);
        var gradeColumns = mappings
            .Where(m => m.FieldKey == nameof(ExportFieldKey.GradeRandom34))
            .Select(m => m.ColumnIndex).Distinct().OrderBy(c => c).ToList();
        var overallColumn = mappings
            .Single(m => m.FieldKey == nameof(ExportFieldKey.GradeOverall34)).ColumnIndex;

        Assert.Equal(4, gradeColumns.Count);

        var firstSheet = first.Worksheets.First();
        var secondSheet = second.Worksheets.First();

        for (var i = 0; i < roster.Count; i++)
        {
            var targetRow = row + i;

            var grades = gradeColumns.Select(c => firstSheet.Cell(targetRow, c).GetDouble()).ToList();
            Assert.All(grades, g => Assert.InRange(g, 3, 4));

            // Стабільність між прогонами.
            foreach (var c in gradeColumns)
                Assert.Equal(firstSheet.Cell(targetRow, c).GetDouble(), secondSheet.Cell(targetRow, c).GetDouble());

            var expectedOverall = Math.Round(grades.Average(), MidpointRounding.AwayFromZero);
            Assert.Equal(expectedOverall, firstSheet.Cell(targetRow, overallColumn).GetDouble());
        }
    }
}
```

- [ ] **Step 2: Прогнати**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj --filter "FullyQualifiedName~XlsxRealTemplateTests" -v q --nologo`
Expected: PASS, 5 passed.

Якщо `Zalik_SignatureBlockShiftsDownByInsertedRowCount` не знаходить «ПЕТРЕНКО» — значить, `{{піб_начальника}}` у справжньому файлі розбитий на кілька клітинок або лежить в іншому аркуші. Тоді шукати рядок за тегом `{{піб_начальника}}` у ВИХІДНОМУ файлі й порівнювати номер того самого рядка після генерації; сенс перевірки не змінюється.

- [ ] **Step 3: Прогнати весь набір і закомітити**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj -v q --nologo`
Expected: PASS, 227 passed, 4 skipped.

```bash
git add GenDoc.Tests/Generation/XlsxRealTemplateTests.cs
git commit -m "test: cover end-to-end xlsx generation on the real report templates"
```

---

### Task 9: Тести періодів і аркушів на кожну дату

**Files:**
- Create: `GenDoc.Tests/Generation/PeriodSheetsTests.cs`

**Interfaces:**
- Consumes: `TemplateFixtures` з Task 1; `XlsxGenerationService.ParsePeriodDates(string)` (internal static), константи `XlsxGenerationService.PeriodTag` і `DateSheetTag`.
- Produces: **[R]** тест `UnfilledTags_DoNotRepeatTagsThatWereActuallyFilled` зі `Skip`, знімається в Task 15.

- [ ] **Step 1: Написати тести**

Create `GenDoc.Tests/Generation/PeriodSheetsTests.cs`:

```csharp
using ClosedXML.Excel;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Services.Templates;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Generation;

public class PeriodSheetsTests
{
    // ── ParsePeriodDates ────────────────────────────────────────────

    [Fact]
    public void ParsePeriodDates_ExpandsRangeInclusively()
    {
        var dates = XlsxGenerationService.ParsePeriodDates("03.08.2026-05.08.2026");

        Assert.Equal(
            new[] { new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 4), new DateOnly(2026, 8, 5) },
            dates.ToArray());
    }

    [Fact]
    public void ParsePeriodDates_MixesRangesAndSingleDates_SortedAndDeduplicated()
    {
        var dates = XlsxGenerationService.ParsePeriodDates("05.08.2026, 03.08.2026-04.08.2026, 03.08.2026");

        Assert.Equal(
            new[] { new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 4), new DateOnly(2026, 8, 5) },
            dates.ToArray());
    }

    [Fact]
    public void ParsePeriodDates_ReversedRange_ThrowsWithReadableMessage()
    {
        var ex = Assert.Throws<FormatException>(
            () => XlsxGenerationService.ParsePeriodDates("05.08.2026-03.08.2026"));

        Assert.Contains("кінцева дата раніша за початкову", ex.Message);
    }

    [Fact]
    public void ParsePeriodDates_UnknownFormat_ThrowsWithReadableMessage()
    {
        var ex = Assert.Throws<FormatException>(
            () => XlsxGenerationService.ParsePeriodDates("2026-08-03"));

        Assert.Contains("дд.мм.рррр", ex.Message);
    }

    [Fact]
    public void ParsePeriodDates_Empty_ThrowsWithReadableMessage()
    {
        var ex = Assert.Throws<FormatException>(() => XlsxGenerationService.ParsePeriodDates("   "));
        Assert.Contains("порожній", ex.Message);
    }

    // ── repeatSheetPerDate ──────────────────────────────────────────

    private static (int Row, List<ExportTemplateColumnMapping> Mappings) ScanForGeneration(string path)
    {
        var regex = new System.Text.RegularExpressions.Regex(@"\{\{[^{}]+\}\}");
        using var workbook = new XLWorkbook(new MemoryStream(TemplateFixtures.Bytes(path)));
        var usedRange = workbook.Worksheets.First().RangeUsed()!;
        var row = ExportTemplateService.FindTemplateRow(usedRange)!.Value;

        var mappings = new List<ExportTemplateColumnMapping>();
        var outsideTags = new HashSet<string>(StringComparer.Ordinal);

        foreach (var cell in usedRange.CellsUsed())
        {
            foreach (System.Text.RegularExpressions.Match match in regex.Matches(cell.GetString()))
            {
                if (cell.Address.RowNumber == row)
                {
                    var (sourceType, fieldName) = PlaceholderTagMaps.Classify(match.Value);
                    mappings.Add(new ExportTemplateColumnMapping
                    {
                        ColumnIndex = cell.Address.ColumnNumber,
                        HeaderText = string.Empty,
                        FieldKey = fieldName ?? string.Empty,
                        PlaceholderTag = match.Value,
                        SourceType = sourceType
                    });
                }
                else
                {
                    outsideTags.Add(match.Value);
                }
            }
        }

        foreach (var tag in outsideTags)
        {
            var (sourceType, fieldName) = PlaceholderTagMaps.Classify(tag);
            mappings.Add(new ExportTemplateColumnMapping
            {
                ColumnIndex = 0, HeaderText = string.Empty,
                FieldKey = fieldName ?? string.Empty,
                PlaceholderTag = tag, SourceType = sourceType
            });
        }

        return (row, mappings);
    }

    private static Dictionary<string, string> RozdavalnaManualValues(string? period) =>
        new()
        {
            ["{{калібр}}"] = "5,45",
            ["{{кількість_патронів}}"] = "30",
            ["{{номер_відомості}}"] = "12",
            [XlsxGenerationService.PeriodTag] = period ?? string.Empty
        };

    [Fact]
    public void RepeatSheetPerDate_CreatesOneSheetPerDate_NamedByDate()
    {
        var (row, mappings) = ScanForGeneration(TemplateFixtures.RozdavalnaXlsx);

        var result = new XlsxGenerationService().Generate(
            TemplateFixtures.Bytes(TemplateFixtures.RozdavalnaXlsx), row, usesPlaceholders: true,
            mappings, TemplateFixtures.Roster(3),
            org: new OrganizationSettings { UnitNumber = "А1234" },
            manualValues: RozdavalnaManualValues("03.08.2026-05.08.2026"),
            repeatSheetPerDate: true);

        Assert.True(result.Success, result.ErrorMessage);

        using var produced = new XLWorkbook(new MemoryStream(result.Content!));
        var names = produced.Worksheets.Select(s => s.Name).ToList();

        Assert.Contains("03.08.2026", names);
        Assert.Contains("04.08.2026", names);
        Assert.Contains("05.08.2026", names);
    }

    [Fact]
    public void RepeatSheetPerDate_FillsSheetDateTagPerSheet()
    {
        var (row, mappings) = ScanForGeneration(TemplateFixtures.RozdavalnaXlsx);

        var result = new XlsxGenerationService().Generate(
            TemplateFixtures.Bytes(TemplateFixtures.RozdavalnaXlsx), row, usesPlaceholders: true,
            mappings, TemplateFixtures.Roster(2),
            org: new OrganizationSettings { UnitNumber = "А1234" },
            manualValues: RozdavalnaManualValues("03.08.2026-04.08.2026"),
            repeatSheetPerDate: true);

        Assert.True(result.Success, result.ErrorMessage);

        using var produced = new XLWorkbook(new MemoryStream(result.Content!));
        foreach (var sheetName in new[] { "03.08.2026", "04.08.2026" })
        {
            var text = produced.Worksheet(sheetName).RangeUsed()!.CellsUsed()
                .Select(c => c.GetString())
                .Aggregate(string.Empty, (a, b) => a + "\n" + b);

            Assert.Contains(sheetName, text);
            Assert.DoesNotContain(XlsxGenerationService.DateSheetTag, text);
        }
    }

    [Fact]
    public void RepeatSheetPerDate_PutsFullRosterOnEverySheet()
    {
        var roster = TemplateFixtures.Roster(3);
        var (row, mappings) = ScanForGeneration(TemplateFixtures.RozdavalnaXlsx);

        var result = new XlsxGenerationService().Generate(
            TemplateFixtures.Bytes(TemplateFixtures.RozdavalnaXlsx), row, usesPlaceholders: true,
            mappings, roster,
            org: new OrganizationSettings { UnitNumber = "А1234" },
            manualValues: RozdavalnaManualValues("03.08.2026-04.08.2026"),
            repeatSheetPerDate: true);

        Assert.True(result.Success, result.ErrorMessage);

        using var produced = new XLWorkbook(new MemoryStream(result.Content!));
        foreach (var sheetName in new[] { "03.08.2026", "04.08.2026" })
        {
            var cells = produced.Worksheet(sheetName).RangeUsed()!.CellsUsed()
                .Select(c => c.GetString()).ToList();

            foreach (var person in roster)
                Assert.Contains(cells, t => t.Contains(person.LastName, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void RepeatSheetPerDate_WithoutPeriodTag_FailsWithReadableMessage()
    {
        var (row, mappings) = ScanForGeneration(TemplateFixtures.RozdavalnaXlsx);

        var result = new XlsxGenerationService().Generate(
            TemplateFixtures.Bytes(TemplateFixtures.RozdavalnaXlsx), row, usesPlaceholders: true,
            mappings, TemplateFixtures.Roster(2),
            org: new OrganizationSettings { UnitNumber = "А1234" },
            manualValues: RozdavalnaManualValues(period: null),
            repeatSheetPerDate: true);

        Assert.False(result.Success);
        Assert.Contains(XlsxGenerationService.PeriodTag, result.ErrorMessage);
    }

    // Дефект C1: CollectResidualUnfilledTags проходить аркуш ще раз і додає
    // будь-який залишковий тег поверх уже порахованих — у зведенні з'являються
    // теги, які насправді підставились.
    [Fact(Skip = "Червоний до Task 15 — unfilledTags містить теги, які були заповнені")]
    public void UnfilledTags_DoNotRepeatTagsThatWereActuallyFilled()
    {
        var (row, mappings) = ScanForGeneration(TemplateFixtures.RozdavalnaXlsx);

        var manual = RozdavalnaManualValues(period: null);
        manual.Remove(XlsxGenerationService.PeriodTag);
        manual["{{дата_аркуша}}"] = "07.08.2026";

        var result = new XlsxGenerationService().Generate(
            TemplateFixtures.Bytes(TemplateFixtures.RozdavalnaXlsx), row, usesPlaceholders: true,
            mappings, TemplateFixtures.Roster(2),
            org: new OrganizationSettings { UnitNumber = "А1234" },
            manualValues: manual,
            repeatSheetPerDate: false);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.DoesNotContain("{{калібр}}", result.UnfilledTags);
        Assert.DoesNotContain("{{дата_аркуша}}", result.UnfilledTags);
        Assert.DoesNotContain("{{номер_відомості}}", result.UnfilledTags);
    }
}
```

- [ ] **Step 2: Прогнати**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj --filter "FullyQualifiedName~PeriodSheetsTests" -v q --nologo`
Expected: PASS, 9 passed, 1 skipped.

- [ ] **Step 3: Прогнати весь набір і закомітити**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj -v q --nologo`
Expected: PASS, 236 passed, 5 skipped.

```bash
git add GenDoc.Tests/Generation/PeriodSheetsTests.cs
git commit -m "test: cover period parsing and per-date sheet cloning"
```

---

### Task 10: Тести оркестрації `RunPackage`

**Files:**
- Create: `GenDoc.Tests/Generation/RunPackageTests.cs`

**Interfaces:**
- Consumes: `TestDb`, `FakeAuditLog`, `FakeCurrentUser`, `TemplateFixtures` з Task 1; `GenerationService`, `RosterSelection`, `RunResult`.
- Produces: `RunPackageTests.BuildService(TestDb)` і `RunPackageTests.SeedPackage(...)` — повторюються в Task 11 повним текстом.

- [ ] **Step 1: Написати тести**

Create `GenDoc.Tests/Generation/RunPackageTests.cs`:

```csharp
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Documents;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Tests.Generation;

public class RunPackageTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-run-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static GenerationService BuildService(TestDb db) => new(
        db.Factory,
        new DocumentGenerationService(),
        new XlsxGenerationService(),
        new FakeAuditLog(),
        new FakeCurrentUser(),
        new DocumentHashService());

    private static readonly IProgress<string> NoProgress = new Progress<string>(_ => { });

    // Пакет з одним персональним DOCX-шаблоном (справжній «індивідуальний рапорт»)
    // і заданим складом людей.
    private static (int PackageId, List<int> RecipientIds) SeedPackage(TestDb db, List<Recipient> people)
    {
        using var ctx = db.Factory.CreateDbContext();

        ctx.Users.Add(new UserProfile { Id = 1, FullName = "Тест Тестович" });
        ctx.OrganizationSettings.Add(new OrganizationSettings
        {
            UnitNumber = "А1234", City = "Львів",
            CommanderRank = "полковник", CommanderFullName = "І. ПЕТРЕНКО",
            CommanderPosition = "начальник", HrOfficerFullName = "К. КАДРОВ",
            UnitFullName = "Військовий коледж"
        });

        var template = new Template
        {
            Name = "Шаблон_Рапорт_котлове_ІНДИВІДУАЛЬНИЙ",
            OriginalFileName = "rapport.docx",
            Content = TemplateFixtures.Bytes(TemplateFixtures.RaportIndividualDocx),
            UploadedAt = DateTime.Now,
            Kind = TemplateKind.PerRecipient
        };
        ctx.Templates.Add(template);
        ctx.Recipients.AddRange(people);
        ctx.SaveChanges();

        // Мапінги: беремо реальні теги шаблону, класифікуючи їх як у продакшні.
        foreach (var tag in new[]
                 {
                     "{{звання_зв}}", "{{піб_зв}}", "{{прибув}}", "{{таким}}",
                     "{{номер_посвідчення}}", "{{прод_атестат}}", "{{дата_посвідчення}}"
                 })
        {
            var (sourceType, fieldName) = Services.Templates.PlaceholderTagMaps.Classify(tag);
            ctx.TemplateFieldMappings.Add(new TemplateFieldMapping
            {
                TemplateId = template.Id,
                PlaceholderTag = tag,
                SourceType = sourceType,
                FieldName = fieldName,
                IsInsideRepeatingBlock = false
            });
        }

        var package = new GenerationPackage { Name = "Тестовий пакет" };
        package.Templates.Add(new GenerationPackageTemplate { TemplateId = template.Id, SortOrder = 0 });
        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();

        return (package.Id, people.Select(p => p.Id).ToList());
    }

    private RunResult Run(TestDb db, int packageId, bool regenerate = false, RosterSelection? selection = null)
        => BuildService(db).RunPackage(
            packageId, _folder, new Dictionary<string, string>(), regenerate,
            selection ?? RosterSelection.Everyone, NoProgress);

    [Fact]
    public void RunPackage_GeneratesOneFilePerPerson()
    {
        using var db = new TestDb();
        var (packageId, _) = SeedPackage(db, TemplateFixtures.Roster(3));

        var result = Run(db, packageId);

        Assert.Equal(3, result.Generated);
        Assert.Equal(0, result.Errors);
        Assert.Equal(3, Directory.GetFiles(_folder, "*.docx").Length);
    }

    // Ім'я файлу: «ПРІЗВИЩЕ Ім'я Назва шаблону.docx», без технічного префікса «Шаблон_».
    [Fact]
    public void RunPackage_FileNameDropsTemplatePrefixAndUnderscores()
    {
        using var db = new TestDb();
        var (packageId, _) = SeedPackage(db, new List<Recipient>
        {
            TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас")
        });

        Run(db, packageId);

        var fileName = Path.GetFileName(Directory.GetFiles(_folder, "*.docx").Single());
        Assert.Equal("ШЕВЧЕНКО Тарас Рапорт котлове ІНДИВІДУАЛЬНИЙ.docx", fileName);
    }

    [Fact]
    public void RunPackage_TwoPeopleWithIdenticalNames_GetDistinctFileNames()
    {
        using var db = new TestDb();
        var (packageId, _) = SeedPackage(db, new List<Recipient>
        {
            TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас"),
            TemplateFixtures.Person(2, "ШЕВЧЕНКО", "Тарас")
        });

        var result = Run(db, packageId);

        Assert.Equal(2, result.Generated);
        Assert.Equal(2, Directory.GetFiles(_folder, "*.docx").Length);
    }

    // Другий прогін без regenerateExisting нічого не робить, бо файли на місці.
    [Fact]
    public void RunPackage_SecondRun_SkipsWhenFilesStillPresent()
    {
        using var db = new TestDb();
        var (packageId, _) = SeedPackage(db, TemplateFixtures.Roster(2));

        Run(db, packageId);
        var second = Run(db, packageId);

        Assert.Equal(0, second.Generated);
        Assert.Equal(2, second.Skipped);
    }

    // А якщо файли з теки прибрали — має сформувати наново, інакше тека лишиться порожньою.
    [Fact]
    public void RunPackage_SecondRun_RegeneratesWhenFilesWereRemovedFromFolder()
    {
        using var db = new TestDb();
        var (packageId, _) = SeedPackage(db, TemplateFixtures.Roster(2));

        Run(db, packageId);
        foreach (var file in Directory.GetFiles(_folder, "*.docx")) File.Delete(file);

        var second = Run(db, packageId);

        Assert.Equal(2, second.Generated);
        Assert.Equal(0, second.Skipped);
    }

    [Fact]
    public void RunPackage_RegenerateExisting_BumpsVersionAndKeepsOneCurrent()
    {
        using var db = new TestDb();
        var (packageId, recipientIds) = SeedPackage(db, TemplateFixtures.Roster(1));

        Run(db, packageId);
        Run(db, packageId, regenerate: true);

        using var ctx = db.Factory.CreateDbContext();
        var docs = ctx.GeneratedDocuments
            .Where(g => g.RecipientId == recipientIds[0])
            .OrderBy(g => g.Version).ToList();

        Assert.Equal(new[] { 1, 2 }, docs.Select(d => d.Version).ToArray());
        Assert.Single(docs.Where(d => d.IsCurrent));
        Assert.Equal(2, docs.Single(d => d.IsCurrent).Version);
    }

    [Fact]
    public void RunPackage_SelectedRecipientsOnly_IgnoresTheRest()
    {
        using var db = new TestDb();
        var (packageId, recipientIds) = SeedPackage(db, TemplateFixtures.Roster(4));

        var result = Run(db, packageId, selection: new RosterSelection(
            AllRecipients: false,
            RecipientIds: new[] { recipientIds[0], recipientIds[2] },
            FitnessFilter: FitnessFilter.All,
            PermanentStaffOnly: false,
            RankCategories: Array.Empty<RankCategory>(),
            Ranks: Array.Empty<string>()));

        Assert.Equal(2, result.Generated);
    }

    [Fact]
    public void RunPackage_RankFilter_NarrowsRosterByExactRank()
    {
        using var db = new TestDb();
        var (packageId, _) = SeedPackage(db, new List<Recipient>
        {
            TemplateFixtures.Person(1, "ПЕРШИЙ", "Іван", "майор"),
            TemplateFixtures.Person(2, "ДРУГИЙ", "Петро", "капітан"),
            TemplateFixtures.Person(3, "ТРЕТІЙ", "Сидір", "майор")
        });

        var result = Run(db, packageId, selection: new RosterSelection(
            AllRecipients: true,
            RecipientIds: Array.Empty<int>(),
            FitnessFilter: FitnessFilter.All,
            PermanentStaffOnly: false,
            RankCategories: Array.Empty<RankCategory>(),
            Ranks: new[] { "майор" }));

        Assert.Equal(2, result.Generated);
    }

    // PermanentStaffOnly = лише люди без набору (IntakeId == null).
    [Fact]
    public void RunPackage_PermanentStaffOnly_ExcludesIntakeMembers()
    {
        using var db = new TestDb();
        var people = TemplateFixtures.Roster(3);
        var (packageId, recipientIds) = SeedPackage(db, people);

        using (var ctx = db.Factory.CreateDbContext())
        {
            var intake = new Intake { Number = 29, Status = IntakeStatus.Active };
            ctx.Intakes.Add(intake);
            ctx.SaveChanges();

            var member = ctx.Recipients.First(r => r.Id == recipientIds[0]);
            member.IntakeId = intake.Id;
            ctx.SaveChanges();
        }

        var result = Run(db, packageId, selection: new RosterSelection(
            AllRecipients: true,
            RecipientIds: Array.Empty<int>(),
            FitnessFilter: FitnessFilter.All,
            PermanentStaffOnly: true,
            RankCategories: Array.Empty<RankCategory>(),
            Ranks: Array.Empty<string>()));

        Assert.Equal(2, result.Generated);
    }

    [Fact]
    public void RunPackage_RecordsRunRowWithPackageLink()
    {
        using var db = new TestDb();
        var (packageId, _) = SeedPackage(db, TemplateFixtures.Roster(2));

        Run(db, packageId);

        using var ctx = db.Factory.CreateDbContext();
        var run = Assert.Single(ctx.GenerationPackageRuns.ToList());
        Assert.Equal(packageId, run.GenerationPackageId);
    }
}
```

- [ ] **Step 2: Прогнати**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj --filter "FullyQualifiedName~RunPackageTests" -v q --nologo`
Expected: PASS, 10 passed.

Якщо `RunPackage_FileNameDropsTemplatePrefixAndUnderscores` падає — звірити очікуване ім'я з тим, що дає `TemplateNaming.Clean("Шаблон_Рапорт_котлове_ІНДИВІДУАЛЬНИЙ")`, і виправити рядок очікування в тесті, а не продакшн-код: `TemplateNaming` уже покритий власними тестами.

- [ ] **Step 3: Прогнати весь набір і закомітити**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj -v q --nologo`
Expected: PASS, 246 passed, 5 skipped.

```bash
git add GenDoc.Tests/Generation/RunPackageTests.cs
git commit -m "test: cover RunPackage roster filters, skip logic, versioning and naming"
```

---

### Task 11: Червоні тести зведення запуску

Фіксує дефекти B1, B2 і B3. Обидва тести зі `Skip`, який знімається в Task 14.

**Files:**
- Create: `GenDoc.Tests/Generation/RunPackageReportingTests.cs`

**Interfaces:**
- Consumes: `TestDb`, `FakeAuditLog`, `FakeCurrentUser`, `TemplateFixtures` з Task 1; `DocumentArchiveService.GetRunItemsAsync(int)`.
- Produces: нічого; після Task 14 обидва тести стають зеленими.

- [ ] **Step 1: Написати тести**

Create `GenDoc.Tests/Generation/RunPackageReportingTests.cs`:

```csharp
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Documents;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Tests.Generation;

// Екран «Запуски» бере лічильники з рядка GenerationPackageRun, а перелік
// помилок відновлює з поля Summary. Обидва джерела зараз брешуть.
public class RunPackageReportingTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-report-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static readonly IProgress<string> NoProgress = new Progress<string>(_ => { });

    private static GenerationService BuildGeneration(TestDb db) => new(
        db.Factory,
        new DocumentGenerationService(),
        new XlsxGenerationService(),
        new FakeAuditLog(),
        new FakeCurrentUser(),
        new DocumentHashService());

    private static DocumentArchiveService BuildArchive(TestDb db) => new(
        db.Factory,
        new FakeAuditLog(),
        new FakeCurrentUser(),
        new FakeTempFiles(),
        new NoOpWatermarkService(),
        new DocumentGenerationService(),
        new DocumentHashService());

    // Пакет з двома XLSX-відомостями: одна валідна, друга зі свідомо
    // пошкодженим вмістом — тобто XLSX-фаза дасть рівно одну помилку.
    private static int SeedPackageWithFailingXlsx(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();

        ctx.Users.Add(new UserProfile { Id = 1, FullName = "Тест Тестович" });
        ctx.OrganizationSettings.Add(new OrganizationSettings
        {
            UnitNumber = "А1234", City = "Львів",
            CommanderRank = "полковник", CommanderFullName = "І. ПЕТРЕНКО",
            CommanderPosition = "начальник", HrOfficerFullName = "К. КАДРОВ",
            UnitFullName = "Військовий коледж"
        });
        ctx.Recipients.AddRange(TemplateFixtures.Roster(2));

        var broken = new ExportTemplate
        {
            Name = "Зламана відомість",
            OriginalFileName = "broken.xlsx",
            Content = new byte[] { 0x00, 0x01, 0x02, 0x03 }, // не є zip/xlsx
            UsesPlaceholders = true,
            TemplateRowIndex = 2,
            UploadedAt = DateTime.Now
        };
        broken.ColumnMappings.Add(new ExportTemplateColumnMapping
        {
            ColumnIndex = 1, HeaderText = string.Empty,
            FieldKey = nameof(ExportFieldKey.FullNameFormatted),
            PlaceholderTag = "{{піб}}", SourceType = MappingSourceType.Recipient
        });
        ctx.ExportTemplates.Add(broken);
        ctx.SaveChanges();

        var package = new GenerationPackage { Name = "Пакет зі зламаною відомістю" };
        package.ExportTemplates.Add(new GenerationPackageExportTemplate
        {
            ExportTemplateId = broken.Id, SortOrder = 0, FitnessFilter = FitnessFilter.All
        });
        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();

        return package.Id;
    }

    // Дефект B1: run.ErrorCount пишеться лише з docx-фази, тож помилка
    // XLSX-фази на екрані «Запуски» не видно взагалі.
    [Fact(Skip = "Червоний до Task 14 — лічильники запуску враховують лише docx-фазу")]
    public void RunPackage_PersistsCountersSummedAcrossAllThreePhases()
    {
        using var db = new TestDb();
        var packageId = SeedPackageWithFailingXlsx(db);

        var result = BuildGeneration(db).RunPackage(
            packageId, _folder, new Dictionary<string, string>(),
            regenerateExisting: false, RosterSelection.Everyone, NoProgress);

        Assert.Equal(1, result.GroupErrors);

        using var ctx = db.Factory.CreateDbContext();
        var run = Assert.Single(ctx.GenerationPackageRuns.ToList());

        Assert.Equal(
            result.Errors + result.GroupErrors + result.DocxGroupErrors,
            run.ErrorCount);
        Assert.Equal(
            result.Generated + result.GroupGenerated + result.DocxGroupGenerated,
            run.GeneratedCount);
        Assert.Equal(
            result.Skipped + result.GroupSkipped + result.DocxGroupSkipped,
            run.SkippedCount);
    }

    // Дефект B2: рядок «ГРУПА: Назва: текст» розбирається як ПІБ = «ГРУПА»,
    // шаблон = «—», а текст обрізається на першій двокрапці.
    [Fact(Skip = "Червоний до Task 14 — помилки запуску відновлюються розбором рядка")]
    public async Task GetRunItemsAsync_ReportsGroupPhaseErrorWithTemplateNameIntact()
    {
        using var db = new TestDb();
        var packageId = SeedPackageWithFailingXlsx(db);

        BuildGeneration(db).RunPackage(
            packageId, _folder, new Dictionary<string, string>(),
            regenerateExisting: false, RosterSelection.Everyone, NoProgress);

        int runId;
        using (var ctx = db.Factory.CreateDbContext())
            runId = ctx.GenerationPackageRuns.Select(r => r.Id).Single();

        var items = await BuildArchive(db).GetRunItemsAsync(runId);

        var errorItem = Assert.Single(items.Where(i => i.IsError));
        Assert.Equal("Зламана відомість", errorItem.TemplateName);
        Assert.DoesNotContain("ГРУПА", errorItem.Person, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Прогнати — обидва пропущені, набір зелений**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj --filter "FullyQualifiedName~RunPackageReportingTests" -v q --nologo`
Expected: PASS, 0 passed, 2 skipped.

- [ ] **Step 3: Перевірити, що тести дійсно червоні**

Тимчасово прибрати `Skip = "…"` з обох атрибутів, прогнати, переконатись, що обидва падають, і повернути `Skip` назад.

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj --filter "FullyQualifiedName~RunPackageReportingTests" -v q --nologo`
Expected: FAIL, 2 failed — інакше дефекту немає і Task 14 треба переглянути.

- [ ] **Step 4: Прогнати весь набір і закомітити**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj -v q --nologo`
Expected: PASS, 246 passed, 7 skipped.

```bash
git add GenDoc.Tests/Generation/RunPackageReportingTests.cs
git commit -m "test: pin down run summary counters and per-item errors (skipped until fixed)"
```

---

### Task 12: Виправлення — ланцюг версій групових документів

Знімає `Skip` з трьох тестів Task 4.

**Files:**
- Modify: `GenDoc/Services/Documents/DocumentArchiveService.cs`
- Modify: `GenDoc/Services/Documents/IDocumentArchiveService.cs:41`
- Modify: `GenDoc/Services/Documents/ArchiveModels.cs`
- Test: `GenDoc.Tests/Archive/GroupDocumentArchiveTests.cs`

**Interfaces:**
- Consumes: тести Task 4.
- Produces: `GroupDocumentRowDto` отримує поле `int? DocxTemplateId` після `ExportTemplateId`; `GetGroupVersionsAsync` змінює сигнатуру на `Task<List<GroupVersionDto>> GetGroupVersionsAsync(int? exportTemplateId, int? docxTemplateId)`.

- [ ] **Step 1: Зняти `Skip` з трьох тестів і переконатись, що вони червоні**

У `GenDoc.Tests/Archive/GroupDocumentArchiveTests.cs` замінити `[Fact(Skip = "Червоний до Task 12 — …")]` на `[Fact]` у:
`DeleteGroupAsync_Docx_DoesNotTouchOtherDocxTemplate`,
`RestoreGroupAsync_Docx_DoesNotStealCurrentFlagFromOtherTemplate`,
`QueryGroupAsync_ReturnsDocxGroupsWithTheirTemplateName`.

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj --filter "FullyQualifiedName~GroupDocumentArchiveTests" -v q --nologo`
Expected: FAIL, 3 failed, 3 passed, 1 skipped.

- [ ] **Step 2: Додати спільний предикат серії**

У `GenDoc/Services/Documents/DocumentArchiveService.cs` додати приватний статичний метод одразу перед `QueryGroupAsync`:

```csharp
        // Серія версій групового документа визначається ПАРОЮ ключів, бо один
        // з них завжди null: XLSX-відомість тримається на ExportTemplateId,
        // груповий DOCX — на TemplateId. Порівняння лише за ExportTemplateId
        // означало `IS NULL` і зачіпало всі групові DOCX усіх шаблонів одразу.
        private static IQueryable<GeneratedGroupDocument> SameGroupSeries(
            IQueryable<GeneratedGroupDocument> source, GeneratedGroupDocument doc)
            => source.Where(g =>
                g.ExportTemplateId == doc.ExportTemplateId
                && g.TemplateId == doc.TemplateId
                && g.IntakeId == doc.IntakeId);
```

- [ ] **Step 3: Застосувати предикат у трьох місцях**

`DeleteGroupAsync` — замінити тіло гілки `if (doc.IsCurrent)`:

```csharp
                if (doc.IsCurrent)
                {
                    doc.IsCurrent = false;
                    var previous = await SameGroupSeries(db.GeneratedGroupDocuments, doc)
                        .Where(g => g.Id != doc.Id && g.DeletedAt == null)
                        .OrderByDescending(g => g.Version)
                        .FirstOrDefaultAsync();
                    if (previous is not null) previous.IsCurrent = true;
                }
```

`MakeGroupCurrentAsync` — замінити цикл гасіння:

```csharp
            foreach (var current in SameGroupSeries(db.GeneratedGroupDocuments, target)
                .Where(g => g.IsCurrent).ToList())
            {
                current.IsCurrent = false;
            }
            target.IsCurrent = true;
```

`RestoreGroupAsync` — замінити обидва запити:

```csharp
            var maxAliveVersion = await SameGroupSeries(db.GeneratedGroupDocuments, doc)
                .Where(g => g.Id != doc.Id)
                .MaxAsync(g => (int?)g.Version) ?? 0;

            if (doc.Version >= maxAliveVersion)
            {
                foreach (var current in SameGroupSeries(db.GeneratedGroupDocuments, doc)
                    .Where(g => g.IsCurrent).ToList())
                {
                    current.IsCurrent = false;
                }
                doc.IsCurrent = true;
            }
```

- [ ] **Step 4: Показати групові DOCX у списку**

`GenDoc/Services/Documents/ArchiveModels.cs` — у `GroupDocumentRowDto` додати поле після `ExportTemplateId`:

```csharp
    public record GroupDocumentRowDto(
        int Id,
        int ExportTemplateId,
        int? DocxTemplateId,
        string TemplateName,
        bool TemplateAlive,
        int Version,
        int RecipientCount,
        DateTime GeneratedAt,
        string Author,
        bool HasContent,
        string FileName,
        long SizeBytes);
```

`DocumentArchiveService.QueryGroupAsync` — замінити проєкцію (TODO у коді знімається):

```csharp
                .Select(g => new GroupDocumentRowDto(
                    g.Id,
                    g.ExportTemplateId ?? 0,
                    g.TemplateId,
                    g.ExportTemplate != null
                        ? g.ExportTemplate.Name
                        : g.Template != null ? g.Template.Name : "—",
                    g.ExportTemplate != null
                        ? g.ExportTemplate.DeletedAt == null
                        : g.Template != null && g.Template.DeletedAt == null,
                    g.Version,
                    g.RecipientCount,
                    g.GeneratedAt,
                    g.GeneratedByUser != null ? g.GeneratedByUser.FullName : "—",
                    g.HasContent,
                    g.FileName,
                    g.SizeBytes))
```

`GetDeletedGroupDocumentsAsync` — так само дати назву DOCX-шаблону:

```csharp
                    g.ExportTemplate != null
                        ? g.ExportTemplate.Name
                        : g.Template != null ? g.Template.Name : "—",
```

`GetGroupTemplateOptionsAsync` — додати DOCX-шаблони до списку фільтра:

```csharp
        public async Task<List<(int Id, string Name)>> GetGroupTemplateOptionsAsync()
        {
            using var db = _dbFactory.CreateDbContext();

            var exportIds = await db.GeneratedGroupDocuments
                .Where(g => g.ExportTemplateId != null)
                .Select(g => g.ExportTemplateId!.Value).Distinct().ToListAsync();

            var options = (await db.ExportTemplates.IgnoreQueryFilters()
                    .Where(t => exportIds.Contains(t.Id))
                    .Select(t => new { t.Id, t.Name })
                    .ToListAsync())
                .Select(t => (t.Id, t.Name))
                .ToList();

            var docxIds = await db.GeneratedGroupDocuments
                .Where(g => g.TemplateId != null)
                .Select(g => g.TemplateId!.Value).Distinct().ToListAsync();

            options.AddRange((await db.Templates.IgnoreQueryFilters()
                    .Where(t => docxIds.Contains(t.Id))
                    .Select(t => new { t.Id, t.Name })
                    .ToListAsync())
                .Select(t => (t.Id, t.Name)));

            return options.OrderBy(o => o.Name, StringComparer.CurrentCulture).ToList();
        }
```

- [ ] **Step 5: Оновити `GetGroupVersionsAsync` під обидва ґатунки**

`IDocumentArchiveService.cs` — замінити рядок оголошення:

```csharp
        Task<List<GroupVersionDto>> GetGroupVersionsAsync(int? exportTemplateId, int? docxTemplateId);
```

`DocumentArchiveService.cs`:

```csharp
        public async Task<List<GroupVersionDto>> GetGroupVersionsAsync(int? exportTemplateId, int? docxTemplateId)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.GeneratedGroupDocuments
                .Where(g => g.ExportTemplateId == exportTemplateId && g.TemplateId == docxTemplateId)
                .OrderByDescending(g => g.Version)
                .Select(g => new GroupVersionDto(
                    g.Id, g.Version, g.GeneratedAt,
                    g.GeneratedByUser != null ? g.GeneratedByUser.FullName : "—",
                    g.SizeBytes, g.IsCurrent, g.HasContent, g.FileName, g.RecipientCount))
                .ToListAsync();
        }
```

- [ ] **Step 6: Полагодити виклики у в'ю-моделях**

Run: `dotnet build GenDoc/GenDoc.csproj -v q --nologo`

Компілятор укаже на всі місця, де `GroupDocumentRowDto` конструюється позиційно або де викликається `GetGroupVersionsAsync`. У кожному з них:
- для `GetGroupVersionsAsync` передавати `row.Dto.ExportTemplateId == 0 ? null : row.Dto.ExportTemplateId` як перший аргумент і `row.Dto.DocxTemplateId` як другий;
- нові позиційні поля `GroupDocumentRowDto` заповнювати з наявних даних рядка.

Expected: build succeeded, 0 errors.

- [ ] **Step 7: Прогнати тести групових документів**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj --filter "FullyQualifiedName~GroupDocumentArchiveTests" -v q --nologo`
Expected: PASS, 6 passed, 1 skipped.

- [ ] **Step 8: Прогнати весь набір і закомітити**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj -v q --nologo`
Expected: PASS, 249 passed, 4 skipped.

```bash
git add GenDoc/Services/Documents GenDoc/ViewModels/Archive GenDoc.Tests/Archive/GroupDocumentArchiveTests.cs
git commit -m "fix: key group document version chain on both template kinds

Порівняння лише за ExportTemplateId перетворювалось на IS NULL і зачіпало
всі групові DOCX усіх шаблонів: видалення однієї відомості робило актуальною
версію чужої. Серія тепер визначається парою (ExportTemplateId, TemplateId,
IntakeId), а список групових документів показує і DOCX-групи."
```

---

### Task 13: Виправлення — записи без збереженого вмісту

Знімає `Skip` з тесту `OpenGroupAsync_DocumentWithoutStoredContent_ReturnsFailureInsteadOfThrowing` (Task 4).

**Files:**
- Modify: `GenDoc/Services/Documents/IDocumentArchiveService.cs`
- Modify: `GenDoc/Services/Documents/DocumentArchiveService.cs`
- Modify: `GenDoc/ViewModels/Archive/ArchiveViewModel.cs`
- Modify: `GenDoc/ViewModels/Archive/VersionHistoryViewModel.cs`
- Modify: `GenDoc/ViewModels/Archive/GroupVersionHistoryViewModel.cs`
- Test: `GenDoc.Tests/Archive/GroupDocumentArchiveTests.cs`

**Interfaces:**
- Produces: `OpenAsync`, `OpenGroupAsync` і `OpenAttachmentAsync` повертають `Task<ArchiveOpResult>` замість `Task`; `SaveAsAsync` і `SaveGroupAsAsync` уже повертають `ArchiveOpResult` і починають віддавати `Success = false` замість винятку.

- [ ] **Step 1: Написати додатковий червоний тест для персонального документа**

У `GenDoc.Tests/Archive/DocumentVersionChainTests.cs` додати:

```csharp
    // Легасі-запис без збереженого вмісту не має падати з внутрішнім
    // «Sequence contains no elements» — користувач мусить побачити пояснення.
    [Fact]
    public async Task OpenAsync_DocumentWithoutStoredContent_ReturnsFailureInsteadOfThrowing()
    {
        using var db = new TestDb();
        int docId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var person = new Recipient
            {
                LastName = "БЕЗВМІСТУ", FirstName = "Іван",
                Rank = "капітан", Position = "слухач", ServiceNumber = "7"
            };
            var template = new Template
            {
                Name = "Рапорт", OriginalFileName = "r.docx",
                Content = Array.Empty<byte>(), UploadedAt = DateTime.Now
            };
            ctx.Recipients.Add(person);
            ctx.Templates.Add(template);
            ctx.SaveChanges();

            var doc = new GeneratedDocument
            {
                RecipientId = person.Id, TemplateId = template.Id,
                GeneratedAt = DateTime.Now, GeneratedByUserId = 1,
                FileName = "no-content.docx", SizeBytes = 0,
                Version = 1, IsCurrent = true,
                SourceType = DocumentSourceType.Generated, HasContent = false
            };
            ctx.GeneratedDocuments.Add(doc);
            ctx.SaveChanges();
            docId = doc.Id;
        }

        var result = await BuildService(db).OpenAsync(docId);

        Assert.False(result.Success);
        Assert.Contains("не збережено в архіві", result.ErrorMessage);
    }
```

- [ ] **Step 2: Прогнати — має не скомпілюватись або впасти**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj --filter "FullyQualifiedName~DocumentVersionChainTests" -v q --nologo`
Expected: помилка компіляції `Cannot implicitly convert type 'System.Threading.Tasks.Task'` — саме тому сигнатура й змінюється.

- [ ] **Step 3: Змінити інтерфейс**

`GenDoc/Services/Documents/IDocumentArchiveService.cs` — три рядки:

```csharp
        Task<ArchiveOpResult> OpenAsync(int documentId);
```
```csharp
        Task<ArchiveOpResult> OpenAttachmentAsync(int attachmentId);
```
```csharp
        Task<ArchiveOpResult> OpenGroupAsync(int groupDocumentId);
```

- [ ] **Step 4: Реалізувати**

`GenDoc/Services/Documents/DocumentArchiveService.cs` — додати спільну константу поруч з `DefaultMaxDocumentSizeKb`:

```csharp
        private const string NoContentMessage =
            "Файл цього документа не збережено в архіві — доступні лише його дані. " +
            "Сформуйте документ наново або завантажте файл вручну.";
```

Замінити `OpenAsync`:

```csharp
        public async Task<ArchiveOpResult> OpenAsync(int documentId)
        {
            using var db = _dbFactory.CreateDbContext();
            var doc = await db.GeneratedDocuments.FirstAsync(g => g.Id == documentId);
            var content = await db.GeneratedDocumentContents
                .FirstOrDefaultAsync(c => c.GeneratedDocumentId == documentId);
            if (content is null) return new ArchiveOpResult(false, NoContentMessage);

            await _tempFileService.OpenAsync(doc.FileName, content.Content);

            _auditLogService.Log(db, "Відкрито документ", "GeneratedDocument", documentId, null, doc.FileName);
            await db.SaveChangesAsync();
            return new ArchiveOpResult(true, null);
        }
```

`SaveAsAsync` — та сама заміна `FirstAsync` на `FirstOrDefaultAsync` з перевіркою:

```csharp
            var content = await db.GeneratedDocumentContents
                .FirstOrDefaultAsync(c => c.GeneratedDocumentId == documentId);
            if (content is null) return new ArchiveOpResult(false, NoContentMessage);
```

`OpenGroupAsync`:

```csharp
        public async Task<ArchiveOpResult> OpenGroupAsync(int groupDocumentId)
        {
            using var db = _dbFactory.CreateDbContext();
            var doc = await db.GeneratedGroupDocuments.FirstAsync(g => g.Id == groupDocumentId);
            var content = await db.GeneratedGroupDocumentContents
                .FirstOrDefaultAsync(c => c.GeneratedGroupDocumentId == groupDocumentId);
            if (content is null) return new ArchiveOpResult(false, NoContentMessage);

            await _tempFileService.OpenAsync(doc.FileName, content.Content);

            _auditLogService.Log(db, "Відкрито документ", "GeneratedGroupDocument", groupDocumentId, null, doc.FileName);
            await db.SaveChangesAsync();
            return new ArchiveOpResult(true, null);
        }
```

`SaveGroupAsAsync` — так само `FirstOrDefaultAsync` + перевірка на `null` з поверненням `NoContentMessage`.

`OpenAttachmentAsync` — вкладення тримає байти в самому рядку, тож окремої перевірки на вміст не потрібно; змінюється лише тип повернення:

```csharp
        public async Task<ArchiveOpResult> OpenAttachmentAsync(int attachmentId)
        {
            using var db = _dbFactory.CreateDbContext();
            var attachment = await db.DocumentAttachments.FirstAsync(a => a.Id == attachmentId);
            await _tempFileService.OpenAsync(attachment.FileName, attachment.Content);

            _auditLogService.Log(db, "Відкрито документ", "GeneratedDocument",
                attachment.GeneratedDocumentId, null, attachment.FileName);
            await db.SaveChangesAsync();
            return new ArchiveOpResult(true, null);
        }
```

- [ ] **Step 5: Оновити в'ю-моделі**

Run: `dotnet build GenDoc/GenDoc.csproj -v q --nologo`

Компілятор укаже на всі виклики. У кожному місці, де раніше було `await _archiveService.OpenAsync(row.Id);`, стає:

```csharp
                var result = await _archiveService.OpenAsync(row.Id);
                if (!result.Success)
                {
                    StatusMessage = result.ErrorMessage;
                    return;
                }
```

Якщо в конкретній в'ю-моделі немає властивості `StatusMessage`, показати повідомлення тим самим способом, яким там уже показуються помилки (пошукати в файлі наявний виклик `MessageBox` або присвоєння повідомлення). Не вводити новий механізм показу помилок.

Місця для правки: `ArchiveViewModel.cs:369`, `:389`, `:419`, `:642`, `:660`, `:774`; `VersionHistoryViewModel.cs:104`, `:122`; `GroupVersionHistoryViewModel.cs:63`.

Expected: build succeeded, 0 errors.

- [ ] **Step 6: Зняти `Skip` і полагодити тіло групового тесту**

У `GenDoc.Tests/Archive/GroupDocumentArchiveTests.cs` замінити атрибут на `[Fact]` і повернути тіло до варіанта з `ArchiveOpResult` (той, що записаний у Task 4, Step 1).

- [ ] **Step 7: Прогнати весь набір і закомітити**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj -v q --nologo`
Expected: PASS, 251 passed, 3 skipped.

```bash
git add GenDoc/Services/Documents GenDoc/ViewModels/Archive GenDoc.Tests/Archive
git commit -m "fix: report missing archived content instead of throwing

Записи з HasContent = false падали з InvalidOperationException
«Sequence contains no elements». Open/SaveAs тепер повертають
ArchiveOpResult з поясненням українською."
```

---

### Task 14: Виправлення — лічильники запуску і структуровані помилки

Знімає `Skip` з обох тестів Task 11.

**Files:**
- Create: `GenDoc/Services/Generation/RunIssue.cs`
- Modify: `GenDoc/Services/Generation/GenerationService.cs`
- Modify: `GenDoc/Services/Documents/DocumentArchiveService.cs`
- Test: `GenDoc.Tests/Generation/RunPackageReportingTests.cs`

**Interfaces:**
- Produces: `public sealed record RunIssue(string Phase, string Person, string TemplateName, string Message)` у просторі імен `GenDoc.Services.Generation`, зі статичними членами `Serialize(IReadOnlyList<RunIssue>)` і `TryDeserialize(string?, out List<RunIssue>)`.

- [ ] **Step 1: Зняти `Skip` і переконатись у падінні**

Замінити обидва `[Fact(Skip = "…")]` на `[Fact]` у `RunPackageReportingTests.cs`.

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj --filter "FullyQualifiedName~RunPackageReportingTests" -v q --nologo`
Expected: FAIL, 2 failed.

- [ ] **Step 2: Створити `RunIssue`**

Create `GenDoc/Services/Generation/RunIssue.cs`:

```csharp
using System.Text.Json;

namespace GenDoc.Services.Generation
{
    // Один рядок помилки запуску. Зберігається в наявній колонці
    // GenerationPackageRun.Summary у вигляді JSON-масиву — без міграції схеми.
    // Старий формат (звичайний текст з рядками «ПІБ / Шаблон: помилка») читається
    // запасним парсером у DocumentArchiveService, бо в робочих базах він уже є.
    public sealed record RunIssue(string Phase, string Person, string TemplateName, string Message)
    {
        public const string PhaseDocx = "docx";
        public const string PhaseXlsx = "xlsx";
        public const string PhaseDocxGroup = "docx-group";

        private static readonly JsonSerializerOptions Options = new()
        {
            // Кирилиця має лишатись читабельною і в самій базі.
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static string? Serialize(IReadOnlyList<RunIssue> issues)
            => issues.Count == 0 ? null : JsonSerializer.Serialize(issues, Options);

        public static bool TryDeserialize(string? raw, out List<RunIssue> issues)
        {
            issues = new List<RunIssue>();
            if (string.IsNullOrWhiteSpace(raw)) return false;
            if (raw.TrimStart()[0] != '[') return false;

            try
            {
                issues = JsonSerializer.Deserialize<List<RunIssue>>(raw, Options) ?? new List<RunIssue>();
                return true;
            }
            catch (JsonException)
            {
                issues = new List<RunIssue>();
                return false;
            }
        }
    }
}
```

- [ ] **Step 3: Збирати `RunIssue` у трьох фазах**

У `GenDoc/Services/Generation/GenerationService.cs`:

`DocxPhaseResult` — замінити тип поля помилок:

```csharp
        private sealed record DocxPhaseResult(int Generated, int Skipped, int Errors, List<RunIssue> Issues);
```

У `RunDocxPhase` замінити `var errorMessages = new List<string>();` на `var issues = new List<RunIssue>();`, обидва додавання — на:

```csharp
                            issues.Add(new RunIssue(RunIssue.PhaseDocx,
                                $"{recipient.LastName} {recipient.FirstName}", template.Name,
                                result.ErrorMessage ?? "невідома помилка"));
```
```csharp
                        issues.Add(new RunIssue(RunIssue.PhaseDocx,
                            $"{recipient.LastName} {recipient.FirstName}", template.Name, ex.Message));
```

і повернення — на `new DocxPhaseResult(generated, skipped, errors, issues)`.

`XlsxPhaseResult` і `DocxGroupPhaseResult` — так само замінити `List<string> SummaryLines` на `List<RunIssue> Issues`. У `RunXlsxPhase` кожен `summaryLines.Add($"ГРУПА: …")` стає:

```csharp
                    issues.Add(new RunIssue(RunIssue.PhaseXlsx, string.Empty, template.Name, "<текст як був>"));
```

де `<текст як був>` — той самий рядок без префікса «ГРУПА: {template.Name}: ». Конкретно чотири місця:
- «пропущено — немає людей за фільтром придатності»;
- `result.ErrorMessage` при `!result.Success`;
- `ex.Message` у `catch`;
- `$"не заповнено теги — {string.Join(", ", result.UnfilledTags)}"`.

У `RunDocxGroupPhase` — те саме з `RunIssue.PhaseDocxGroup` і трьома місцями («пропущено — немає людей за обраним складом», `result.ErrorMessage`, `ex.Message`, «не заповнено теги»).

- [ ] **Step 4: Підсумувати лічильники і записати JSON**

У `RunPackage` замінити блок після трьох фаз:

```csharp
            run.GeneratedCount = docx.Generated + xlsx.Generated + docxGroup.Generated;
            run.SkippedCount = docx.Skipped + xlsx.Skipped + docxGroup.Skipped;
            run.ErrorCount = docx.Errors + xlsx.Errors + docxGroup.Errors;

            var issues = new List<RunIssue>(docx.Issues);
            issues.AddRange(xlsx.Issues);
            issues.AddRange(docxGroup.Issues);
            run.Summary = RunIssue.Serialize(issues);
```

- [ ] **Step 5: Читати JSON у архіві**

У `GenDoc/Services/Documents/DocumentArchiveService.cs` замінити хвіст `GetRunItemsAsync` (блок `if (!string.IsNullOrWhiteSpace(summary))`):

```csharp
            var summary = await db.GenerationPackageRuns
                .Where(r => r.Id == runId).Select(r => r.Summary).FirstOrDefaultAsync();

            if (RunIssue.TryDeserialize(summary, out var issues))
            {
                foreach (var issue in issues)
                {
                    items.Add(new RunItemDto(
                        issue.Person.Length > 0 ? issue.Person : "—",
                        issue.TemplateName,
                        $"помилка: {issue.Message}",
                        true, 0, null, false, string.Empty));
                }
            }
            else if (!string.IsNullOrWhiteSpace(summary))
            {
                // Запуски, зроблені до переходу на JSON: старий текстовий формат.
                foreach (var line in summary.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split(':', 2);
                    var head = parts[0].Split('/', 2);
                    items.Add(new RunItemDto(
                        head[0].Trim(),
                        head.Length > 1 ? head[1].Trim() : "—",
                        parts.Length > 1 ? $"помилка: {parts[1].Trim()}" : "помилка",
                        true, 0, null, false, string.Empty));
                }
            }
```

Додати `using GenDoc.Services.Generation;` — він уже є у файлі.

- [ ] **Step 6: Прогнати**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj --filter "FullyQualifiedName~RunPackageReportingTests" -v q --nologo`
Expected: PASS, 2 passed.

- [ ] **Step 7: Прогнати весь набір і закомітити**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj -v q --nologo`
Expected: PASS, 253 passed, 1 skipped.

```bash
git add GenDoc/Services/Generation GenDoc/Services/Documents/DocumentArchiveService.cs GenDoc.Tests/Generation/RunPackageReportingTests.cs
git commit -m "fix: sum run counters across all phases and persist errors as JSON

Лічильники запуску бралися лише з docx-фази, тож помилки XLSX і групового
DOCX не було видно на екрані «Запуски». Помилки тепер зберігаються
структуровано в наявній колонці Summary; старий текстовий формат
читається запасним парсером."
```

---

### Task 15: Виправлення — чесний перелік незаповнених тегів і `{{дата_аркуша}}`

Знімає `Skip` з тесту `UnfilledTags_DoNotRepeatTagsThatWereActuallyFilled` (Task 9).

**Files:**
- Modify: `GenDoc/Services/Generation/XlsxGenerationService.cs`
- Modify: `GenDoc/Services/Generation/GenerationService.cs:215-263` (`GetManualTags`)
- Test: `GenDoc.Tests/Generation/PeriodSheetsTests.cs`

**Interfaces:**
- Produces: сигнатури не змінюються; змінюється лише вміст `XlsxGenerationResult.UnfilledTags` і результат `IGenerationService.GetManualTags(int)`.

- [ ] **Step 1: Зняти `Skip` і переконатись у падінні**

Замінити `[Fact(Skip = "Червоний до Task 15 — …")]` на `[Fact]`.

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj --filter "FullyQualifiedName~UnfilledTags_DoNotRepeatTagsThatWereActuallyFilled" -v q --nologo`
Expected: FAIL, 1 failed.

- [ ] **Step 2: Не рахувати вже оброблені теги вдруге**

У `GenDoc/Services/Generation/XlsxGenerationService.cs` замінити `CollectResidualUnfilledTags` так, щоб вона додавала лише ті теги, яких нема серед уже врахованих:

```csharp
        // Після підстановки в аркуші можуть лишитись сирі {{теги}} двох ґатунків:
        // ті, що ми вже позначили незаповненими (не дублюємо), і ті, для яких
        // узагалі немає мапінгу — саме вони цікаві, бо шаблон посилається на поле,
        // якого програма не знає.
        private static void CollectResidualUnfilledTags(IXLWorksheet sheet, List<string> unfilledTags)
        {
            var usedRange = sheet.RangeUsed();
            if (usedRange is null) return;

            var alreadyKnown = new HashSet<string>(unfilledTags, StringComparer.Ordinal);

            foreach (var cell in usedRange.CellsUsed())
            {
                var text = cell.GetString();
                if (!text.Contains("{{", StringComparison.Ordinal)) continue;

                foreach (Match match in PlaceholderRegex.Matches(text))
                {
                    if (alreadyKnown.Add(match.Value))
                        unfilledTags.Add(match.Value);
                }
            }
        }
```

- [ ] **Step 3: Не позначати незаповненим те, що заповнилось**

Причина, з якої заповнені теги все одно потрапляли в список: `SubstituteOutsideTags` записує тег у `unfilledTags` за порожнього значення ДО того, як заміна відбулась, а `ResolvePlaceholderValue` робить те саме для клітинок рядка. Обидва місця коректні. Проблема лише в тому, що після заміни в клітинці могли лишитись ІНШІ теги того самого аркуша, які не мають мапінгу.

Перевірити після Step 2, чи тест уже зелений:

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj --filter "FullyQualifiedName~UnfilledTags_DoNotRepeatTagsThatWereActuallyFilled" -v q --nologo`

Якщо PASS — перейти до Step 4. Якщо все ще FAIL, подивитись, який саме тег лишився в списку, і знайти його в `SubstituteOutsideTags`: там `relevantMappings` відбирає лише теги, присутні на аркуші, але значення береться з `manualValues` за ключем `mapping.PlaceholderTag`. Якщо ключ у словнику записаний без фігурних дужок, заміна не спрацює — у такому разі виправити виклик у `GenerationService.RunXlsxPhase`, який формує `manualValues`, щоб ключі були з дужками, і додати до тесту `Assert` на цей тег.

- [ ] **Step 4: Прибрати `{{дата_аркуша}}` з форми ручних тегів**

`{{дата_аркуша}}` заповнює сам генератор, але тільки коли ввімкнено «аркуш на кожну дату». Для звичайного шаблону цей тег досі має заповнювати людина, тож прибирати його безумовно не можна.

У `GenDoc/Services/Generation/GenerationService.cs`, у методі `GetManualTags`, замінити блок збору тегів XLSX-шаблонів:

```csharp
            var exportTemplates = db.GenerationPackageExportTemplates
                .Where(pt => pt.GenerationPackageId == packageId)
                .OrderBy(pt => pt.SortOrder)
                .Select(pt => new { pt.ExportTemplateId, pt.ExportTemplate!.RepeatSheetPerDate })
                .ToList();

            foreach (var exportTemplate in exportTemplates)
            {
                var manualTags = db.ExportTemplateColumnMappings
                    .Where(m => m.ExportTemplateId == exportTemplate.ExportTemplateId
                                && m.SourceType == MappingSourceType.Manual)
                    .OrderBy(m => m.PlaceholderTag)
                    .Select(m => m.PlaceholderTag)
                    .ToList();

                foreach (var tag in manualTags)
                {
                    // Дату аркуша генератор проставляє сам на кожному клоні —
                    // питати її в користувача означало б просити значення, яке
                    // все одно буде перезаписане. {{період}} навпаки лишається:
                    // саме з нього і беруться дати.
                    if (exportTemplate.RepeatSheetPerDate && tag == XlsxGenerationService.DateSheetTag)
                        continue;

                    if (seen.Add(tag)) tags.Add(tag);
                }
            }
```

- [ ] **Step 5: Додати тест на це правило**

У `GenDoc.Tests/Generation/RunPackageTests.cs` додати:

```csharp
    // {{дата_аркуша}} питати в користувача треба лише тоді, коли аркуші НЕ
    // розмножуються по датах — інакше введене значення все одно затреться.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void GetManualTags_SheetDateTag_IsAskedOnlyWhenSheetsAreNotRepeatedPerDate(
        bool repeatSheetPerDate, bool expectTagInForm)
    {
        using var db = new TestDb();
        int packageId;

        using (var ctx = db.Factory.CreateDbContext())
        {
            var template = new ExportTemplate
            {
                Name = "Роздавальна", OriginalFileName = "r.xlsx",
                Content = Array.Empty<byte>(), UsesPlaceholders = true,
                TemplateRowIndex = 9, RepeatSheetPerDate = repeatSheetPerDate,
                UploadedAt = DateTime.Now
            };
            template.ColumnMappings.Add(new ExportTemplateColumnMapping
            {
                ColumnIndex = 4, HeaderText = string.Empty, FieldKey = string.Empty,
                PlaceholderTag = XlsxGenerationService.DateSheetTag,
                SourceType = MappingSourceType.Manual
            });
            template.ColumnMappings.Add(new ExportTemplateColumnMapping
            {
                ColumnIndex = 0, HeaderText = string.Empty, FieldKey = string.Empty,
                PlaceholderTag = "{{номер_відомості}}",
                SourceType = MappingSourceType.Manual
            });
            ctx.ExportTemplates.Add(template);
            ctx.SaveChanges();

            var package = new GenerationPackage { Name = "Пакет" };
            package.ExportTemplates.Add(new GenerationPackageExportTemplate
            {
                ExportTemplateId = template.Id, SortOrder = 0, FitnessFilter = FitnessFilter.All
            });
            ctx.GenerationPackages.Add(package);
            ctx.SaveChanges();
            packageId = package.Id;
        }

        var tags = BuildService(db).GetManualTags(packageId);

        Assert.Contains("{{номер_відомості}}", tags);
        Assert.Equal(expectTagInForm, tags.Contains(XlsxGenerationService.DateSheetTag));
    }
```

- [ ] **Step 6: Прогнати весь набір і закомітити**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj -v q --nologo`
Expected: PASS, 256 passed, 0 skipped.

```bash
git add GenDoc/Services/Generation GenDoc.Tests/Generation
git commit -m "fix: stop double-reporting unfilled tags and asking for the sheet date

CollectResidualUnfilledTags додавала теги поверх уже врахованих, тож у
зведенні з'являлись теги, які насправді підставились. {{дата_аркуша}}
більше не потрапляє у форму ручних тегів, коли аркуші розмножуються по датах."
```

---

### Task 16: Виправлення — справжня причина відмови під час завантаження шаблону

**Files:**
- Modify: `GenDoc/Services/Templates/TemplateService.cs:42-58`
- Test: `GenDoc.Tests/Templates/TemplateUploadFailureTests.cs` (створити)

**Interfaces:**
- Produces: сигнатура `Upload(string)` не змінюється; змінюється текст у `UploadResult.ErrorMessage`.

- [ ] **Step 1: Написати червоний тест**

Create `GenDoc.Tests/Templates/TemplateUploadFailureTests.cs`:

```csharp
using GenDoc.Services;
using GenDoc.Services.Templates;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Templates;

public class TemplateUploadFailureTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    private string TempFile(string extension, byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, content);
        _tempFiles.Add(path);
        return path;
    }

    private static TemplateService BuildService(TestDb db)
        => new(db.Factory, new FakeAuditLog(), new FakeCurrentUser());

    [Fact]
    public void Upload_FileThatIsNotADocx_SaysSo()
    {
        using var db = new TestDb();
        var path = TempFile(".docx", new byte[] { 0x01, 0x02, 0x03 });

        var result = BuildService(db).Upload(path);

        Assert.False(result.Success);
        Assert.Contains("не є документом Word", result.ErrorMessage);
    }

    // Відсутній файл — це НЕ «не документ Word»; повідомлення має називати причину.
    [Fact]
    public void Upload_MissingFile_ReportsThatFileWasNotFound()
    {
        using var db = new TestDb();
        var missing = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.docx");

        var result = BuildService(db).Upload(missing);

        Assert.False(result.Success);
        Assert.Contains("не знайдено", result.ErrorMessage);
        Assert.DoesNotContain("не є документом Word", result.ErrorMessage);
    }
}
```

- [ ] **Step 2: Прогнати — другий тест має впасти**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj --filter "FullyQualifiedName~TemplateUploadFailureTests" -v q --nologo`
Expected: FAIL, 1 failed, 1 passed.

- [ ] **Step 3: Розділити причини**

У `GenDoc/Services/Templates/TemplateService.cs` замінити блок `try`/`catch` на початку `Upload`:

```csharp
            byte[] content;
            ScanResult scan;

            try
            {
                content = File.ReadAllBytes(filePath);
            }
            catch (FileNotFoundException)
            {
                return new UploadResult(false, $"Файл не знайдено: {filePath}");
            }
            catch (DirectoryNotFoundException)
            {
                return new UploadResult(false, $"Теку не знайдено: {Path.GetDirectoryName(filePath)}");
            }
            catch (IOException ex)
            {
                // Найчастіше — файл відкритий у Word.
                return new UploadResult(false, $"Не вдалося прочитати файл: {ex.Message}");
            }
            catch (UnauthorizedAccessException)
            {
                return new UploadResult(false, "Немає прав на читання цього файлу.");
            }

            try
            {
                using var stream = new MemoryStream(content);
                using var doc = WordprocessingDocument.Open(stream, false);
                scan = ScanPlaceholders(doc);
            }
            catch (Exception ex)
            {
                // Тільки тут «не документ Word» — це справді достовірний висновок.
                return new UploadResult(false,
                    "Файл не є документом Word (.docx). Якщо це текстова чернетка — відкрийте її у Word "
                    + $"і збережіть як .docx. Технічна причина: {ex.Message}");
            }
```

- [ ] **Step 4: Прогнати весь набір і закомітити**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj -v q --nologo`
Expected: PASS, 258 passed.

```bash
git add GenDoc/Services/Templates/TemplateService.cs GenDoc.Tests/Templates/TemplateUploadFailureTests.cs
git commit -m "fix: name the real reason a template upload failed

Один catch без фільтра повертав «Файл не є документом Word» на будь-який
виняток, зокрема на відсутній чи зайнятий файл."
```

---

### Task 17: Виправлення — зрозуміле повідомлення про блок у таблиці

**Files:**
- Modify: `GenDoc/Services/Generation/DocumentGenerationService.cs:182-215`
- Test: `GenDoc.Tests/Generation/DocxRealTemplateTests.cs`

**Interfaces:**
- Produces: `GenerateGroup` за блоку в таблиці повертає `Success = false` з повідомленням, яке називає шаблон і блок, замість того щоб віддавати текст винятку без контексту.

- [ ] **Step 1: Написати червоний тест**

У `GenDoc.Tests/Generation/DocxRealTemplateTests.cs` додати:

```csharp
    // Повторюваний блок усередині таблиці ще не підтримується. Важливо, щоб
    // повідомлення називало шаблон і блок — інакше користувач бачить лише
    // «Тіло повторюваного блоку має бути на одному рівні…» без жодної підказки,
    // який саме файл винен.
    [Fact]
    public void GroupBlockInsideTable_FailsWithMessageNamingTemplateAndBlock()
    {
        var bytes = BuildGroupDocxWithBlockInsideTable();

        var path = OutputPath("table-block.docx");
        var result = new DocumentGenerationService().GenerateGroup(
            TemplateStub("Відомість у таблиці"), bytes,
            new List<IDictionary<string, string>>
            {
                new Dictionary<string, string> { ["{{піб}}"] = "ПЕРШИЙ" }
            },
            new Dictionary<string, string>(), path);

        Assert.False(result.Success);
        Assert.Contains("Відомість у таблиці", result.ErrorMessage);
        Assert.Contains("список", result.ErrorMessage);
        Assert.Contains("таблиц", result.ErrorMessage);
    }

    private static byte[] BuildGroupDocxWithBlockInsideTable()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            doc.AddMainDocumentPart().Document = new Document(new Body());
            var body = doc.MainDocumentPart!.Document.Body!;

            body.AppendChild(new Paragraph(new Run(new Text("{{#список}}"))));

            // Тіло блоку — усередині комірки таблиці, тобто на іншому рівні,
            // ніж маркери.
            var cell = new TableCell(new Paragraph(new Run(new Text("{{піб}}"))));
            body.AppendChild(new Table(new TableRow(cell)));

            body.AppendChild(new Paragraph(new Run(new Text("{{/список}}"))));

            doc.MainDocumentPart.Document.Save();
        }
        return stream.ToArray();
    }
```

- [ ] **Step 2: Прогнати — має впасти на змісті повідомлення**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj --filter "FullyQualifiedName~GroupBlockInsideTable" -v q --nologo`
Expected: FAIL — `result.ErrorMessage` не містить назви шаблону.

- [ ] **Step 3: Передати назву шаблону і блоку в повідомлення**

У `GenDoc/Services/Generation/DocumentGenerationService.cs`:

`ProcessContainer` отримує назву шаблону — змінити сигнатуру і виклики:

```csharp
        private static List<string> ProcessContainer(
            OpenXmlCompositeElement container,
            IReadOnlyList<IDictionary<string, string>> perRecipientValues,
            IDictionary<string, string> sharedValues,
            string templateName)
```

У `GenerateGroup` три виклики стають `ProcessContainer(…, template.Name)`.

Виклик `ExpandBlock` — передати назву блоку і шаблону:

```csharp
                    ExpandBlock(openParagraph!, bodyParagraphs, paragraph, perRecipientValues,
                        sharedWithCount, unfilled, templateName, openBlockName);
```

`ExpandBlock` — нова сигнатура і нове повідомлення:

```csharp
        private static void ExpandBlock(
            Paragraph openParagraph, List<Paragraph> bodyParagraphs, Paragraph closeParagraph,
            IReadOnlyList<IDictionary<string, string>> perRecipientValues,
            IDictionary<string, string> sharedWithCount,
            List<string> unfilled,
            string templateName,
            string blockName)
        {
            if (bodyParagraphs.Any(p => !ReferenceEquals(p.Parent, openParagraph.Parent)))
                throw new InvalidOperationException(
                    $"Шаблон «{templateName}»: тіло блоку «{{{{#{blockName}}}}}» лежить усередині таблиці. "
                    + "Повторювані блоки в таблицях поки не підтримуються — винесіть рядки блоку "
                    + "зі таблиці на рівень маркерів {{#…}}/{{/…}}.");
```

Решта тіла `ExpandBlock` не змінюється.

- [ ] **Step 4: Прогнати**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj --filter "FullyQualifiedName~DocxRealTemplateTests" -v q --nologo`
Expected: PASS, 6 passed.

- [ ] **Step 5: Прогнати весь набір і закомітити**

Run: `dotnet test GenDoc.Tests/GenDoc.Tests.csproj -v q --nologo`
Expected: PASS, 259 passed, 0 skipped.

```bash
git add GenDoc/Services/Generation/DocumentGenerationService.cs GenDoc.Tests/Generation/DocxRealTemplateTests.cs
git commit -m "fix: name the template and block when a repeating block sits in a table"
```

---

## Підсумок

Після Task 17 набір має бути **259 passed, 0 skipped, 0 failed**, а всі сім дефектів зі специфікації — закриті:

| Дефект | Задача-виправлення | Тести |
|---|---|---|
| A1 — ланцюг версій групового DOCX на `NULL`-ключі | Task 12 | Task 4 |
| A2 — групові DOCX не видно в архіві | Task 12 | Task 4 |
| A3 — записи без вмісту кидають виняток | Task 13 | Task 4, Task 13 |
| B1 — лічильники лише з docx-фази | Task 14 | Task 11 |
| B2/B3 — помилки розбираються рядком | Task 14 | Task 11 |
| C1 — шум у `unfilledTags` | Task 15 | Task 9 |
| C2 — `{{дата_аркуша}}` у формі ручних тегів | Task 15 | Task 15 |
| C3 — «не документ Word» на будь-яку причину | Task 16 | Task 16 |
| C4 — блок у таблиці без контексту | Task 17 | Task 17 |

## Знайдене під час роботи

Розділ для дефектів, які виявляться під час виконання плану і не входили до специфікації. Записувати сюди, а не виправляти на місці — інакше сітка безпеки змішується з правками.

- (порожньо)

