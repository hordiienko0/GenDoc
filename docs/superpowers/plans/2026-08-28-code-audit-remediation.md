# Виправлення за наскрізним аудитом коду — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Закрити 28 дефектів з наскрізного аудиту 2026-08-28 — розблокувати збірку, припинити псування даних, звести підрахунок готовності до одного правила, виправити відмінювання в документах і дві прогалини в конвенції міграцій.

**Architecture:** Виправлення йдуть блоками A→F у порядку залежності: A розблоковує збірку (без нього не прогнати тести), далі незалежні блоки. Кожен дефект отримує тест, що спершу падає. Там, де той самий клас помилки трапився втретє (забутий `Include`), додається тест-сторож, який ловитиме майбутні повторення.

**Tech Stack:** .NET 8, WPF, EF Core 8 + SQLCipher, CommunityToolkit.Mvvm 8.4.2, ClosedXML 0.105, xunit 2.5.3.

**Spec:** `docs/superpowers/specs/2026-08-28-code-audit-remediation-design.md`

## Global Constraints

- Нічого з функціоналу не прибирається; глобальні `AppSettings` лишаються замороженим fallback-ом.
- Міграції: колонку дописувати В ОБИДВА місця — гілку версії й безумовний хвіст `EnsureInitialized`. NOT NULL — лише з DEFAULT.
- Теги в БД лежать З ДУЖКАМИ (`{{піб}}`); порівняння — через `ManualTagClassifier.Normalize`.
- Гліфи Segoe MDL2 у C# — тільки через `\uXXXX`.
- Кирилиця: порівняння без урахування регістру — в пам'яті через `uk-UA`, не SQL.
- `{StaticResource}` — лише на ресурси зі своєї області видимості або з `Theme.xaml`.
- Прогін тестів: `dotnet test -c Debug --nologo`. Базова лінія — 419 тестових методів, зелено.
- Комміти — по одному на задачу, повідомлення українською в стилі гілки.

---

### Task A1: Домашній розділ «Мій набір» — в'юха, DI, пункт меню

**Files:**
- Create: `GenDoc/Views/Home/HomeView.xaml`
- Modify: `GenDoc/App.xaml.cs` (реєстрація), `GenDoc/ViewModels/Shell/MainViewModel.cs` (пункт меню), `GenDoc/MainWindow.xaml` (DataTemplate)
- Test: наявний `GenDoc.Tests/Home/HomeViewModelTests.cs` (уже написаний, має позеленіти)

**Interfaces:**
- Consumes: `HomeViewModel` (готовий) — `HasIntake`, `IntakeTitle`, `PeopleCountText`, `MissingCountText`, `HasLastRun`, `LastRunText`, команди `OpenGenerationCommand`, `OpenCompletenessCommand`, `OpenArchiveCommand`, `OpenIntakesCommand`, `OpenLastRunCommand`, метод `InitializeAsync()`.
- Produces: розділ «Мій набір» у навігації; `HomeView` як `DataTemplate` для `HomeViewModel`.

- [ ] **Step 1: Переконатись, що збірка падає саме на цьому**

Run: `dotnet build GenDoc.sln -v q --nologo`
Expected: `error CS0103: The name 'InitializeComponent' does not exist` у `HomeView.xaml.cs(11,9)`.

- [ ] **Step 2: Створити `HomeView.xaml`**

`UserControl` з `Loaded="HomeView_Loaded"` (обробник уже є в code-behind). Три картки на `PanelBrush` з `BorderLineBrush`, за зразком карток у `GenerationView.xaml`. Порожні стани через `Visibility` на `HasIntake`/`HasLastRun` — обидва слоти задавати явно (пастка: `ContentControl` із заданим `ContentTemplate` малює шаблон навіть при `Content = null`).

- [ ] **Step 3: Зареєструвати в DI і додати `DataTemplate`**

`App.xaml.cs`, поруч із рядком 113: `services.AddTransient<ViewModels.Home.HomeViewModel>();`
`MainWindow.xaml`, поруч з іншими: `<DataTemplate DataType="{x:Type homeVm:HomeViewModel}"><homeViews:HomeView/></DataTemplate>` + оголошення просторів імен.

- [ ] **Step 4: Пункт меню першим**

`MainViewModel`: константа `HomeSectionTitle = "Мій набір"` і `new NavigationItem(HomeSectionTitle, "", () => _serviceProvider.GetRequiredService<HomeViewModel>())` першим у групі. Стартовим розділом лишається «Особовий склад».

- [ ] **Step 5: Збірка + тести**

Run: `dotnet build GenDoc.sln -v q --nologo` → 0 errors
Run: `dotnet test -c Debug --nologo` → усі зелені, `HomeViewModelTests` серед них.

- [ ] **Step 6: Commit** — `"Аудит A1: домашній розділ «Мій набір» - в'юха, DI, пункт меню (збірка розблокована)"`

---

### Task A2: `IntakeWizardViewModel` у DI

**Files:**
- Modify: `GenDoc/App.xaml.cs`
- Test: `GenDoc.Tests/DependencyRegistrationTests.cs` (створити)

**Interfaces:**
- Produces: тест-сторож `AllDialogViewModels_AreRegistered`, що ловитиме майбутні пропуски.

- [ ] **Step 1: Тест, що падає.** Пройтись рефлексією по `DialogService._viewModelToWindow` (зробити мапу `internal static`) і перевірити, що кожен ключ резолвиться з контейнера. Очікуваний провал: `IntakeWizardViewModel` не зареєстрований.
- [ ] **Step 2: Запустити, побачити провал.**
- [ ] **Step 3: Додати `services.AddTransient<IntakeWizardViewModel>();`**
- [ ] **Step 4: Тести зелені.**
- [ ] **Step 5: Commit** — `"Аудит A2: IntakeWizardViewModel у DI + сторож реєстрації діалогів"`

---

### Task A3: Пакетне «Зберегти як» для групових відомостей

**Files:**
- Modify: `GenDoc/Services/Documents/DocumentArchiveService.cs` (`SaveGroupAsAsync`), `GenDoc/ViewModels/Archive/ArchiveViewModel.cs:606,886,1100,1128`, `ViewModels/Archive/VersionHistoryViewModel.cs:122`, `GroupVersionHistoryViewModel.cs:86`
- Test: `GenDoc.Tests/Archive/ArchiveExportAndFilterTests.cs`

- [ ] **Step 1: Тест, що падає.** `SaveGroupAsAsync` у неіснуючу підтеку має створити її й записати файл. Очікуваний провал: `DirectoryNotFoundException`.
- [ ] **Step 2: Запустити, побачити провал.**
- [ ] **Step 3: `Directory.CreateDirectory(Path.GetDirectoryName(target))` перед записом; у в'ю-моделях — `Path.GetFileName(Dto.FileName)` у `SaveFileDialog.FileName`.**
- [ ] **Step 4: Тести зелені.**
- [ ] **Step 5: Commit** — `"Аудит A3: пакетний експорт групових відомостей створює теку; у діалог іде ім'я файлу, не шлях"`

---

### Task B1: Забутий `Include(r => r.Weapons)` + тест-сторож

**Files:**
- Modify: `GenDoc/Services/Completeness/CompletenessService.cs` (`LoadAsync`, `GetCellAsync`), `GenDoc/Services/Documents/DocumentArchiveService.cs` (`RegenerateAsync`)
- Test: `GenDoc.Tests/Documents/DocumentHashServiceTests.cs` (створити)

- [ ] **Step 1: Тести, що падають.** (а) сторож: `ComputeSourceHash` для шаблону з міткою зброї дає РІЗНІ хеші для особи зі зброєю і без — доводить, що пропущений `Include` не може лишитись непоміченим; (б) інтеграційний: документ, згенерований для особи зі зброєю, у матриці НЕ позначений застарілим одразу після генерації.
- [ ] **Step 2: Запустити, побачити провал (б).**
- [ ] **Step 3: Додати `.Include(r => r.Weapons)` у три місця; у `RegenerateAsync` — ще й `.ThenInclude` на `OrgNode`.**
- [ ] **Step 4: Тести зелені.**
- [ ] **Step 5: Commit** — `"Аудит B1: Include(Weapons) у комплектності й перегенерації - документи більше не «вічно застарілі», перегенерація не стирає зброю"`

---

### Task B2: Злиття ручних значень по ключах

**Files:**
- Modify: `GenDoc/Services/Generation/ManualTagFormBuilder.cs` (`GetLastValuesAsync`, `GetLastSignerIdAsync`), `GenDoc/Services/Staff/StaffService.cs:160-181`
- Test: `GenDoc.Tests/Generation/ManualTagFormBuilderTests.cs`

- [ ] **Step 1: Тест, що падає.** У `AppSettings` три теги, у `UserSettings` один (інший ключ) → форма має підставити чотири. Очікуваний провал: підставляється один.
- [ ] **Step 2: Запустити, побачити провал.**
- [ ] **Step 3: Читання злютовує словники — глобальний як база, профільний перекриває по ключах. Той самий прийом для підписантів і в `StaffService`.**
- [ ] **Step 4: Тести зелені.**
- [ ] **Step 5: Commit** — `"Аудит B2: пер-профільні ручні значення злютовуються з глобальними по ключах, а не блобом"`

---

### Task B3: Позначка прогону бачить групові файли

**Files:**
- Modify: `GenDoc/Services/Generation/GenerationService.cs` (`ResolveRunStamp`)
- Test: `GenDoc.Tests/Generation/RunPackageTests.cs`

- [ ] **Step 1: Тест, що падає.** У теці лежить файл `Спільні/Залік/2026-08-28.xlsx`; `ResolveRunStamp` має повернути позначку З часом. Очікуваний провал: повертає голу дату.
- [ ] **Step 2: Запустити, побачити провал.**
- [ ] **Step 3: `alreadyUsed` враховує і теку з іменем дати, і файл, чиє ім'я без розширення дорівнює даті.**
- [ ] **Step 4: Тести зелені.**
- [ ] **Step 5: Commit** — `"Аудит B3: другий прогін за день більше не затирає групову відомість"`

---

### Task B4: «Перемістити» переносить зброю

**Files:**
- Modify: `GenDoc/Services/Import/ImportService.cs` (`MoveExistingRecipient`)
- Test: `GenDoc.Tests/Import/ImportMoveEndToEndTests.cs`

- [ ] **Step 1: Тест, що падає.** Наявна особа + рядок файлу зі зброєю + дія «Перемістити» → у особи має з'явитись зброя. Порожня колонка наявну зброю не стирає (другий тест).
- [ ] **Step 2: Запустити, побачити провал.**
- [ ] **Step 3: У гілці переміщення розібрати `fields.WeaponRaw` і замінити колекцію, якщо рядок непорожній.**
- [ ] **Step 4: Тести зелені.**
- [ ] **Step 5: Commit** — `"Аудит B4: «Перемістити» переносить зброю з файлу"`

---

### Task B5 + B6: Стабільна оцінка і провідні нулі

**Files:**
- Modify: `GenDoc/Services/Generation/XlsxGenerationService.cs` (`ComputeGradeRandom34`, `AssignTypedOrString`)
- Test: `GenDoc.Tests/Generation/XlsxRealTemplateTests.cs`

- [ ] **Step 1: Тести, що падають.** (а) `ComputeGradeRandom34` дає конкретні очікувані значення для трьох пар — закріплює детермінізм; (б) `AssignTypedOrString` для `"0501234567"` лишає текст, для `"42"` кладе число.
- [ ] **Step 2: Запустити. (а) впаде на будь-якому очікуваному значенні, бо seed плаває між процесами; (б) впаде на телефоні.**
- [ ] **Step 3: Детермінований хеш замість `HashCode.Combine`; числом кладемо лише те, що не має провідного нуля при довжині > 1.**
- [ ] **Step 4: Тести зелені.**
- [ ] **Step 5: Commit** — `"Аудит B5+B6: оцінка стабільна між запусками; провідний нуль у телефоні й ВОС не зникає"`

---

### Task B7: Відновлення з кошика повертає набір

**Files:**
- Modify: `GenDoc/Services/OrgTree/OrgTreeService.cs` (`RestoreAsync`)
- Test: `GenDoc.Tests/OrgTree/OrgTreeServiceTests.cs`

- [ ] **Step 1: Тест, що падає.** Видалити корінь набору → відновити → `IntakeService.GetByIdAsync` має повернути набір. Очікуваний провал: `null`.
- [ ] **Step 2: Запустити, побачити провал.**
- [ ] **Step 3: `RestoreAsync` знімає `DeletedAt` і з `Intake` тих вузлів, що повертаються — симетрично до `DeleteAsync`.**
- [ ] **Step 4: Тести зелені.**
- [ ] **Step 5: Commit** — `"Аудит B7: відновлення папки набору з кошика повертає й сам набір"`

---

### Task B8: Відсутня база не створюється мовчки

**Files:**
- Modify: `GenDoc/Services/IDatabaseUnlockService.cs`, `DatabaseUnlockService.cs`, `GenDoc/ViewModels/Login/LoginViewModel.cs`
- Test: `GenDoc.Tests/DatabaseUnlockServiceTests.cs` (створити)

- [ ] **Step 1: Тест, що падає.** `DatabaseExists` для неіснуючого шляху = `false`, для наявного = `true`.
- [ ] **Step 2: Запустити — властивості немає, компіляція падає.**
- [ ] **Step 3: Додати `DatabaseExists`; `LoginViewModel` за відсутності файла показує підтвердження з повним шляхом і йде далі лише за згодою.**
- [ ] **Step 4: Тести зелені.**
- [ ] **Step 5: Commit** — `"Аудит B8: відсутність файла бази більше не мовчазне створення порожньої"`

---

### Task C: Одне правило готовності (C1–C5 одним блоком)

Виправляти по одному не можна: п'ять місць рахують те саме число, і тест на будь-яке одне з них червонітиме, поки не зведені всі.

**Files:**
- Modify: `GenDoc/Services/Completeness/ICompletenessService.cs` (`Resolve`, `RecipientDocStatus`), `CompletenessService.cs` (`LoadAsync`, `GetRecipientStatusAsync`, `GetBadgeCountAsync`, `GenerateMissingForRecipientAsync`), `ViewModels/Completeness/MatrixCellViewModel.cs`, `MatrixRowViewModel.cs`, `ViewModels/Personnel/PersonCardViewModel.cs`
- Test: `GenDoc.Tests/Completeness/CompletenessConsistencyTests.cs` (створити)

**Interfaces:**
- Produces: `MatrixCellViewModel.IsSatisfied => State == MatrixCellState.Present`; `RecipientDocStatus` з полем `Requirement`.

- [ ] **Step 1: Тести, що падають.** (а) пакет з одного обов'язкового групового шаблону, особа поза складом → і матриця, і картка кажуть «неповний»; (б) 10 осіб, усі документи наявні, 4 застарілі → `IsFullyComplete == false` і відсоток зведення збігається з матрицею; (в) бейдж = «бракує персональних» + «застарілих обов'язкових», тобто рівно сума двох кнопок екрана; (г) документ із `IntakeId = null` для особи набору видно в матриці; (д) `Resolve("Придатний")` == `Resolve("придатний")`.
- [ ] **Step 2: Запустити, побачити п'ять провалів.**
- [ ] **Step 3: Реалізація за спекою C1–C5.**
- [ ] **Step 4: Тести зелені + повний прогін (наявні тести комплектності можуть змінити очікування — правити тест лише там, де змінилось саме визначення готовності).**
- [ ] **Step 5: Commit** — `"Аудит C: одне правило готовності на матрицю, картку, бейдж і зведення"`

---

### Task D: Логіка екранів (D1–D6)

**Files:**
- Modify: `ViewModels/Generation/GenerationViewModel.cs` (D1, D2), `Services/Generation/IGenerationService.cs` + `GenerationService.cs` (D2), `ViewModels/Archive/ArchiveViewModel.cs` (D3), `ViewModels/Personnel/PersonCardViewModel.cs` (D4), `ViewModels/Home/HomeViewModel.cs` (D5), `Views/Templates/TemplateBuilderView.xaml` (D6)
- Test: `GenDoc.Tests/Generation/GenerationViewModelTests.cs`, `GenDoc.Tests/Home/HomeViewModelTests.cs`, `GenDoc.Tests/StaticBindingScopeTests.cs` (створити)

- [ ] **Step 1: Тести, що падають.** (а) `GetRecipientCount(selection)` з фільтром звань дає число, що збігається з фактичним складом прогону; (б) `HomeViewModel` бере останній запуск лише активного набору; (в) сканер розмітки: жоден `{Binding X}` не вказує на `static`-властивість в'ю-моделі відповідного `DataType`.
- [ ] **Step 2: Запустити, побачити провали.**
- [ ] **Step 3: Реалізація D1–D6 за спекою.**
- [ ] **Step 4: Тести зелені.**
- [ ] **Step 5: Commit** — `"Аудит D: лічильник звань, IsBusy у finally, три фікси архіву, шапка картки, домашня картка, x:Static у конструкторі"`

---

### Task E: Мова документів (E1–E5)

**Files:**
- Modify: `GenDoc/Services/UkrainianGrammar.cs`, `GenDoc/Services/TemplateNaming.cs`
- Test: `GenDoc.Tests/UkrainianGrammarTests.cs` (переписати `Accusative_KoSuffixSurnames_AreInvariant`), `GenDoc.Tests/TemplateNamingTests.cs`

- [ ] **Step 1: Тести, що падають.** «Петренко»/Male → «Петренка»; «Петренко»/Female → «Петренко»; «майор»/Female → «майора»; «старший лейтенант»/Female → «старшого лейтенанта»; «капітан 1 рангу»/Male → «капітана 1 рангу»; «Ігор»/Male → «Ігоря»; `Clean("Шаблони обліку")` → «Шаблони обліку».
- [ ] **Step 2: Запустити. Наявний `Accusative_KoSuffixSurnames_AreInvariant` тепер суперечить новому тесту — переписати його: чоловічі з «-а», жіночі незмінні.**
- [ ] **Step 3: Реалізація E1–E5 за спекою.**
- [ ] **Step 4: Тести зелені.**
- [ ] **Step 5: Commit** — `"Аудит E: відмінювання прізвищ на -ко, звань для жінок, флотських звань; regex назви шаблона"`

---

### Task F: Конвенція міграцій (F1, F2)

**Files:**
- Modify: `GenDoc/Services/DatabaseSchemaInitializer.cs`
- Test: `GenDoc.Tests/DatabaseSchemaInitializerTests.cs`

- [ ] **Step 1: Тести, що падають.** (а) база з `SchemaVersions = 26`, але без `Recipients.Nationality` / `OrganizationSettings.HrOfficerFullName` / `Rooms.*` після `EnsureInitialized` має мати ці колонки; (б) таблиця `UserSettings` без унікального індексу після `EnsureUserSettingsTable` має його отримати.
- [ ] **Step 2: Запустити, побачити провали.**
- [ ] **Step 3: Три виклики `AddMissingColumns` у хвіст; `CREATE UNIQUE INDEX IF NOT EXISTS` за межі `if (!TableExists)`.**
- [ ] **Step 4: Тести зелені.**
- [ ] **Step 5: Commit** — `"Аудит F: колонки v2-v4 в ідемпотентний хвіст; унікальний індекс UserSettings поза створенням таблиці"`

---

## Після всіх задач

- [ ] Повний прогін `dotnet test -c Debug --nologo` — зелено, кількість тестів зросла.
- [ ] Оновити `docs/superpowers/specs/2026-08-28-code-audit-remediation-design.md`: позначити закриті блоки.
- [ ] Живий прогін тих трьох місць, де код однозначний, а очі надійніші: поповер оформлення в конструкторі, перемикач «Всі / Мої» в архіві, домашній розділ «Мій набір».
- [ ] Оновити пам'ять `gendoc-current-work`.
