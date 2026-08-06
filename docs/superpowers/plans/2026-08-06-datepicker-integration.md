# Дата-пікер: інтеграція у застосунок Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the two genuinely-missing pieces of the approved "square calendar date picker" mockup to the real GenDoc WPF app: a "Дата документа" field on the Generation screen (defaults to today, editable, feeds a reserved `{{дата}}` tag into every generated document), and a per-row date-format picker on the Templates screen's DOCX placeholder-mapping list (so a template author can choose `дд.мм.рррр` / `дд місяця рррр` / `рррр-мм-дд` for date-shaped recipient fields).

**Architecture:** Both features reuse patterns already established in the codebase — WPF's built-in `DatePicker` (already used everywhere else in the app, styled via `Themes/Theme.xaml`), CommunityToolkit.Mvvm `[ObservableProperty]`/`[RelayCommand]`, and the project's hand-rolled schema-versioning system (`DatabaseSchemaInitializer`, no EF Migrations). No new controls, no new packages.

**Tech Stack:** .NET 8 / WPF (`net8.0-windows`), CommunityToolkit.Mvvm 8.4.2, EF Core over SQLCipher SQLite, xUnit (`GenDoc.Tests`, `InternalsVisibleTo("GenDoc.Tests")` already set on `GenDoc/AssemblyInfo.cs`).

## Global Constraints

- Schema changes go through `GenDoc/Services/DatabaseSchemaInitializer.cs` only — no EF Migrations, no direct `ALTER TABLE` elsewhere. Follow the exact `AddMissingColumns`/`CurrentSchemaVersion` pattern already in that file.
- MVVM pattern: `[ObservableProperty]` fields (lowerCamelCase backing field, PascalCase generated property) + `[RelayCommand]` methods, mirroring `GenerationViewModel.cs`/`DocxMappingRowViewModel.cs`.
- Do not touch `ManualTagClassifier`/`ManualTagFormViewModel` beyond what's specified — they're a separate, unrelated tag system.
- Do not add the Excel-import "assign to intake" wizard step — explicitly deferred by the user; out of scope for this plan.
- Reuse `Themes/Theme.xaml`'s existing `DatePicker` styling — do not introduce a new custom control.
- Tests: xUnit `[Fact]`, pure-logic unit tests only (no DB-integration tests for this plan — the codebase's existing test suite doesn't cover simple CRUD passthrough either; only the risky/rebuild schema migrations get dedicated migration tests).

---

## Task 1: Schema v16 — `TemplateFieldMappings.DateFormat` column

**Files:**
- Modify: `GenDoc/Services/DatabaseSchemaInitializer.cs`

**Interfaces:**
- Produces: a nullable `TEXT` column `DateFormat` on table `TemplateFieldMappings`, added idempotently via the existing `AddMissingColumns` helper.

- [ ] **Step 1: Add the v16 column array**

In `GenDoc/Services/DatabaseSchemaInitializer.cs`, right after the existing `RecipientColumnsV15`/`AppSettingsColumnsV15` fields (lines 115-123), add:

```csharp
    private static readonly (string Name, string Type)[] TemplateFieldMappingColumnsV16 =
    {
        ("DateFormat", "TEXT")
    };
```

- [ ] **Step 2: Bump the schema version and add the v16 migration block**

Change line 9 from:
```csharp
    private const int CurrentSchemaVersion = 15;
```
to:
```csharp
    private const int CurrentSchemaVersion = 16;
```

Then, right after the `if (currentVersion < 15) { ... }` block (ends at line 334, just before the closing `}` of the `else` block at line 335), add a new block:

```csharp
            if (currentVersion < 16)
            {
                AddMissingColumns(db, "TemplateFieldMappings", TemplateFieldMappingColumnsV16);

                db.SchemaVersions.Add(new SchemaVersion
                {
                    Version = 16,
                    AppliedAt = DateTime.Now,
                    Description = "Шаблони: обраний формат дати для мапінгу дато-полів"
                });
            }
```

- [ ] **Step 3: Add the idempotent top-level call**

In the "Ідемпотентно... незалежно від SchemaVersion" section near the bottom of `EnsureInitialized` (right after the existing line `AddMissingColumns(db, "TemplateFieldMappings", TemplateFieldMappingColumnsV12);` at line 366), add:

```csharp
        AddMissingColumns(db, "TemplateFieldMappings", TemplateFieldMappingColumnsV16);
```

- [ ] **Step 4: Build to verify no compile errors**

Run: `dotnet build D:\GenDoc\GenDoc\GenDoc.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add GenDoc/Services/DatabaseSchemaInitializer.cs
git commit -m "feat: add schema v16 — TemplateFieldMappings.DateFormat column"
```

---

## Task 2: `TemplateFieldMapping.DateFormat` model property + `DateFormatCatalog`

**Files:**
- Modify: `GenDoc/Models/TemplateFieldMapping.cs`
- Create: `GenDoc/Services/Generation/DateFormatCatalog.cs`
- Test: `GenDoc.Tests/DateFormatCatalogTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `TemplateFieldMapping.DateFormat` (`string?`); `DateFormatCatalog.DdMmYyyy`/`Long`/`YyyyMmDd` (`const string`), `DateFormatCatalog.Options` (`IReadOnlyList<DateFormatOption>`), `DateFormatCatalog.Format(DateOnly date, string? formatKey, string fallbackKey) : string`. Task 3 (`GenerationService`) and Task 5 (`DocxMappingRowViewModel`) consume these directly.

- [ ] **Step 1: Write the failing test**

Create `GenDoc.Tests/DateFormatCatalogTests.cs`:

```csharp
using GenDoc.Services.Generation;

namespace GenDoc.Tests;

public class DateFormatCatalogTests
{
    [Fact]
    public void Format_DdMmYyyyKey_ReturnsShortDotted()
    {
        var date = new DateOnly(2026, 8, 6);

        var result = DateFormatCatalog.Format(date, DateFormatCatalog.DdMmYyyy, DateFormatCatalog.DdMmYyyy);

        Assert.Equal("06.08.2026", result);
    }

    [Fact]
    public void Format_YyyyMmDdKey_ReturnsIsoDashed()
    {
        var date = new DateOnly(2026, 8, 6);

        var result = DateFormatCatalog.Format(date, DateFormatCatalog.YyyyMmDd, DateFormatCatalog.DdMmYyyy);

        Assert.Equal("2026-08-06", result);
    }

    [Fact]
    public void Format_LongKey_ReturnsUkrainianGenitiveForm()
    {
        var date = new DateOnly(2026, 8, 6);

        var result = DateFormatCatalog.Format(date, DateFormatCatalog.Long, DateFormatCatalog.DdMmYyyy);

        Assert.Equal("6 серпня 2026 року", result);
    }

    [Fact]
    public void Format_NullFormatKey_FallsBackToFallbackKey()
    {
        var date = new DateOnly(2026, 8, 6);

        var result = DateFormatCatalog.Format(date, null, DateFormatCatalog.Long);

        Assert.Equal("6 серпня 2026 року", result);
    }

    [Fact]
    public void Options_ContainsAllThreeFormatsInOrder()
    {
        var keys = DateFormatCatalog.Options.Select(o => o.Key).ToList();

        Assert.Equal(new[] { DateFormatCatalog.DdMmYyyy, DateFormatCatalog.Long, DateFormatCatalog.YyyyMmDd }, keys);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test D:\GenDoc\GenDoc.Tests\GenDoc.Tests.csproj --filter DateFormatCatalogTests`
Expected: FAIL (compile error — `GenDoc.Services.Generation.DateFormatCatalog` does not exist).

- [ ] **Step 3: Add `DateFormat` to the model**

In `GenDoc/Models/TemplateFieldMapping.cs`, add a property after `FieldName` (line 14):

```csharp
        public string? FieldName { get; set; }
        public string? DateFormat { get; set; }
```

- [ ] **Step 4: Implement `DateFormatCatalog`**

Create `GenDoc/Services/Generation/DateFormatCatalog.cs`:

```csharp
using GenDoc.Services;

namespace GenDoc.Services.Generation
{
    public record DateFormatOption(string Key, string Display);

    // Три формати дати, які можна обрати для дато-полів у мапінгу плейсхолдерів
    // (Views/Templates). Ключ зберігається як TemplateFieldMapping.DateFormat.
    public static class DateFormatCatalog
    {
        public const string DdMmYyyy = "dd.MM.yyyy";
        public const string Long = "long";
        public const string YyyyMmDd = "yyyy-MM-dd";

        public static readonly IReadOnlyList<DateFormatOption> Options = new List<DateFormatOption>
        {
            new(DdMmYyyy, "дд.мм.рррр"),
            new(Long, "дд місяця рррр"),
            new(YyyyMmDd, "рррр-мм-дд"),
        };

        public static string Format(DateOnly date, string? formatKey, string fallbackKey)
        {
            var key = string.IsNullOrEmpty(formatKey) ? fallbackKey : formatKey;
            return key switch
            {
                Long => UkrainianDate.Long(date),
                YyyyMmDd => date.ToString(YyyyMmDd),
                _ => date.ToString(DdMmYyyy)
            };
        }
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test D:\GenDoc\GenDoc.Tests\GenDoc.Tests.csproj --filter DateFormatCatalogTests`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add GenDoc/Models/TemplateFieldMapping.cs GenDoc/Services/Generation/DateFormatCatalog.cs GenDoc.Tests/DateFormatCatalogTests.cs
git commit -m "feat: add DateFormatCatalog and TemplateFieldMapping.DateFormat"
```

---

## Task 3: `GenerationService` uses per-mapping date format

**Files:**
- Modify: `GenDoc/Services/Generation/GenerationService.cs:772-829` (`BuildValues`, `GetRecipientFieldValue`)
- Test: `GenDoc.Tests/GenerationServiceBuildValuesTests.cs`

**Interfaces:**
- Consumes: `DateFormatCatalog.Format` (Task 2), `TemplateFieldMapping.DateFormat` (Task 2).
- Produces: `GenerationService.BuildValues(List<TemplateFieldMapping>, Recipient, OrganizationSettings?, Dictionary<string,string>) : Dictionary<string,string>` keeps its exact existing signature (internal static) — only its internal date-formatting behavior changes. No caller changes needed elsewhere.

- [ ] **Step 1: Write the failing test**

Create `GenDoc.Tests/GenerationServiceBuildValuesTests.cs`:

```csharp
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Generation;

namespace GenDoc.Tests;

public class GenerationServiceBuildValuesTests
{
    [Fact]
    public void BuildValues_DateOfBirthWithNoDateFormat_UsesDefaultDdMmYyyy()
    {
        var recipient = new Recipient
        {
            LastName = "Тест", FirstName = "Тест",
            DateOfBirth = new DateOnly(1990, 3, 15)
        };
        var mappings = new List<TemplateFieldMapping>
        {
            new() { PlaceholderTag = "{{дата_народження}}", SourceType = MappingSourceType.Recipient, FieldName = "DateOfBirth", DateFormat = null }
        };

        var values = GenerationService.BuildValues(mappings, recipient, org: null, manualValues: new Dictionary<string, string>());

        Assert.Equal("15.03.1990", values["{{дата_народження}}"]);
    }

    [Fact]
    public void BuildValues_DateOfBirthWithExplicitLongFormat_UsesUkrainianLongForm()
    {
        var recipient = new Recipient
        {
            LastName = "Тест", FirstName = "Тест",
            DateOfBirth = new DateOnly(1990, 3, 15)
        };
        var mappings = new List<TemplateFieldMapping>
        {
            new() { PlaceholderTag = "{{дата_народження}}", SourceType = MappingSourceType.Recipient, FieldName = "DateOfBirth", DateFormat = DateFormatCatalog.Long }
        };

        var values = GenerationService.BuildValues(mappings, recipient, org: null, manualValues: new Dictionary<string, string>());

        Assert.Equal("15 березня 1990 року", values["{{дата_народження}}"]);
    }

    [Fact]
    public void BuildValues_TravelCertificateDateWithNoDateFormat_DefaultsToLongForm()
    {
        var recipient = new Recipient
        {
            LastName = "Тест", FirstName = "Тест",
            TravelCertificateDate = new DateOnly(2026, 8, 6)
        };
        var mappings = new List<TemplateFieldMapping>
        {
            new() { PlaceholderTag = "{{дата_посвідчення}}", SourceType = MappingSourceType.Recipient, FieldName = "TravelCertificateDate", DateFormat = null }
        };

        var values = GenerationService.BuildValues(mappings, recipient, org: null, manualValues: new Dictionary<string, string>());

        Assert.Equal("6 серпня 2026 року", values["{{дата_посвідчення}}"]);
    }

    [Fact]
    public void BuildValues_TravelCertificateDateWithExplicitIsoFormat_UsesIsoForm()
    {
        var recipient = new Recipient
        {
            LastName = "Тест", FirstName = "Тест",
            TravelCertificateDate = new DateOnly(2026, 8, 6)
        };
        var mappings = new List<TemplateFieldMapping>
        {
            new() { PlaceholderTag = "{{дата_посвідчення}}", SourceType = MappingSourceType.Recipient, FieldName = "TravelCertificateDate", DateFormat = DateFormatCatalog.YyyyMmDd }
        };

        var values = GenerationService.BuildValues(mappings, recipient, org: null, manualValues: new Dictionary<string, string>());

        Assert.Equal("2026-08-06", values["{{дата_посвідчення}}"]);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test D:\GenDoc\GenDoc.Tests\GenDoc.Tests.csproj --filter GenerationServiceBuildValuesTests`
Expected: FAIL — `BuildValues_DateOfBirthWithExplicitLongFormat_UsesUkrainianLongForm` and the ISO-format test fail (current code ignores `DateFormat` and always renders `DateOfBirth` as `dd.MM.yyyy`); the two "no format" tests pass already (current hardcoded defaults happen to match).

- [ ] **Step 3: Update `GetRecipientFieldValue` and its call site**

In `GenDoc/Services/Generation/GenerationService.cs`, change the method signature at line 784 from:
```csharp
        private static string GetRecipientFieldValue(Recipient r, string? fieldName) => fieldName switch
```
to:
```csharp
        private static string GetRecipientFieldValue(Recipient r, string? fieldName, string? dateFormat) => fieldName switch
```

Then update the three date-shaped cases (lines 794, 797, 809) from:
```csharp
            "DateOfBirth" => r.DateOfBirth?.ToString("dd.MM.yyyy") ?? string.Empty,
```
to:
```csharp
            "DateOfBirth" => r.DateOfBirth is { } dob ? DateFormatCatalog.Format(dob, dateFormat, DateFormatCatalog.DdMmYyyy) : string.Empty,
```
and:
```csharp
            "CourseArrivalDate" => r.CourseArrivalDate?.ToString("dd.MM.yyyy") ?? string.Empty,
```
to:
```csharp
            "CourseArrivalDate" => r.CourseArrivalDate is { } cad ? DateFormatCatalog.Format(cad, dateFormat, DateFormatCatalog.DdMmYyyy) : string.Empty,
```
and:
```csharp
            "TravelCertificateDate" => r.TravelCertificateDate is { } tcd ? UkrainianDate.Long(tcd) : string.Empty,
```
to:
```csharp
            "TravelCertificateDate" => r.TravelCertificateDate is { } tcd ? DateFormatCatalog.Format(tcd, dateFormat, DateFormatCatalog.Long) : string.Empty,
```

Now fix the two call sites. `BuildValues` (line 774):
```csharp
                    MappingSourceType.Recipient => GetRecipientFieldValue(recipient, mapping.FieldName),
```
becomes:
```csharp
                    MappingSourceType.Recipient => GetRecipientFieldValue(recipient, mapping.FieldName, mapping.DateFormat),
```

`ResolveHashField` (line 630) uses a *different* mapping type (`ExportTemplateColumnMapping`, not `TemplateFieldMapping` — out of scope for this plan, xlsx export path). That type has no `DateFormat` property, so pass `dateFormat: null` to preserve its exact current hardcoded-default behavior:
```csharp
            MappingSourceType.Recipient => GetRecipientFieldValue(r, mapping.FieldKey),
```
becomes:
```csharp
            MappingSourceType.Recipient => GetRecipientFieldValue(r, mapping.FieldKey, dateFormat: null),
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test D:\GenDoc\GenDoc.Tests\GenDoc.Tests.csproj --filter GenerationServiceBuildValuesTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Run the full test suite to check nothing else broke**

Run: `dotnet test D:\GenDoc\GenDoc.Tests\GenDoc.Tests.csproj`
Expected: all tests pass (in particular `DocumentGenerationServiceTests` and `FoodAllowanceReportTests`, which also exercise `GenerationService`/`GetRecipientFieldValue` indirectly).

- [ ] **Step 6: Commit**

```bash
git add GenDoc/Services/Generation/GenerationService.cs GenDoc.Tests/GenerationServiceBuildValuesTests.cs
git commit -m "feat: honor per-mapping DateFormat when rendering recipient date fields"
```

---

## Task 4: `ITemplateService`/`TemplateService` carry `DateFormat` through mapping load/save

**Files:**
- Modify: `GenDoc/Services/Templates/ITemplateService.cs:13-14`
- Modify: `GenDoc/Services/Templates/TemplateService.cs:112-144`

**Interfaces:**
- Consumes: `TemplateFieldMapping.DateFormat` (Task 2).
- Produces: `ITemplateService.GetMappings(int templateId) : List<(int Id, string PlaceholderTag, MappingSourceType SourceType, string? FieldName, string? DateFormat)>` and `ITemplateService.SaveMappings(int templateId, List<(int Id, MappingSourceType SourceType, string? FieldName, string? DateFormat)> mappings) : void` — both tuples grow a 5th element `DateFormat`. Task 5 (`TemplatesViewModel`) consumes these new signatures directly.

- [ ] **Step 1: Update the interface**

In `GenDoc/Services/Templates/ITemplateService.cs`, change:
```csharp
        List<(int Id, string PlaceholderTag, MappingSourceType SourceType, string? FieldName)> GetMappings(int templateId);
        void SaveMappings(int templateId, List<(int Id, MappingSourceType SourceType, string? FieldName)> mappings);
```
to:
```csharp
        List<(int Id, string PlaceholderTag, MappingSourceType SourceType, string? FieldName, string? DateFormat)> GetMappings(int templateId);
        void SaveMappings(int templateId, List<(int Id, MappingSourceType SourceType, string? FieldName, string? DateFormat)> mappings);
```

- [ ] **Step 2: Update the implementation**

In `GenDoc/Services/Templates/TemplateService.cs`, change `GetMappings` (lines 112-122) from:
```csharp
        public List<(int Id, string PlaceholderTag, MappingSourceType SourceType, string? FieldName)> GetMappings(int templateId)
        {
            using var db = _dbFactory.CreateDbContext();
            return db.TemplateFieldMappings
                .Where(m => m.TemplateId == templateId)
                .OrderBy(m => m.PlaceholderTag)
                .Select(m => new { m.Id, m.PlaceholderTag, m.SourceType, m.FieldName })
                .AsEnumerable()
                .Select(m => (m.Id, m.PlaceholderTag, m.SourceType, m.FieldName))
                .ToList();
        }
```
to:
```csharp
        public List<(int Id, string PlaceholderTag, MappingSourceType SourceType, string? FieldName, string? DateFormat)> GetMappings(int templateId)
        {
            using var db = _dbFactory.CreateDbContext();
            return db.TemplateFieldMappings
                .Where(m => m.TemplateId == templateId)
                .OrderBy(m => m.PlaceholderTag)
                .Select(m => new { m.Id, m.PlaceholderTag, m.SourceType, m.FieldName, m.DateFormat })
                .AsEnumerable()
                .Select(m => (m.Id, m.PlaceholderTag, m.SourceType, m.FieldName, m.DateFormat))
                .ToList();
        }
```

Change `SaveMappings` (lines 124-144) from:
```csharp
        public void SaveMappings(int templateId, List<(int Id, MappingSourceType SourceType, string? FieldName)> mappings)
        {
            using var db = _dbFactory.CreateDbContext();
            var existing = db.TemplateFieldMappings.Where(m => m.TemplateId == templateId).ToList();

            var oldSnapshot = string.Join(", ", existing.OrderBy(m => m.PlaceholderTag).Select(m => $"{m.PlaceholderTag}:{m.SourceType}/{m.FieldName}"));

            foreach (var (id, sourceType, fieldName) in mappings)
            {
                var mapping = existing.FirstOrDefault(m => m.Id == id);
                if (mapping is null) continue;

                mapping.SourceType = sourceType;
                mapping.FieldName = sourceType == MappingSourceType.Manual ? null : fieldName;
            }

            var newSnapshot = string.Join(", ", existing.OrderBy(m => m.PlaceholderTag).Select(m => $"{m.PlaceholderTag}:{m.SourceType}/{m.FieldName}"));

            _auditLogService.LogUpdate(db, "Template", templateId, oldSnapshot, newSnapshot, "Оновлено мапінг міток");
            db.SaveChanges();
        }
```
to:
```csharp
        public void SaveMappings(int templateId, List<(int Id, MappingSourceType SourceType, string? FieldName, string? DateFormat)> mappings)
        {
            using var db = _dbFactory.CreateDbContext();
            var existing = db.TemplateFieldMappings.Where(m => m.TemplateId == templateId).ToList();

            var oldSnapshot = string.Join(", ", existing.OrderBy(m => m.PlaceholderTag).Select(m => $"{m.PlaceholderTag}:{m.SourceType}/{m.FieldName}"));

            foreach (var (id, sourceType, fieldName, dateFormat) in mappings)
            {
                var mapping = existing.FirstOrDefault(m => m.Id == id);
                if (mapping is null) continue;

                mapping.SourceType = sourceType;
                mapping.FieldName = sourceType == MappingSourceType.Manual ? null : fieldName;
                mapping.DateFormat = sourceType == MappingSourceType.Manual ? null : dateFormat;
            }

            var newSnapshot = string.Join(", ", existing.OrderBy(m => m.PlaceholderTag).Select(m => $"{m.PlaceholderTag}:{m.SourceType}/{m.FieldName}"));

            _auditLogService.LogUpdate(db, "Template", templateId, oldSnapshot, newSnapshot, "Оновлено мапінг міток");
            db.SaveChanges();
        }
```

- [ ] **Step 3: Build to verify no compile errors**

Run: `dotnet build D:\GenDoc\GenDoc\GenDoc.csproj`
Expected: fails — `TemplatesViewModel.ToggleDocxMapping`/`SaveDocxMapping` (Task 5) still call the old 4-tuple signatures. This is expected; Task 5 fixes it. Confirm the *only* errors are in `TemplatesViewModel.cs`.

- [ ] **Step 4: Commit**

```bash
git add GenDoc/Services/Templates/ITemplateService.cs GenDoc/Services/Templates/TemplateService.cs
git commit -m "feat: carry DateFormat through ITemplateService mapping load/save"
```

---

## Task 5: `DocxMappingRowViewModel` date-format picker + `TemplatesViewModel` wiring

**Files:**
- Modify: `GenDoc/ViewModels/Templates/DocxMappingRowViewModel.cs`
- Modify: `GenDoc/ViewModels/Templates/TemplatesViewModel.cs` (`ToggleDocxMapping` and `SaveDocxMapping`)
- Test: `GenDoc.Tests/DocxMappingRowViewModelTests.cs`

**Interfaces:**
- Consumes: `DateFormatCatalog.Options` (Task 2), `ITemplateService.GetMappings`/`SaveMappings` new 5-tuple signatures (Task 4).
- Produces: `DocxMappingRowViewModel.IsDateField : bool`, `DocxMappingRowViewModel.DateFormatOptions : IReadOnlyList<DateFormatOption>`, `DocxMappingRowViewModel.SelectedDateFormat : string?` (`[ObservableProperty]`). Task 6 (XAML) binds directly to these three members.

- [ ] **Step 1: Write the failing test**

Create `GenDoc.Tests/DocxMappingRowViewModelTests.cs`:

```csharp
using GenDoc.Models.Enums;
using GenDoc.ViewModels.Templates;

namespace GenDoc.Tests;

public class DocxMappingRowViewModelTests
{
    [Fact]
    public void IsDateField_KnownDateFieldName_ReturnsTrue()
    {
        var row = new DocxMappingRowViewModel(1, "{{дата_народження}}", MappingSourceType.Recipient, "DateOfBirth", dateFormat: null);

        Assert.True(row.IsDateField);
    }

    [Fact]
    public void IsDateField_NonDateFieldName_ReturnsFalse()
    {
        var row = new DocxMappingRowViewModel(1, "{{прізвище}}", MappingSourceType.Recipient, "LastName", dateFormat: null);

        Assert.False(row.IsDateField);
    }

    [Fact]
    public void IsDateField_UpdatesWhenSelectedFieldNameChangesToADateField()
    {
        var row = new DocxMappingRowViewModel(1, "{{тег}}", MappingSourceType.Recipient, "LastName", dateFormat: null);
        Assert.False(row.IsDateField);

        row.SelectedFieldName = "TravelCertificateDate";

        Assert.True(row.IsDateField);
    }

    [Fact]
    public void Constructor_PreservesSuppliedDateFormat()
    {
        var row = new DocxMappingRowViewModel(1, "{{дата_народження}}", MappingSourceType.Recipient, "DateOfBirth", dateFormat: "yyyy-MM-dd");

        Assert.Equal("yyyy-MM-dd", row.SelectedDateFormat);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test D:\GenDoc\GenDoc.Tests\GenDoc.Tests.csproj --filter DocxMappingRowViewModelTests`
Expected: FAIL (compile error — constructor doesn't accept a 5th `dateFormat` argument; `IsDateField`/`SelectedDateFormat` don't exist).

- [ ] **Step 3: Update `DocxMappingRowViewModel`**

Replace the full contents of `GenDoc/ViewModels/Templates/DocxMappingRowViewModel.cs` with:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using GenDoc.Models.Enums;
using GenDoc.Services.Generation;
using GenDoc.Services.Templates;

namespace GenDoc.ViewModels.Templates;

public record MappingSourceOption(MappingSourceType Value, string Display);

public partial class DocxMappingRowViewModel : ObservableObject
{
    private static readonly IReadOnlyList<MappingSourceOption> SharedSourceOptions = new List<MappingSourceOption>
    {
        new(MappingSourceType.Recipient, "Про людину"),
        new(MappingSourceType.Organization, "Про частину"),
        new(MappingSourceType.Manual, "Вручну при генерації")
    };

    // Поля-дати з TemplateFieldCatalog.RecipientFields — узгоджено з
    // GenerationService.GetRecipientFieldValue, де саме ці три ключі дато-форматуються.
    private static readonly HashSet<string> DateFieldNames = new(StringComparer.Ordinal)
    {
        "DateOfBirth", "CourseArrivalDate", "TravelCertificateDate"
    };

    public DocxMappingRowViewModel(int id, string placeholderTag, MappingSourceType sourceType, string? fieldName, string? dateFormat)
    {
        Id = id;
        PlaceholderTag = placeholderTag;
        this.sourceType = sourceType;
        selectedFieldName = fieldName;
        selectedDateFormat = dateFormat;
    }

    public int Id { get; }
    public string PlaceholderTag { get; }
    public IReadOnlyList<MappingSourceOption> SourceOptions => SharedSourceOptions;
    public IReadOnlyList<DateFormatOption> DateFormatOptions => DateFormatCatalog.Options;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFieldEnabled))]
    [NotifyPropertyChangedFor(nameof(FieldOptions))]
    private MappingSourceType sourceType;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDateField))]
    private string? selectedFieldName;

    [ObservableProperty]
    private string? selectedDateFormat;

    public bool IsFieldEnabled => SourceType != MappingSourceType.Manual;

    public bool IsDateField => SelectedFieldName is not null && DateFieldNames.Contains(SelectedFieldName);

    public IReadOnlyList<TemplateFieldOption> FieldOptions => SourceType switch
    {
        MappingSourceType.Recipient => TemplateFieldCatalog.RecipientFields,
        MappingSourceType.Organization => TemplateFieldCatalog.OrganizationFields,
        _ => Array.Empty<TemplateFieldOption>()
    };

    partial void OnSourceTypeChanged(MappingSourceType value)
    {
        if (value == MappingSourceType.Manual) SelectedFieldName = null;
    }
}
```

- [ ] **Step 4: Fix the two call sites in `TemplatesViewModel`**

In `GenDoc/ViewModels/Templates/TemplatesViewModel.cs`, change `ToggleDocxMapping`'s mapping projection from:
```csharp
            var mappings = _templateService.GetMappings(item.Id)
                .Select(m => new DocxMappingRowViewModel(m.Id, m.PlaceholderTag, m.SourceType, m.FieldName));
```
to:
```csharp
            var mappings = _templateService.GetMappings(item.Id)
                .Select(m => new DocxMappingRowViewModel(m.Id, m.PlaceholderTag, m.SourceType, m.FieldName, m.DateFormat));
```

Change `SaveDocxMapping`'s tuple projection from:
```csharp
        var mappings = item.Mappings.Select(m => (m.Id, m.SourceType, m.SelectedFieldName)).ToList();
        _templateService.SaveMappings(item.Id, mappings);
```
to:
```csharp
        var mappings = item.Mappings.Select(m => (m.Id, m.SourceType, m.SelectedFieldName, m.SelectedDateFormat)).ToList();
        _templateService.SaveMappings(item.Id, mappings);
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test D:\GenDoc\GenDoc.Tests\GenDoc.Tests.csproj --filter DocxMappingRowViewModelTests`
Expected: PASS (4 tests).

- [ ] **Step 6: Build the whole solution to confirm Task 4's deferred errors are now gone**

Run: `dotnet build D:\GenDoc\GenDoc\GenDoc.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add GenDoc/ViewModels/Templates/DocxMappingRowViewModel.cs GenDoc/ViewModels/Templates/TemplatesViewModel.cs GenDoc.Tests/DocxMappingRowViewModelTests.cs
git commit -m "feat: add per-row date-format picker to DOCX placeholder mapping"
```

---

## Task 6: Templates screen XAML — date-format column

**Files:**
- Modify: `GenDoc/Views/Templates/TemplatesView.xaml:100-150`

**Interfaces:**
- Consumes: `DocxMappingRowViewModel.IsDateField`/`DateFormatOptions`/`SelectedDateFormat` (Task 5), the app-wide `BoolToVisibility` converter (`GenDoc/App.xaml:12`).

- [ ] **Step 1: Add a 4th header column**

In `GenDoc/Views/Templates/TemplatesView.xaml`, change the header `Grid` (lines 100-109) from:
```xml
                                                    <Grid Margin="0,0,0,8">
                                                        <Grid.ColumnDefinitions>
                                                            <ColumnDefinition Width="1*"/>
                                                            <ColumnDefinition Width="1.1*"/>
                                                            <ColumnDefinition Width="1.4*"/>
                                                        </Grid.ColumnDefinitions>
                                                        <TextBlock Grid.Column="0" Text="Мітка" Style="{StaticResource TemplatesHeaderTextStyle}"/>
                                                        <TextBlock Grid.Column="1" Text="Джерело" Style="{StaticResource TemplatesHeaderTextStyle}"/>
                                                        <TextBlock Grid.Column="2" Text="Поле" Style="{StaticResource TemplatesHeaderTextStyle}"/>
                                                    </Grid>
```
to:
```xml
                                                    <Grid Margin="0,0,0,8">
                                                        <Grid.ColumnDefinitions>
                                                            <ColumnDefinition Width="1*"/>
                                                            <ColumnDefinition Width="1.1*"/>
                                                            <ColumnDefinition Width="1.4*"/>
                                                            <ColumnDefinition Width="150"/>
                                                        </Grid.ColumnDefinitions>
                                                        <TextBlock Grid.Column="0" Text="Мітка" Style="{StaticResource TemplatesHeaderTextStyle}"/>
                                                        <TextBlock Grid.Column="1" Text="Джерело" Style="{StaticResource TemplatesHeaderTextStyle}"/>
                                                        <TextBlock Grid.Column="2" Text="Поле" Style="{StaticResource TemplatesHeaderTextStyle}"/>
                                                        <TextBlock Grid.Column="3" Text="Формат дати" Style="{StaticResource TemplatesHeaderTextStyle}"/>
                                                    </Grid>
```

- [ ] **Step 2: Add the format ComboBox to the mapping-row template**

Change the row `Grid` (lines 114-147) from:
```xml
                                                                <Grid Margin="0,0,0,8">
                                                                    <Grid.ColumnDefinitions>
                                                                        <ColumnDefinition Width="1*"/>
                                                                        <ColumnDefinition Width="1.1*"/>
                                                                        <ColumnDefinition Width="1.4*"/>
                                                                    </Grid.ColumnDefinitions>

                                                                    <TextBlock Grid.Column="0" Text="{Binding PlaceholderTag}"
                                                                               FontFamily="Consolas" Foreground="{StaticResource AccentBrush}"
                                                                               VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/>

                                                                    <ComboBox Grid.Column="1" ItemsSource="{Binding SourceOptions}"
                                                                              SelectedValuePath="Value"
                                                                              SelectedValue="{Binding SourceType, Mode=TwoWay}"
                                                                              Width="180" HorizontalAlignment="Left" Margin="0,0,10,0">
                                                                        <ComboBox.ItemTemplate>
                                                                            <DataTemplate>
                                                                                <TextBlock Text="{Binding Display}"/>
                                                                            </DataTemplate>
                                                                        </ComboBox.ItemTemplate>
                                                                    </ComboBox>

                                                                    <ComboBox Grid.Column="2" ItemsSource="{Binding FieldOptions}"
                                                                              SelectedValuePath="FieldName"
                                                                              SelectedValue="{Binding SelectedFieldName, Mode=TwoWay}"
                                                                              IsEnabled="{Binding IsFieldEnabled}"
                                                                              Width="260" HorizontalAlignment="Left">
                                                                        <ComboBox.ItemTemplate>
                                                                            <DataTemplate>
                                                                                <TextBlock Text="{Binding DisplayName}"/>
                                                                            </DataTemplate>
                                                                        </ComboBox.ItemTemplate>
                                                                    </ComboBox>
                                                                </Grid>
```
to:
```xml
                                                                <Grid Margin="0,0,0,8">
                                                                    <Grid.ColumnDefinitions>
                                                                        <ColumnDefinition Width="1*"/>
                                                                        <ColumnDefinition Width="1.1*"/>
                                                                        <ColumnDefinition Width="1.4*"/>
                                                                        <ColumnDefinition Width="150"/>
                                                                    </Grid.ColumnDefinitions>

                                                                    <TextBlock Grid.Column="0" Text="{Binding PlaceholderTag}"
                                                                               FontFamily="Consolas" Foreground="{StaticResource AccentBrush}"
                                                                               VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/>

                                                                    <ComboBox Grid.Column="1" ItemsSource="{Binding SourceOptions}"
                                                                              SelectedValuePath="Value"
                                                                              SelectedValue="{Binding SourceType, Mode=TwoWay}"
                                                                              Width="180" HorizontalAlignment="Left" Margin="0,0,10,0">
                                                                        <ComboBox.ItemTemplate>
                                                                            <DataTemplate>
                                                                                <TextBlock Text="{Binding Display}"/>
                                                                            </DataTemplate>
                                                                        </ComboBox.ItemTemplate>
                                                                    </ComboBox>

                                                                    <ComboBox Grid.Column="2" ItemsSource="{Binding FieldOptions}"
                                                                              SelectedValuePath="FieldName"
                                                                              SelectedValue="{Binding SelectedFieldName, Mode=TwoWay}"
                                                                              IsEnabled="{Binding IsFieldEnabled}"
                                                                              Width="260" HorizontalAlignment="Left">
                                                                        <ComboBox.ItemTemplate>
                                                                            <DataTemplate>
                                                                                <TextBlock Text="{Binding DisplayName}"/>
                                                                            </DataTemplate>
                                                                        </ComboBox.ItemTemplate>
                                                                    </ComboBox>

                                                                    <ComboBox Grid.Column="3" ItemsSource="{Binding DateFormatOptions}"
                                                                              SelectedValuePath="Key"
                                                                              SelectedValue="{Binding SelectedDateFormat, Mode=TwoWay}"
                                                                              Visibility="{Binding IsDateField, Converter={StaticResource BoolToVisibility}}"
                                                                              Width="140" HorizontalAlignment="Left">
                                                                        <ComboBox.ItemTemplate>
                                                                            <DataTemplate>
                                                                                <TextBlock Text="{Binding Display}"/>
                                                                            </DataTemplate>
                                                                        </ComboBox.ItemTemplate>
                                                                    </ComboBox>
                                                                </Grid>
```

- [ ] **Step 3: Build and manually verify**

Run: `dotnet build D:\GenDoc\GenDoc\GenDoc.csproj`
Expected: Build succeeded, 0 errors (no XAML compile errors).

Manual check (WPF XAML has no automated UI test coverage in this project — matches existing convention): launch the app, open Шаблони → expand mapping for a DOCX template that has a `{{дата_народження}}`/`{{дата_посвідчення}}`-style tag mapped to `Recipient`/`DateOfBirth` (or `TravelCertificateDate`), confirm the "Формат дати" ComboBox appears only on that row (not on non-date rows), pick a format, click "Зберегти мапінг", reopen the mapping and confirm the choice persisted.

- [ ] **Step 4: Commit**

```bash
git add GenDoc/Views/Templates/TemplatesView.xaml
git commit -m "feat: show date-format picker column on DOCX mapping rows"
```

---

## Task 7: `GenerationViewModel.DocumentDate` + merge into generation

**Files:**
- Modify: `GenDoc/ViewModels/Generation/GenerationViewModel.cs`
- Test: `GenDoc.Tests/GenerationViewModelDocumentDateTests.cs`

**Interfaces:**
- Produces: `GenerationViewModel.DocumentDate : DateTime?` (`[ObservableProperty]`, defaults to `DateTime.Today`), `GenerationViewModel.ApplyDocumentDate(Dictionary<string,string> manualValues, DateTime? documentDate) : void` (`internal static`, testable without constructing the full ViewModel). Task 8 (XAML) binds a `DatePicker` to `DocumentDate`.

- [ ] **Step 1: Write the failing test**

Create `GenDoc.Tests/GenerationViewModelDocumentDateTests.cs`:

```csharp
using GenDoc.ViewModels.Generation;

namespace GenDoc.Tests;

public class GenerationViewModelDocumentDateTests
{
    [Fact]
    public void ApplyDocumentDate_ExplicitDate_SetsReservedDateTagFormatted()
    {
        var manualValues = new Dictionary<string, string>();

        GenerationViewModel.ApplyDocumentDate(manualValues, new DateTime(2026, 8, 6));

        Assert.Equal("06.08.2026", manualValues["дата"]);
    }

    [Fact]
    public void ApplyDocumentDate_NullDate_FallsBackToToday()
    {
        var manualValues = new Dictionary<string, string>();

        GenerationViewModel.ApplyDocumentDate(manualValues, null);

        Assert.Equal(DateTime.Today.ToString("dd.MM.yyyy"), manualValues["дата"]);
    }

    [Fact]
    public void ApplyDocumentDate_OverwritesExistingManualDateEntry()
    {
        var manualValues = new Dictionary<string, string> { ["дата"] = "01.01.2000" };

        GenerationViewModel.ApplyDocumentDate(manualValues, new DateTime(2026, 8, 6));

        Assert.Equal("06.08.2026", manualValues["дата"]);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test D:\GenDoc\GenDoc.Tests\GenDoc.Tests.csproj --filter GenerationViewModelDocumentDateTests`
Expected: FAIL (compile error — `GenerationViewModel.ApplyDocumentDate` does not exist).

- [ ] **Step 3: Add the `DocumentDate` property**

In `GenDoc/ViewModels/Generation/GenerationViewModel.cs`, right after the `regenerateExisting` property (lines 100-101):
```csharp
    [ObservableProperty]
    private bool regenerateExisting;
```
add:
```csharp
    [ObservableProperty]
    private DateTime? documentDate = DateTime.Today;
```

- [ ] **Step 4: Add `ApplyDocumentDate` and call it from `GenerateAllAsync`**

In the same file, add this method right after `GenerateAllAsync` (after the closing brace of the method, before the closing brace of the class — i.e. after line 464):
```csharp

    // Тег "дата" зарезервований під це поле — підставляється в кожен документ
    // пакета незалежно від шаблону; якщо шаблон явно мапить {{дата}} вручну,
    // це поле є єдиним джерелом значення (перекриває будь-який попередній запис).
    internal static void ApplyDocumentDate(Dictionary<string, string> manualValues, DateTime? documentDate)
    {
        manualValues["дата"] = (documentDate ?? DateTime.Today).ToString("dd.MM.yyyy");
    }
```

Then, in `GenerateAllAsync`, change:
```csharp
        var manualValues = ManualTagForm?.GetValues() ?? new Dictionary<string, string>();
```
to:
```csharp
        var manualValues = ManualTagForm?.GetValues() ?? new Dictionary<string, string>();
        ApplyDocumentDate(manualValues, DocumentDate);
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test D:\GenDoc\GenDoc.Tests\GenDoc.Tests.csproj --filter GenerationViewModelDocumentDateTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Build to confirm no regressions**

Run: `dotnet build D:\GenDoc\GenDoc\GenDoc.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add GenDoc/ViewModels/Generation/GenerationViewModel.cs GenDoc.Tests/GenerationViewModelDocumentDateTests.cs
git commit -m "feat: add DocumentDate field feeding a reserved {{дата}} tag into generation"
```

---

## Task 8: Generation screen XAML — "Дата документа" section

**Files:**
- Modify: `GenDoc/Views/Generation/GenerationView.xaml:258-260`

**Interfaces:**
- Consumes: `GenerationViewModel.DocumentDate` (Task 7).

- [ ] **Step 1: Insert the new section**

In `GenDoc/Views/Generation/GenerationView.xaml`, between the end of the "ОСОБОВИЙ СКЛАД" `Border` (line 258, `</Border>`) and the start of the "ЗНАЧЕННЯ ДЛЯ ЦІЄЇ ГЕНЕРАЦІЇ" `StackPanel` (line 260), insert a new always-visible section:

```xml
                        <StackPanel Margin="0,0,0,20">
                            <TextBlock Text="ДАТА ДОКУМЕНТА" Style="{StaticResource SectionTitleStyle}"/>
                            <Border Background="{StaticResource PanelBrush}" BorderBrush="{StaticResource BorderLineBrush}"
                                    BorderThickness="1" CornerRadius="0" Padding="16">
                                <DatePicker SelectedDate="{Binding DocumentDate}" HorizontalAlignment="Left" Width="160"/>
                            </Border>
                        </StackPanel>

```

(Note the blank line preserved before the existing `<StackPanel Margin="0,0,0,20">` that starts the manual-tag-form section — the file should end up with two consecutive `<StackPanel Margin="0,0,0,20">` blocks: the new "ДАТА ДОКУМЕНТА" one, then the existing "ЗНАЧЕННЯ ДЛЯ ЦІЄЇ ГЕНЕРАЦІЇ" one.)

- [ ] **Step 2: Build to verify no XAML compile errors**

Run: `dotnet build D:\GenDoc\GenDoc\GenDoc.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Manual verification**

Launch the app, open Генерація, select a package: confirm a "ДАТА ДОКУМЕНТА" section appears above "ЗНАЧЕННЯ ДЛЯ ЦІЄЇ ГЕНЕРАЦІЇ" (or standalone, if the package has no manual tags) with today's date pre-filled and an editable `DatePicker` with the standard calendar-icon flyout (same as every other `DatePicker` in the app). Change the date, run "Згенерувати всім" against a template that has `{{дата}}` mapped as a manual placeholder, and confirm the generated `.docx` contains the chosen date, not today's.

- [ ] **Step 4: Commit**

```bash
git add GenDoc/Views/Generation/GenerationView.xaml
git commit -m "feat: add Дата документа section to Generation screen"
```

---

## Self-Review Notes

- **Spec coverage:** Task 1-2 (schema + model + format catalog) → Task 3 (generation honors format) → Task 4-6 (Templates UI to pick format) covers the approved "Templates tab: date-format icon" location. Task 7-8 covers the approved "Generation tab: Дата документа, defaults to today, editable" location. The three already-`DatePicker`-equipped locations (Набори create dialog, Відрядження/Відпустка quick-doc dialog, Generation's existing manual-tag dates) needed no code changes, confirmed by research — nothing to add for them. The Import-wizard "assign to intake" location was explicitly deferred by the user and intentionally has no task here.
- **Placeholder scan:** no TBD/TODO, no "add appropriate error handling" — every step has literal code.
- **Type consistency:** `DocxMappingRowViewModel` constructor signature `(int id, string placeholderTag, MappingSourceType sourceType, string? fieldName, string? dateFormat)` matches its two call sites in Task 5 Step 4 and its test in Task 5 Step 1. `ITemplateService.GetMappings`/`SaveMappings` 5-tuple shapes match between Task 4 (interface + impl) and Task 5 (call sites). `GenerationViewModel.ApplyDocumentDate(Dictionary<string,string>, DateTime?)` signature matches between Task 7 Step 1 test and Step 4 implementation.
