# Варіант Б: «моє» для кількох курсових — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Пер-профільний стан для кількох курсових на одному ноутбуку: свій набір, свій останній пакет, свої дати/підписант, фільтр «Всі/Мої» в архіві, домашній екран «Мій набір», закріплена кнопка генерації.

**Architecture:** Нова таблиця `UserSettings` (один рядок на профіль, міграція v26) + `IUserSettingsService` (get-or-create по `ICurrentUserContext`). Наявні сервіси читають користувач-спершу з fallback на глобальні `AppSettings`/глобальний активний набір. Домашній екран — звичайний розділ (`HomeViewModel`/`HomeView`) на наявних сервісах і `NavigateToSectionMessage`.

**Tech Stack:** WPF/.NET 8, EF Core + SQLCipher, CommunityToolkit.Mvvm, xUnit (`TestDb`/`TestServices` на in-memory SQLite).

**Spec:** `docs/superpowers/specs/2026-08-25-my-view-per-officer-design.md`

## Global Constraints

- Нічого з функціоналу не прибирається; глобальні `AppSettings` лишаються як fallback.
- Міграції: без EF Migrations — `DatabaseSchemaInitializer` (гілка версії + БЕЗУМОВНИЙ хвіст; `Ensure*Table` після створення викликає `AddMissingColumns` з повним переліком колонок).
- Гліфи в C# — ТІЛЬКИ `\uXXXX`; брашi — тільки наявні `*Brush` з Theme.xaml, нових hex не вводити.
- Довгих тире в текстах UI немає («-», не «—»).
- Кожна задача — окремий коміт із тестами; `dotnet test -c Release` зелений перед комітом.
- Категорії придатності в UI: «придатні» та «обмежено придатні» (не «звичайні»).

---

### Task 1: Міграція v26 — таблиця `UserSettings` + `IUserSettingsService`

**Files:**
- Create: `GenDoc/Models/UserSettings.cs`
- Create: `GenDoc/Services/IUserSettingsService.cs`
- Create: `GenDoc/Services/UserSettingsService.cs`
- Modify: `GenDoc/Data/AppDbContext.cs` (DbSet + unique index)
- Modify: `GenDoc/Services/DatabaseSchemaInitializer.cs` (`CurrentSchemaVersion` 25→26, `EnsureUserSettingsTable`)
- Modify: `GenDoc/App.xaml.cs` (DI: `services.AddTransient<IUserSettingsService, UserSettingsService>();` поруч із `IUserProfileService`, рядок ~71)
- Modify: `GenDoc.Tests/Infrastructure/TestServices.cs` (хелпер `UserSettings(TestDb, int?)`)
- Test: `GenDoc.Tests/Services/UserSettingsServiceTests.cs`

**Interfaces:**
- Consumes: `ICurrentUserContext.CurrentUserId` (наявний, `int?`).
- Produces (на них спираються Tasks 2–5):
  ```csharp
  public interface IUserSettingsService
  {
      // Get-or-create для поточного користувача. Без користувача (CurrentUserId is null)
      // повертає НЕзбережений UserSettings з дефолтами - читачі працюють, запис - no-op.
      Task<UserSettings> GetForCurrentUserAsync();
      // Завантажує (або створює) рядок поточного користувача, застосовує mutate, зберігає.
      // Без користувача - no-op.
      Task UpdateAsync(Action<UserSettings> mutate);
  }
  ```
  Модель: `UserSettings { int Id; int UserProfileId; UserProfile? UserProfile; int? ActiveIntakeId; int? LastPackageId; bool ArchiveMineOnly; string? LastManualValuesJson; string? LastSignerByTemplateJson; }`

- [ ] **Step 1: Написати тести (падають)**

```csharp
using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Tests.Services;

public class UserSettingsServiceTests
{
    private static int SeedUser(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        var user = new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now };
        ctx.Users.Add(user);
        ctx.SaveChanges();
        return user.Id;
    }

    [Fact]
    public async Task GetForCurrentUser_CreatesOnce_SecondCallReturnsSameRow()
    {
        using var db = new TestDb();
        var userId = SeedUser(db);
        var svc = TestServices.UserSettings(db, userId);

        var first = await svc.GetForCurrentUserAsync();
        var second = await svc.GetForCurrentUserAsync();

        Assert.Equal(first.Id, second.Id);
        using var ctx = db.Factory.CreateDbContext();
        Assert.Equal(1, await ctx.UserSettings.CountAsync());
        Assert.Equal(userId, first.UserProfileId);
        Assert.False(first.ArchiveMineOnly);
    }

    [Fact]
    public async Task Update_PersistsEveryField()
    {
        using var db = new TestDb();
        var userId = SeedUser(db);
        var svc = TestServices.UserSettings(db, userId);

        await svc.UpdateAsync(s =>
        {
            s.ActiveIntakeId = 7;
            s.LastPackageId = 3;
            s.ArchiveMineOnly = true;
            s.LastManualValuesJson = "{\"а\":\"б\"}";
            s.LastSignerByTemplateJson = "{\"ш\":1}";
        });

        var loaded = await svc.GetForCurrentUserAsync();
        Assert.Equal(7, loaded.ActiveIntakeId);
        Assert.Equal(3, loaded.LastPackageId);
        Assert.True(loaded.ArchiveMineOnly);
        Assert.Equal("{\"а\":\"б\"}", loaded.LastManualValuesJson);
        Assert.Equal("{\"ш\":1}", loaded.LastSignerByTemplateJson);
    }

    [Fact]
    public async Task WithoutUser_ReadReturnsDefaults_WriteIsNoop()
    {
        using var db = new TestDb();
        var svc = TestServices.UserSettings(db, userId: null);

        var settings = await svc.GetForCurrentUserAsync();
        await svc.UpdateAsync(s => s.ArchiveMineOnly = true);

        Assert.False(settings.ArchiveMineOnly);
        using var ctx = db.Factory.CreateDbContext();
        Assert.Equal(0, await ctx.UserSettings.CountAsync());
    }

    [Fact]
    public async Task DeletingProfile_CascadesSettings()
    {
        using var db = new TestDb();
        var userId = SeedUser(db);
        var svc = TestServices.UserSettings(db, userId);
        await svc.GetForCurrentUserAsync();

        using (var ctx = db.Factory.CreateDbContext())
        {
            var user = await ctx.Users.FirstAsync(u => u.Id == userId);
            ctx.Users.Remove(user);
            await ctx.SaveChangesAsync();
        }

        using var check = db.Factory.CreateDbContext();
        Assert.Equal(0, await check.UserSettings.CountAsync());
    }
}
```

У `TestServices.cs` додати (за зразком наявних фабрик; `FixedUserContext` — крихітна реалізація `ICurrentUserContext` там само):

```csharp
private sealed class FixedUserContext : ICurrentUserContext
{
    public int? CurrentUserId { get; private set; }
    public string? CurrentUserFullName { get; private set; }
    public FixedUserContext(int? id) { CurrentUserId = id; CurrentUserFullName = id?.ToString(); }
    public void SetCurrentUser(int userId, string fullName) { CurrentUserId = userId; CurrentUserFullName = fullName; }
    public void Clear() { CurrentUserId = null; CurrentUserFullName = null; }
}

public static UserSettingsService UserSettings(TestDb db, int? userId) =>
    new(db.Factory, new FixedUserContext(userId));
```

- [ ] **Step 2: Переконатись, що тести падають** — `dotnet test -c Release --filter UserSettingsServiceTests` → компіляція падає (типів ще немає).

- [ ] **Step 3: Модель + DbSet + сервіс**

`GenDoc/Models/UserSettings.cs`:

```csharp
namespace GenDoc.Models
{
    // Пер-профільний стан (v26): мій набір, останній пакет, фільтр «Мої» в архіві,
    // дати/підписант «з минулого разу». Глобальні AppSettings лишаються fallback-ом.
    public class UserSettings
    {
        public int Id { get; set; }
        public int UserProfileId { get; set; }
        public UserProfile? UserProfile { get; set; }
        public int? ActiveIntakeId { get; set; }
        public int? LastPackageId { get; set; }
        public bool ArchiveMineOnly { get; set; }
        public string? LastManualValuesJson { get; set; }
        public string? LastSignerByTemplateJson { get; set; }
    }
}
```

`AppDbContext`: `public DbSet<UserSettings> UserSettings => Set<UserSettings>();` і в `OnModelCreating` (за зразком сусідніх):

```csharp
modelBuilder.Entity<UserSettings>(e =>
{
    e.HasIndex(s => s.UserProfileId).IsUnique();
    e.HasOne(s => s.UserProfile).WithMany().HasForeignKey(s => s.UserProfileId)
        .OnDelete(DeleteBehavior.Cascade);
});
```

`UserSettingsService` (новий файл, інтерфейс окремим файлом):

```csharp
using GenDoc.Data;
using GenDoc.Models;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services
{
    public class UserSettingsService : IUserSettingsService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly ICurrentUserContext _currentUser;

        public UserSettingsService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUserContext currentUser)
        {
            _dbFactory = dbFactory;
            _currentUser = currentUser;
        }

        public async Task<UserSettings> GetForCurrentUserAsync()
        {
            if (_currentUser.CurrentUserId is not int userId)
                return new UserSettings();

            using var db = _dbFactory.CreateDbContext();
            var existing = await db.UserSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserProfileId == userId);
            if (existing is not null) return existing;

            var created = new UserSettings { UserProfileId = userId };
            db.UserSettings.Add(created);
            await db.SaveChangesAsync();
            return created;
        }

        public async Task UpdateAsync(Action<UserSettings> mutate)
        {
            if (_currentUser.CurrentUserId is not int userId) return;

            using var db = _dbFactory.CreateDbContext();
            var row = await db.UserSettings.FirstOrDefaultAsync(s => s.UserProfileId == userId);
            if (row is null)
            {
                row = new UserSettings { UserProfileId = userId };
                db.UserSettings.Add(row);
            }
            mutate(row);
            await db.SaveChangesAsync();
        }
    }
}
```

- [ ] **Step 4: Міграція v26.** У `DatabaseSchemaInitializer`: `CurrentSchemaVersion = 26`; гілка `if (version < 26) EnsureUserSettingsTable(db);` і той самий виклик у безумовному хвості (поруч з `EnsureGroupDocumentRecipientsTable`, рядки ~524 і ~563). Реалізація — дзеркало `EnsureGroupDocumentRecipientsTable` (обгортка на `AppDbContext` + `internal static` на `DbConnection` для юніт-тесту):

```sql
CREATE TABLE "UserSettings" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_UserSettings" PRIMARY KEY AUTOINCREMENT,
    "UserProfileId" INTEGER NOT NULL,
    "ActiveIntakeId" INTEGER NULL,
    "LastPackageId" INTEGER NULL,
    "ArchiveMineOnly" INTEGER NOT NULL DEFAULT 0,
    "LastManualValuesJson" TEXT NULL,
    "LastSignerByTemplateJson" TEXT NULL,
    CONSTRAINT "FK_UserSettings_Users_UserProfileId"
        FOREIGN KEY ("UserProfileId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);
CREATE UNIQUE INDEX "IX_UserSettings_UserProfileId" ON "UserSettings" ("UserProfileId");
```

Після `CREATE TABLE`-гілки — БЕЗУМОВНО дописати відсутні колонки повним переліком (конвенція проти пастки «таблицю створив проміжний білд»): використати той самий helper/патерн `AddMissingColumns`, що й у наявному хвості ініціалізатора (NOT NULL — з DEFAULT). Юніт-тест на `internal static`-гілку — за зразком тестів `EnsureWeaponVehicleTables`.

- [ ] **Step 5: DI + TestServices, прогнати тести** — `dotnet test -c Release --filter UserSettingsServiceTests` → PASS; потім повний прогін.

- [ ] **Step 6: Commit** — `git commit -m "Варіант Б (1/6): міграція v26 - UserSettings, пер-профільний стан і сервіс"`.

---

### Task 2: Мій набір — `ActiveIntakeState` користувач-спершу + дія «Зробити моїм»

**Files:**
- Modify: `GenDoc/Services/ActiveIntakeState.cs`
- Modify: `GenDoc/ViewModels/Intakes/IntakesViewModel.cs` (нова команда)
- Modify: `GenDoc/Views/Intakes/IntakesView.xaml` (кнопка на картці набору)
- Test: `GenDoc.Tests/Services/ActiveIntakeStateTests.cs`

**Interfaces:**
- Consumes: `IUserSettingsService` (Task 1), `IIntakeService.GetActiveAsync()`, `IIntakeService.GetByIdAsync(int)`.
- Produces: `ActiveIntakeState.RefreshAsync()` тепер віддає МІЙ набір; `internal static Intake? Pick(Intake? mine, Intake? globalActive)` — чисте правило вибору. Команда `MakeMineCommand` (з `MakeMineAsync(IntakeCardViewModel?)`) в `IntakesViewModel`.

- [ ] **Step 1: Тест правила вибору (падає)**

```csharp
using GenDoc.Models;
using GenDoc.Services;

namespace GenDoc.Tests.Services;

// Мій набір: обраний профілем, якщо він ще існує; інакше глобальний активний.
public class ActiveIntakeStateTests
{
    private static Intake Intake(int id) => new() { Id = id, Number = id, DisplayNumber = $"Набір №{id}" };

    [Fact]
    public void Pick_PrefersMine() =>
        Assert.Equal(5, ActiveIntakeState.Pick(Intake(5), Intake(4))!.Id);

    [Fact]
    public void Pick_FallsBackToGlobal_WhenMineMissing() =>
        Assert.Equal(4, ActiveIntakeState.Pick(null, Intake(4))!.Id);

    [Fact]
    public void Pick_NullWhenNothing() =>
        Assert.Null(ActiveIntakeState.Pick(null, null));
}
```

- [ ] **Step 2: Переконатись, що падає** (методу `Pick` немає).

- [ ] **Step 3: Реалізація.** `ActiveIntakeState`: додати в ctor `IUserSettingsService userSettings`; `RefreshAsync`:

```csharp
public async Task RefreshAsync()
{
    var settings = await _userSettings.GetForCurrentUserAsync();
    Intake? mine = settings.ActiveIntakeId is int id
        ? await _intakeService.GetByIdAsync(id)   // null, якщо набір видалили
        : null;
    Current = Pick(mine, mine is null ? await _intakeService.GetActiveAsync() : null);
    WeakReferenceMessenger.Default.Send(new ActiveIntakeChangedMessage());
}

// Чисте правило: свій набір, поки він існує; інакше глобальний активний.
internal static Intake? Pick(Intake? mine, Intake? globalActive) => mine ?? globalActive;
```

(`GetActiveAsync` викликати лише коли свого немає — він має побічний ефект автопереходу статусів, зайвий запит не потрібен.)

- [ ] **Step 4: Команда в «Наборах».** В `IntakesViewModel` (поруч з `OpenGeneration`, DI-поле `IUserSettingsService`):

```csharp
[RelayCommand]
private async Task MakeMineAsync(IntakeCardViewModel? card)
{
    if (card is null) return;
    await _userSettings.UpdateAsync(s => s.ActiveIntakeId = card.Id);
    await _activeIntakeState.RefreshAsync();
}
```

У `IntakesView.xaml` на картці набору — кнопка `Content="Зробити моїм"` стилем `TextLinkButtonStyle` поруч із наявними діями картки, `Command="{Binding DataContext.MakeMineCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}" CommandParameter="{Binding}"` (той самий патерн, що в сусідніх кнопок картки — звірити на місці).

- [ ] **Step 5: Тести + повний прогін** → PASS.

- [ ] **Step 6: Commit** — `"Варіант Б (2/6): мій набір - ActiveIntakeState користувач-спершу, дія «Зробити моїм»"`.

---

### Task 3: Пам'ять останнього пакета + ручні значення користувач-спершу

**Files:**
- Modify: `GenDoc/Services/Completeness/CompletenessService.cs:414` (`GetDefaultPackageIdAsync`)
- Modify: `GenDoc/ViewModels/Generation/GenerationViewModel.cs` (запис `LastPackageId` при виборі пакета)
- Modify: `GenDoc/Services/Generation/ManualTagFormBuilder.cs:120-165` (читання/запис Json)
- Modify: `GenDoc/Services/Staff/StaffService.cs:155-185` (те саме для постійного складу)
- Test: `GenDoc.Tests/Completeness/DefaultPackageChainTests.cs`; доповнити наявні тести `ManualTagFormBuilder` (знайти по `LastManualValuesJson` у GenDoc.Tests)

**Interfaces:**
- Consumes: `IUserSettingsService` (Task 1).
- Produces: `GetDefaultPackageIdAsync()` — той самий підпис, новий ланцюжок: `UserSettings.LastPackageId` (якщо пакет існує) → `AppSettings.DefaultGenerationPackageId` → перший за алфавітом. Всі 4 наявні виклики (`CompletenessViewModel`, `GenerationViewModel`, `GenerateDocumentsDialogViewModel`, `IntakeService`) отримують нову поведінку автоматично.

- [ ] **Step 1: Тест ланцюжка (падає).** Розширити `TestServices.Completeness(db)` параметром `int? userId = null` (передає `UserSettingsService` з `FixedUserContext` із Task 1).

```csharp
using GenDoc.Models;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Completeness;

// Стартовий пакет: мій останній → глобальний типовий → перший за алфавітом.
public class DefaultPackageChainTests
{
    private static (int UserId, int PackageA, int PackageB) Seed(TestDb db, int? globalDefault = null)
    {
        using var ctx = db.Factory.CreateDbContext();
        var user = new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now };
        ctx.Users.Add(user);
        var a = new GenerationPackage { Name = "А-пакет" };
        var b = new GenerationPackage { Name = "Б-пакет" };
        ctx.GenerationPackages.AddRange(a, b);
        ctx.SaveChanges();
        if (globalDefault == -1) globalDefault = a.Id; // сентинел «глобальний = А»
        var settings = ctx.AppSettings.First();
        settings.DefaultGenerationPackageId = globalDefault;
        ctx.SaveChanges();
        return (user.Id, a.Id, b.Id);
    }

    [Fact]
    public async Task MyLastPackage_WinsWhenItExists()
    {
        using var db = new TestDb();
        var (userId, _, packageB) = Seed(db);
        await TestServices.UserSettings(db, userId).UpdateAsync(s => s.LastPackageId = packageB);

        Assert.Equal(packageB, await TestServices.Completeness(db, userId).GetDefaultPackageIdAsync());
    }

    [Fact]
    public async Task DeletedLastPackage_FallsBackToGlobalDefault()
    {
        using var db = new TestDb();
        var (userId, packageA, _) = Seed(db, globalDefault: -1);
        await TestServices.UserSettings(db, userId).UpdateAsync(s => s.LastPackageId = 999_999);

        Assert.Equal(packageA, await TestServices.Completeness(db, userId).GetDefaultPackageIdAsync());
    }

    [Fact]
    public async Task NothingRemembered_FirstAlphabetically()
    {
        using var db = new TestDb();
        var (userId, packageA, _) = Seed(db);

        Assert.Equal(packageA, await TestServices.Completeness(db, userId).GetDefaultPackageIdAsync());
    }
}
```

(Якщо в `TestDb` немає засіяного рядка `AppSettings` — створити його в `Seed` замість `First()`; звірити з наявними тестами, які читають `AppSettings`.)

- [ ] **Step 2: Переконатись, що падає.**

- [ ] **Step 3: Реалізація.** `CompletenessService` отримує `IUserSettingsService` у ctor; `GetDefaultPackageIdAsync`:

```csharp
public async Task<int?> GetDefaultPackageIdAsync()
{
    using var db = _dbFactory.CreateDbContext();

    var mine = (await _userSettings.GetForCurrentUserAsync()).LastPackageId;
    if (mine is int m && await db.GenerationPackages.AnyAsync(p => p.Id == m)) return m;

    var global = await db.AppSettings.Select(s => s.DefaultGenerationPackageId).FirstOrDefaultAsync();
    if (global is int g && await db.GenerationPackages.AnyAsync(p => p.Id == g)) return g;

    return await db.GenerationPackages.OrderBy(p => p.Name).Select(p => (int?)p.Id).FirstOrDefaultAsync();
}
```

(Поточне тіло на рядку 414 звірити й зберегти його «перший за алфавітом»-гілку як останню.)

`GenerationViewModel`: у місці, де користувач обирає пакет (обробник зміни `SelectedPackage`/метод вибору картки — знайти по присвоєнню обраного пакета), додати `await _userSettings.UpdateAsync(s => s.LastPackageId = package.Id);` (DI-поле додати).

- [ ] **Step 4: Ручні значення користувач-спершу.** У `ManualTagFormBuilder`: обидва читання (`рядки ~153, ~161`) замінити на «спершу `UserSettings`, порожньо → `AppSettings`»; обидва записи (`~125-146`) — мерджити і зберігати ЛИШЕ в `UserSettings` через `UpdateAsync` (глобальні поля більше не пишуться). Той самий прийом у `StaffService` (`~160-181`). Обидва класи отримують `IUserSettingsService` у ctor; `TestServices.Staff`/фабрики тестів доповнити.

- [ ] **Step 5: Тести + повний прогін** → PASS (наявні тести ManualTagFormBuilder переїдуть на user-aware фабрику).

- [ ] **Step 6: Commit** — `"Варіант Б (3/6): останній пакет і ручні значення - пер-профільні з fallback на глобальні"`.

---

### Task 4: Архів — перемикач «Всі / Мої»

**Files:**
- Modify: `GenDoc/Services/Documents/ArchiveModels.cs` (`GroupArchiveFilter` + `int? UserId = null`)
- Modify: `GenDoc/Services/Documents/IDocumentArchiveService.cs` + `DocumentArchiveService.cs` (`GetRunsAsync(int? intakeId, int? year, int? userId = null)`; фільтр у `QueryGroupAsync`)
- Modify: `GenDoc/ViewModels/Archive/ArchiveViewModel.cs` (властивість `MineOnly`, підстановка у три запити, збереження в `UserSettings`)
- Modify: `GenDoc/Views/Archive/ArchiveView.xaml` (перемикач «Всі / Мої» поруч із фільтрами)
- Test: `GenDoc.Tests/Archive/ArchiveMineFilterTests.cs`

**Interfaces:**
- Consumes: `IUserSettingsService`, `ICurrentUserContext`, наявний `ArchiveFilter.UserId` (уже є — «Мої» для вкладки «Документи» його й використовує).
- Produces: `GroupArchiveFilter(int? ExportTemplateId, int? DocxTemplateId, int? Year, int Skip, int Take, int? UserId = null)`; `GetRunsAsync(int? intakeId, int? year, int? userId = null)`.

- [ ] **Step 1: Тести (падають).**

```csharp
using GenDoc.Models;
using GenDoc.Services.Documents;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Archive;

// «Мої» в архіві: фільтр по автору на групових і запусках
// (вкладка «Документи» вже вміє ArchiveFilter.UserId - її покривають наявні тести).
public class ArchiveMineFilterTests
{
    private static (int U1, int U2) Seed(TestDb db)
    {
        using var ctx = db.Factory.CreateDbContext();
        var u1 = new UserProfile { FullName = "Перший", PasswordHash = "x", CreatedAt = DateTime.Now };
        var u2 = new UserProfile { FullName = "Другий", PasswordHash = "x", CreatedAt = DateTime.Now };
        ctx.Users.AddRange(u1, u2);
        ctx.SaveChanges();

        ctx.GeneratedGroupDocuments.AddRange(
            new GeneratedGroupDocument { GeneratedAt = DateTime.Now, GeneratedByUserId = u1.Id, FileName = "a.xlsx", Version = 1, IsCurrent = true, RecipientCount = 1 },
            new GeneratedGroupDocument { GeneratedAt = DateTime.Now, GeneratedByUserId = u2.Id, FileName = "b.xlsx", Version = 1, IsCurrent = true, RecipientCount = 1 });
        ctx.GenerationPackageRuns.AddRange(
            new GenerationPackageRun { RunAt = DateTime.Now, RunByUserId = u1.Id },
            new GenerationPackageRun { RunAt = DateTime.Now, RunByUserId = u2.Id });
        ctx.SaveChanges();
        return (u1.Id, u2.Id);
    }

    [Fact]
    public async Task QueryGroup_MineOnly_FiltersByAuthor()
    {
        using var db = new TestDb();
        var (u1, _) = Seed(db);
        var svc = TestServices.Archive(db);

        var mine = await svc.QueryGroupAsync(new GroupArchiveFilter(null, null, null, 0, 50, UserId: u1));
        var all = await svc.QueryGroupAsync(new GroupArchiveFilter(null, null, null, 0, 50));

        Assert.Single(mine);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task GetRuns_MineOnly_FiltersByRunAuthor()
    {
        using var db = new TestDb();
        var (u1, _) = Seed(db);
        var svc = TestServices.Archive(db);

        var mine = await svc.GetRunsAsync(null, null, u1);
        var all = await svc.GetRunsAsync(null, null);

        Assert.Single(mine);
        Assert.Equal(2, all.Count);
    }
}
```

(Обов'язкові поля сіда звірити з наявними тестами архіву — якщо `GeneratedGroupDocument`/`GenerationPackageRun` вимагають ще щось NOT NULL, скопіювати мінімальний сід звідти.)

- [ ] **Step 2: Переконатись, що падає** (нових параметрів немає).

- [ ] **Step 3: Сервіс.** `QueryGroupAsync`: `if (filter.UserId is int uid) query = query.Where(g => g.GeneratedByUserId == uid);` — поруч із наявними умовами. `GetRunsAsync`: третій параметр, `if (userId is int u) query = query.Where(r => r.RunByUserId == u);`. Вкладка «Документи» вже фільтрує по `ArchiveFilter.UserId` — нічого не міняти в запиті.

- [ ] **Step 4: VM + XAML.** В `ArchiveViewModel`: `[ObservableProperty] private bool mineOnly;` — ініціалізується з `UserSettings.ArchiveMineOnly` при вході в розділ; `OnMineOnlyChanged` → `UpdateAsync(s => s.ArchiveMineOnly = value)` + перезавантаження активної вкладки. У побудові фільтрів: документи — `UserId = MineOnly ? _currentUser.CurrentUserId : <наявне значення з комбобокса авторів>`; групові й запуски — `UserId/userId = MineOnly ? _currentUser.CurrentUserId : null`. У XAML — два `RadioButton` «Всі» / «Мої» стилем сегментів (як перемикачі у вікні «Вимоги пакета» — `RequirementSegmentStyle`-подібний, взяти спільний стиль з Theme.xaml, якщо він там уже є) в рядку фільтрів, видимий на всіх трьох вкладках.

- [ ] **Step 5: Тести + повний прогін** → PASS.

- [ ] **Step 6: Commit** — `"Варіант Б (4/6): архів - перемикач «Всі / Мої» по автору на всіх трьох вкладках"`.

---

### Task 5: Домашній екран «Мій набір»

**Files:**
- Create: `GenDoc/ViewModels/Home/HomeViewModel.cs`
- Create: `GenDoc/Views/Home/HomeView.xaml` + `HomeView.xaml.cs`
- Modify: `GenDoc/ViewModels/Shell/MainViewModel.cs:92` (новий `NavigationItem` ПЕРШИМ у списку; стартовий розділ НЕ міняти — лишити «Особовий склад»)
- Modify: `GenDoc/App.xaml.cs` (DI `AddTransient<HomeViewModel>`), DataTemplate VM→View там, де мапляться інші розділи (звірити місце по `PersonnelViewModel`)
- Test: `GenDoc.Tests/Home/HomeViewModelTests.cs`

**Interfaces:**
- Consumes: `ActiveIntakeState` (`Current`, `StatusText`), `ICompletenessService.GetBadgeCountAsync()`, `IDocumentArchiveService.GetRunsAsync(null, null, userId)` (Task 4), `WeakReferenceMessenger` + `NavigateToSectionMessage(MainViewModel.*SectionTitle, payload)` — той самий патерн, що `IntakesViewModel.OpenGeneration` (`IntakesViewModel.cs:195-202`).
- Produces: `HomeViewModel.InitializeAsync()`; властивості `IntakeTitle` («Набір №4 · день 19 з 57»), `PeopleCountText`, `MissingCount`, `LastRunText`, `HasIntake`, `HasLastRun`; команди `OpenGenerationCommand`, `OpenCompletenessCommand`, `OpenArchiveCommand`, `OpenLastRunCommand`, `OpenIntakesCommand`.

- [ ] **Step 1: Тести чистих функцій VM (падають).** Збирання текстів — `internal static` функції; розрахунок «день X з Y» винести в `ActiveIntakeState.DayOfTotal(Intake, DateOnly)` і викликати з обох місць (`StatusText` і `HomeViewModel`), НЕ дублювати формулу.

```csharp
using GenDoc.Models;
using GenDoc.Services;
using GenDoc.ViewModels.Home;

namespace GenDoc.Tests.Home;

public class HomeViewModelTests
{
    private static Intake Intake4() => new()
    {
        Id = 4, Number = 4, DisplayNumber = "Набір №4",
        DateStart = new DateOnly(2026, 8, 6), DateEnd = new DateOnly(2026, 10, 1)
    };

    [Fact]
    public void DayOfTotal_CountsInclusive() =>
        Assert.Equal((19, 57), ActiveIntakeState.DayOfTotal(Intake4(), new DateOnly(2026, 8, 24)));

    [Fact]
    public void BuildIntakeTitle_ReadsAsStatusLine() =>
        Assert.Equal("Набір №4 · день 19 з 57",
            HomeViewModel.BuildIntakeTitle(Intake4(), new DateOnly(2026, 8, 24)));

    [Fact]
    public void BuildLastRunText_ShowsDatePackageCount() =>
        Assert.Equal("20.08.2026 10:38 · пакет «Зброя» · згенеровано 4",
            HomeViewModel.BuildLastRunText(new DateTime(2026, 8, 20, 10, 38, 0), "Зброя", 4));

    [Fact]
    public void BuildLastRunText_WithoutPackage_SaysSelective() =>
        Assert.Equal("20.08.2026 10:38 · Вибірково · згенеровано 2",
            HomeViewModel.BuildLastRunText(new DateTime(2026, 8, 20, 10, 38, 0), null, 2));
}
```

(Поля `Intake.DateStart/DateEnd` — `DateOnly`, як в `ActiveIntakeState.StatusText`; сигнатуру `BuildLastRunText(DateTime runAt, string? packageName, int generated)` VM викликає з даними `RunDto`. `HasIntake`/`HasLastRun` — прості похідні `Current is null`/`FirstOrDefault() is null`, окремих тестів не потребують.)

- [ ] **Step 2: Переконатись, що падає.**

- [ ] **Step 3: VM.** `InitializeAsync`: `await _activeIntakeState.RefreshAsync()`; заповнити властивості (`MissingCount` — з `GetBadgeCountAsync()`; останній запуск — `(await _archive.GetRunsAsync(null, null, _currentUser.CurrentUserId)).FirstOrDefault()`). Команди — `WeakReferenceMessenger.Default.Send(new NavigateToSectionMessage(MainViewModel.GenerationSectionTitle, null))` і аналогічно для «Комплектність»/«Архів документів»/«Набори»; `OpenLastRunCommand` — з тим самим payload, що «Показати в архіві» в картці підсумку генерації (звірити тип у `GenerationResultViewModel`).

- [ ] **Step 4: View.** Картки на `PanelBrush`/`BorderLineBrush` (за зразком карток «Генерації»): картка набору (заголовок + `PeopleCountText`), картка готовності («бракує N» + кнопка «Перейти до комплектності»), картка останнього запуску (текст + «Показати в архіві»), рядок швидких дій. Порожні стани: `HasIntake=false` → текст «Оберіть набір у "Наборах"» + кнопка (як порожній стан «Запусків», `2.6`); `HasLastRun=false` → «Запусків ще не було» + «Перейти до генерації». Розділ у меню: `new NavigationItem("Мій набір", "", () => _serviceProvider.GetRequiredService<HomeViewModel>())` ПЕРШИМ (гліф `` — Home у MDL2; якщо на екрані виявиться порожнім квадратом, взяти `` і переконатись, що «Кімнати» отримали інший).

- [ ] **Step 5: Тести + повний прогін + живий погляд** (запуск застосунку, знімок розділу).

- [ ] **Step 6: Commit** — `"Варіант Б (5/6): домашній розділ «Мій набір» - картка набору, готовність, останній запуск"`.

---

### Task 6: «Генерація» — закріплений низ без наскрізної прокрутки

**Files:**
- Modify: `GenDoc/Views/Generation/GenerationView.xaml` (права панель: рядки 44–480)

**Interfaces:** нових немає — тільки розкладка, біндинги не міняються.

- [ ] **Step 1: Розкладка.** Зовнішній `ScrollViewer` правої панелі (рядок 44 … 480) замінити на `Grid` з трьома рядками:
  - `RowDefinition Height="Auto"` — шапка «ШАБЛОНИ ПАКЕТА» + кнопка «⚙ Вимоги» (рядки ~170-175);
  - `RowDefinition Height="*"` — `ScrollViewer` з рештою вмісту (шаблони, «ОСОБОВИЙ СКЛАД», «ДАТА ДОКУМЕНТА», «ЗНАЧЕННЯ ДЛЯ ЦІЄЇ ГЕНЕРАЦІЇ»);
  - `RowDefinition Height="Auto"` — низ: «Тека документів:» (рядок ~421), чекбокс «Генерувати повторно вже наявні», кнопка `GenerateAllCommand` (рядок ~432) і картка підсумку.

  Пастки, які тут стріляють (з конвенцій): НЕ давати внутрішньому `ScrollViewer` жити в Auto-рядку (Auto міряє нескінченністю — прокрутка не клацне); ліва колонка зі списком пакетів не чіпається.

- [ ] **Step 2: Збірка + повний прогін тестів** (розкладка тестами не покривається; переконатись, що нічого не зламалось компіляційно).

- [ ] **Step 3: Живий знімок.** Запустити застосунок (вхід — паролі в користувача), «Генерація» → пакет: кнопка й картка підсумку видимі БЕЗ прокрутки на 1180x720 логічного розміру вікна; середина прокручується.

- [ ] **Step 4: Commit** — `"Варіант Б (6/6): «Генерація» - кнопка і підсумок закріплені внизу, прокручується лише середина"`.

---

## Після всіх задач

- Повний `dotnet test -c Release` зелений; запушити гілку.
- Живий прогін за спекою: два профілі на одній базі — свій набір, свій останній пакет, свої дати/підписант, перемикач «Мої», домашній екран.
- Оновити пам'ять проєкту (стан роботи, нові пастки, якщо будуть).
