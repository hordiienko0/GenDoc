# Курсовий офіцер + Новий flow генерації документів у «Постійний склад» Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** (A) Add `IsCourseOfficer` as a real bool flag on `Recipient` — checkbox in the edit form, filter + column in «Постійний склад», mappable in template placeholders. (B) Redesign `StaffDocDialog` (covers Відрядження/Відпустка/«в догонку») from a modal that silently closes on "Згенерувати" into a two-step in-window flow: step 1 «Формування» (unchanged form) → step 2 «Документи» (auto-shown after generation, lists every document ever generated for the selected people, with «Відкрити»/«Зберегти як…» actions per row).

**Architecture:** Feature A follows the existing `DatabaseSchemaInitializer`/`RecipientEditModel`/`RecipientService` three-layer mapping pattern already used for every other `Recipient` field. Feature B moves generation orchestration that currently lives in `StaffViewModel.OpenDocDialogAsync` (called only after the dialog closes) directly into `StaffDocDialogViewModel.Confirm()`, so the dialog can stay open and swap its visible content via a `ShowResults` bool instead of closing — the lower-risk option identified during planning, since it needs no changes to `DialogService`'s single-shot `ShowDialog`/`RequestClose` mechanism.

**Tech Stack:** .NET 8 / WPF (`net8.0-windows`), CommunityToolkit.Mvvm 8.4.2, EF Core over SQLCipher SQLite, xUnit (`GenDoc.Tests`).

## Global Constraints

- Schema changes go through `GenDoc/Services/DatabaseSchemaInitializer.cs` only — no EF Migrations. Follow the exact `AddMissingColumns`/`CurrentSchemaVersion` pattern already in that file (current version is 16; this plan bumps it to 17).
- MVVM pattern: `[ObservableProperty]` fields + `[RelayCommand]` methods (CommunityToolkit.Mvvm source generators), matching every existing ViewModel in this codebase.
- `RecipientEditWindow`'s form fields, layout, and validation rules are NOT to be touched beyond adding the one new checkbox — the user confirmed the form is otherwise good.
- No new templates/quick-actions tied to `IsCourseOfficer` — it is a plain filter/column/mapping flag only, per brainstorming decision. Do not add course-officer-specific document types.
- Feature B applies identically to all three `StaffDocDialog` scenarios (Відрядження/Відпустка/«в догонку») — do not special-case any one of them beyond the existing `ShowDateRange`/`Kind` distinctions already in the dialog.
- Tests: xUnit `[Fact]`, pure-logic unit tests only — this codebase has no ViewModel-with-mocked-DI test pattern anywhere; do not introduce one. XAML/orchestration changes get a manual verification step instead (matches the project's established convention).
- Reuse existing brushes/styles (`PanelBrush`, `BorderLineBrush`, `TextSecondaryBrush`, `AccentBrush`, `DangerBrush`, `GhostButtonStyle`, `BoolToVisibility` converter) — no new styles or converters.

---

## Task 1: Schema v17 — `Recipients.IsCourseOfficer` column

**Files:**
- Modify: `GenDoc/Services/DatabaseSchemaInitializer.cs`

**Interfaces:**
- Produces: a `NOT NULL DEFAULT 0` `INTEGER` column `IsCourseOfficer` on table `Recipients`, added idempotently via `AddMissingColumns`.

- [ ] **Step 1: Add the v17 column array**

Right after the existing `RecipientColumnsV15` field (around line 115-118 of `GenDoc/Services/DatabaseSchemaInitializer.cs`), add:

```csharp
    private static readonly (string Name, string Type)[] RecipientColumnsV17 =
    {
        ("IsCourseOfficer", "INTEGER NOT NULL DEFAULT 0")
    };
```

- [ ] **Step 2: Bump the schema version and add the v17 migration block**

Change line 9 from:
```csharp
    private const int CurrentSchemaVersion = 16;
```
to:
```csharp
    private const int CurrentSchemaVersion = 17;
```

Right after the `if (currentVersion < 16) { ... }` block (ends around line 352, `currentVersion = 16;` then the closing `}` of the block, itself just before the closing `}` of the outer `else`), add:

```csharp
            if (currentVersion < 17)
            {
                AddMissingColumns(db, "Recipients", RecipientColumnsV17);

                db.SchemaVersions.Add(new SchemaVersion
                {
                    Version = 17,
                    AppliedAt = DateTime.Now,
                    Description = "Постійний склад: ознака «курсовий офіцер»"
                });
                currentVersion = 17;
            }
```

- [ ] **Step 3: Add the idempotent top-level call**

Right after the existing line `AddMissingColumns(db, "TemplateFieldMappings", TemplateFieldMappingColumnsV16);` (around line 391, near the bottom of `EnsureInitialized`), add:

```csharp
        AddMissingColumns(db, "Recipients", RecipientColumnsV17);
```

- [ ] **Step 4: Build to verify no compile errors**

Run: `dotnet build D:\GenDoc\GenDoc\GenDoc.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add GenDoc/Services/DatabaseSchemaInitializer.cs
git commit -m "feat: add schema v17 — Recipients.IsCourseOfficer column"
```

---

## Task 2: `Recipient.IsCourseOfficer` model + DTO + service mapping

**Files:**
- Modify: `GenDoc/Models/Recipient.cs`
- Modify: `GenDoc/Services/Recipients/RecipientModels.cs`
- Modify: `GenDoc/Services/Recipients/RecipientService.cs`

**Interfaces:**
- Consumes: schema column from Task 1.
- Produces: `Recipient.IsCourseOfficer : bool`, `RecipientEditModel.IsCourseOfficer : bool`. Task 3 (`RecipientEditViewModel`) consumes both.

- [ ] **Step 1: Add the property to the entity**

In `GenDoc/Models/Recipient.cs`, add right after the `Vehicle` property (end of the questionnaire block, line 54):

```csharp
        public string? Vehicle { get; set; }

        // Постійний склад: курсовий офіцер (не звання/посада — окрема ознака,
        // впливає на фільтр списку й доступна для мапінгу плейсхолдерів шаблонів).
        public bool IsCourseOfficer { get; set; }
```

- [ ] **Step 2: Add the property to the DTO**

In `GenDoc/Services/Recipients/RecipientModels.cs`, add right after `Vehicle` in `RecipientEditModel` (line 53):

```csharp
        public string? Vehicle { get; set; }
        public bool IsCourseOfficer { get; set; }
```

- [ ] **Step 3: Thread it through `GetForEdit` and `Save`**

In `GenDoc/Services/Recipients/RecipientService.cs`, `GetForEdit` (around line 128-167), add to the object initializer right after `Vehicle = r.Vehicle,` (line 162):

```csharp
                Vehicle = r.Vehicle,
                IsCourseOfficer = r.IsCourseOfficer,
```

In `Save` (around line 198-253), add right after `recipient.Vehicle = TrimOrNull(model.Vehicle);` (line 246):

```csharp
            recipient.Vehicle = TrimOrNull(model.Vehicle);
            recipient.IsCourseOfficer = model.IsCourseOfficer;
```

- [ ] **Step 4: Build to verify no compile errors**

Run: `dotnet build D:\GenDoc\GenDoc\GenDoc.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add GenDoc/Models/Recipient.cs GenDoc/Services/Recipients/RecipientModels.cs GenDoc/Services/Recipients/RecipientService.cs
git commit -m "feat: add IsCourseOfficer to Recipient model and edit-model mapping"
```

---

## Task 3: `RecipientEditViewModel` + `RecipientEditWindow.xaml` — checkbox

**Files:**
- Modify: `GenDoc/ViewModels/Recipients/RecipientEditViewModel.cs`
- Modify: `GenDoc/Views/Recipients/RecipientEditWindow.xaml`

**Interfaces:**
- Consumes: `RecipientEditModel.IsCourseOfficer` (Task 2).
- Produces: `RecipientEditViewModel.IsCourseOfficer : bool` (`[ObservableProperty]`), bound from XAML.

- [ ] **Step 1: Add the `[ObservableProperty]`**

In `GenDoc/ViewModels/Recipients/RecipientEditViewModel.cs`, add right after `roomNumber` (line 82):

```csharp
    [ObservableProperty] private string? roomBuilding;
    [ObservableProperty] private string? roomNumber;
    [ObservableProperty] private bool isCourseOfficer;
```

- [ ] **Step 2: Load/reset it in `Initialize`**

In the edit branch (around line 213, right after `Vehicle = model.Vehicle;`), add:

```csharp
            Vehicle = model.Vehicle;
            IsCourseOfficer = model.IsCourseOfficer;
```

In the new-record branch (around line 256, right after `Vehicle = null;`), add:

```csharp
            Vehicle = null;
            IsCourseOfficer = false;
```

- [ ] **Step 3: Include it in `TrySave`'s object initializer**

Around line 444, right after `Vehicle = Vehicle,`, add:

```csharp
            Vehicle = Vehicle,
            IsCourseOfficer = IsCourseOfficer,
```

- [ ] **Step 4: Add the checkbox to the form**

In `GenDoc/Views/Recipients/RecipientEditWindow.xaml`, right after the Rank/Position `Grid` closes (line 198, `</Grid>`) and before the "Підрозділ" `StackPanel` (line 200), add:

```xml
                </Grid>

                <CheckBox Content="Курсовий офіцер" IsChecked="{Binding IsCourseOfficer}" Margin="0,0,0,10"/>

                <StackPanel Margin="0,0,0,10">
                    <TextBlock Text="Підрозділ *" Style="{StaticResource FieldLabelStyle}"/>
```

- [ ] **Step 5: Build to verify no XAML/compile errors**

Run: `dotnet build D:\GenDoc\GenDoc\GenDoc.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Manual verification**

Launch the app, open «Постійний склад» → «+ Додати», confirm the "Курсовий офіцер" checkbox appears right below Звання/Посада, toggle it, save, reopen the same person for editing, confirm the checkbox state persisted.

- [ ] **Step 7: Commit**

```bash
git add GenDoc/ViewModels/Recipients/RecipientEditViewModel.cs GenDoc/Views/Recipients/RecipientEditWindow.xaml
git commit -m "feat: add Курсовий офіцер checkbox to the recipient edit form"
```

---

## Task 4: Template placeholder mapping — `IsCourseOfficer` field

**Files:**
- Modify: `GenDoc/Services/Templates/TemplateFieldCatalog.cs`
- Modify: `GenDoc/Services/Generation/GenerationService.cs:815` area (`GetRecipientFieldValue`)
- Test: `GenDoc.Tests/GenerationServiceCourseOfficerFieldTests.cs`

**Interfaces:**
- Consumes: `Recipient.IsCourseOfficer` (Task 2).
- Produces: a new mappable `TemplateFieldOption("IsCourseOfficer", "Курсовий офіцер (Так/Ні)")`, and `GenerationService.GetRecipientFieldValue` renders it as `"Так"`/`"Ні"`.

- [ ] **Step 1: Write the failing test**

Create `GenDoc.Tests/GenerationServiceCourseOfficerFieldTests.cs`:

```csharp
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Generation;

namespace GenDoc.Tests;

public class GenerationServiceCourseOfficerFieldTests
{
    [Fact]
    public void BuildValues_IsCourseOfficerTrue_RendersTak()
    {
        var recipient = new Recipient { LastName = "Тест", FirstName = "Тест", IsCourseOfficer = true };
        var mappings = new List<TemplateFieldMapping>
        {
            new() { PlaceholderTag = "{{курсовий_офіцер}}", SourceType = MappingSourceType.Recipient, FieldName = "IsCourseOfficer" }
        };

        var values = GenerationService.BuildValues(mappings, recipient, org: null, manualValues: new Dictionary<string, string>());

        Assert.Equal("Так", values["{{курсовий_офіцер}}"]);
    }

    [Fact]
    public void BuildValues_IsCourseOfficerFalse_RendersNi()
    {
        var recipient = new Recipient { LastName = "Тест", FirstName = "Тест", IsCourseOfficer = false };
        var mappings = new List<TemplateFieldMapping>
        {
            new() { PlaceholderTag = "{{курсовий_офіцер}}", SourceType = MappingSourceType.Recipient, FieldName = "IsCourseOfficer" }
        };

        var values = GenerationService.BuildValues(mappings, recipient, org: null, manualValues: new Dictionary<string, string>());

        Assert.Equal("Ні", values["{{курсовий_офіцер}}"]);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test D:\GenDoc\GenDoc.Tests\GenDoc.Tests.csproj --filter GenerationServiceCourseOfficerFieldTests`
Expected: FAIL — both assertions get `string.Empty` (the switch's `_ => string.Empty` default), since no `"IsCourseOfficer"` case exists yet.

- [ ] **Step 3: Add the catalog entry**

In `GenDoc/Services/Templates/TemplateFieldCatalog.cs`, add to `RecipientFields` right after the `Vehicle` entry (line 42):

```csharp
        new("Vehicle", "Автомобіль, номер авто"),
        new("IsCourseOfficer", "Курсовий офіцер (Так/Ні)"),
```

- [ ] **Step 4: Add the switch case**

In `GenDoc/Services/Generation/GenerationService.cs`, in `GetRecipientFieldValue` (the switch expression around lines 784-829), add right after the `"Vehicle"` case (line 815):

```csharp
            "Vehicle" => r.Vehicle ?? string.Empty,
            "IsCourseOfficer" => r.IsCourseOfficer ? "Так" : "Ні",
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test D:\GenDoc\GenDoc.Tests\GenDoc.Tests.csproj --filter GenerationServiceCourseOfficerFieldTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Run the full test suite to check nothing else broke**

Run: `dotnet test D:\GenDoc\GenDoc.Tests\GenDoc.Tests.csproj`
Expected: all tests pass.

- [ ] **Step 7: Commit**

```bash
git add GenDoc/Services/Templates/TemplateFieldCatalog.cs GenDoc/Services/Generation/GenerationService.cs GenDoc.Tests/GenerationServiceCourseOfficerFieldTests.cs
git commit -m "feat: make IsCourseOfficer mappable in template placeholders"
```

---

## Task 5: Thread `IsCourseOfficer` to the «Постійний склад» grid row

**Files:**
- Modify: `GenDoc/Services/Staff/IStaffService.cs`
- Modify: `GenDoc/Services/Staff/StaffService.cs`
- Modify: `GenDoc/ViewModels/Staff/StaffRowViewModel.cs`

**Interfaces:**
- Consumes: `Recipient.IsCourseOfficer` (Task 2).
- Produces: `StaffRowOverview.IsCourseOfficer : bool` (new positional field, appended last), `StaffRowViewModel.IsCourseOfficer : bool`. Task 6 (`StaffViewModel`/`StaffView.xaml`) consumes both.

- [ ] **Step 1: Widen `StaffRowOverview`**

In `GenDoc/Services/Staff/IStaffService.cs`, change:
```csharp
    public record StaffRowOverview(
        int Id, string FullName, string Rank, string Position, string? UnitName,
        StaffEventKind? CurrentState, int DocsCount);
```
to:
```csharp
    public record StaffRowOverview(
        int Id, string FullName, string Rank, string Position, string? UnitName,
        StaffEventKind? CurrentState, int DocsCount, bool IsCourseOfficer);
```

- [ ] **Step 2: Update the construction site**

In `GenDoc/Services/Staff/StaffService.cs`, `GetOverviewAsync` (around lines 57-63), change:
```csharp
            var rows = new List<StaffRowOverview>(staff.Count);
            foreach (var r in staff)
            {
                var state = stateByRecipient.TryGetValue(r.Id, out var kind) ? kind : (StaffEventKind?)null;
                rows.Add(new StaffRowOverview(
                    r.Id, r.FullName, r.Rank, r.Position, r.Unit?.Name, state, docCounts.GetValueOrDefault(r.Id)));
            }
```
to:
```csharp
            var rows = new List<StaffRowOverview>(staff.Count);
            foreach (var r in staff)
            {
                var state = stateByRecipient.TryGetValue(r.Id, out var kind) ? kind : (StaffEventKind?)null;
                rows.Add(new StaffRowOverview(
                    r.Id, r.FullName, r.Rank, r.Position, r.Unit?.Name, state, docCounts.GetValueOrDefault(r.Id),
                    r.IsCourseOfficer));
            }
```

- [ ] **Step 3: Thread it into the row ViewModel**

In `GenDoc/ViewModels/Staff/StaffRowViewModel.cs`, change the constructor body and add a property:
```csharp
        public StaffRowViewModel(StaffRowOverview overview)
        {
            Id = overview.Id;
            FullName = overview.FullName;
            Rank = overview.Rank;
            Position = overview.Position;
            UnitName = overview.UnitName;
            CurrentState = overview.CurrentState;
            DocsCount = overview.DocsCount;
            IsCourseOfficer = overview.IsCourseOfficer;
        }

        public int Id { get; }
        public string FullName { get; }
        public string Rank { get; }
        public string Position { get; }
        public string? UnitName { get; }
        public StaffEventKind? CurrentState { get; }
        public int DocsCount { get; }
        public bool IsCourseOfficer { get; }
```

- [ ] **Step 4: Build to verify no compile errors**

Run: `dotnet build D:\GenDoc\GenDoc\GenDoc.csproj`
Expected: Build succeeded, 0 errors. (Since `StaffRowOverview` is a positional record and the new field is appended last, no other construction site should break — confirm by checking the build output has zero errors, not just in these files.)

- [ ] **Step 5: Commit**

```bash
git add GenDoc/Services/Staff/IStaffService.cs GenDoc/Services/Staff/StaffService.cs GenDoc/ViewModels/Staff/StaffRowViewModel.cs
git commit -m "feat: thread IsCourseOfficer through to the staff grid row"
```

---

## Task 6: «Постійний склад» filter + grid column

**Files:**
- Modify: `GenDoc/ViewModels/Staff/StaffViewModel.cs`
- Modify: `GenDoc/Views/Staff/StaffView.xaml`

**Interfaces:**
- Consumes: `StaffRowOverview.IsCourseOfficer`/`StaffRowViewModel.IsCourseOfficer` (Task 5).

- [ ] **Step 1: Add the filter property and wire it into `ApplyFilter`**

In `GenDoc/ViewModels/Staff/StaffViewModel.cs`, add a new `[ObservableProperty]` right after `searchText` (line 56):

```csharp
        [ObservableProperty]
        private string? searchText;

        [ObservableProperty]
        private bool showOnlyCourseOfficers;
```

Add a partial-change handler next to the existing ones (around line 80):

```csharp
        partial void OnSearchTextChanged(string? value) => ApplyFilter();
        partial void OnShowOnlyCourseOfficersChanged(bool value) => ApplyFilter();
```

In `ApplyFilter()` (around line 108-129), add the new filter right after the state-switch block (after line 121, before the `SearchText` block):

```csharp
            filtered = SelectedState?.Filter switch
            {
                StaffStateFilter.OnSite => filtered.Where(o => o.CurrentState is null),
                StaffStateFilter.BusinessTrip => filtered.Where(o => o.CurrentState == StaffEventKind.BusinessTrip),
                StaffStateFilter.Leave => filtered.Where(o => o.CurrentState == StaffEventKind.Leave),
                _ => filtered
            };

            if (ShowOnlyCourseOfficers)
                filtered = filtered.Where(o => o.IsCourseOfficer);

            var query = SearchText?.Trim();
```

- [ ] **Step 2: Add the filter checkbox to the XAML**

In `GenDoc/Views/Staff/StaffView.xaml`, in the filter bar (lines 64-72), add a `CheckBox` right after the state `ComboBox` (line 68) and before the search `TextBox` (line 69):

```xml
            <ComboBox Width="140" Margin="0,0,8,0" ItemsSource="{Binding StateOptions}"
                      SelectedItem="{Binding SelectedState}" DisplayMemberPath="Label"/>
            <CheckBox Content="Тільки курсові офіцери" IsChecked="{Binding ShowOnlyCourseOfficers}"
                      VerticalAlignment="Center" Margin="0,0,8,0"/>
            <TextBox Width="220" Tag="Пошук за ПІБ або посадою…"
                     Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}"/>
```

- [ ] **Step 3: Add the grid column**

In the same file, in `DataGrid.Columns` (lines 128-159), add a narrow column right after "Посада" (line 141) and before the "Стан" `DataGridTemplateColumn` (line 143):

```xml
                            <DataGridTextColumn Header="Посада" Binding="{Binding Position}" Width="1.6*"/>

                            <DataGridTemplateColumn Header="КО" Width="40">
                                <DataGridTemplateColumn.CellTemplate>
                                    <DataTemplate>
                                        <TextBlock Text="✓" FontWeight="Bold" Foreground="{StaticResource AccentBrush}"
                                                   HorizontalAlignment="Center" VerticalAlignment="Center"
                                                   Visibility="{Binding IsCourseOfficer, Converter={StaticResource BoolToVisibility}}"/>
                                    </DataTemplate>
                                </DataGridTemplateColumn.CellTemplate>
                            </DataGridTemplateColumn>

                            <DataGridTemplateColumn Header="Стан" Width="130">
```

- [ ] **Step 4: Build to verify no XAML/compile errors**

Run: `dotnet build D:\GenDoc\GenDoc\GenDoc.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Manual verification**

Launch the app, open «Постійний склад», mark a person as "Курсовий офіцер" (via the Task 3 checkbox) and save, confirm the "✓" appears in the "КО" column for that row, toggle "Тільки курсові офіцери" and confirm the grid filters down to just that person, untoggle and confirm the full list returns.

- [ ] **Step 6: Commit**

```bash
git add GenDoc/ViewModels/Staff/StaffViewModel.cs GenDoc/Views/Staff/StaffView.xaml
git commit -m "feat: add course-officer filter and column to Постійний склад"
```

---

## Task 7: `StaffDocResultRowViewModel` — new results-row ViewModel

**Files:**
- Create: `GenDoc/ViewModels/Staff/StaffDocResultRowViewModel.cs`
- Test: `GenDoc.Tests/StaffDocResultRowViewModelTests.cs`

**Interfaces:**
- Consumes: `IDocumentArchiveService.OpenAsync(int) : Task`, `IDocumentArchiveService.SaveAsAsync(int, string) : Task<ArchiveOpResult>` (both already exist, unchanged).
- Produces: `StaffDocResultRowViewModel(IDocumentArchiveService archiveService, int? documentId, string fileName, string recipientName, string templateName, bool success, string? errorMessage)`, with `FileName`, `RecipientName`, `TemplateName`, `Success`, `ErrorMessage`, `CanOpen : bool`, `OpenCommand`, `SaveAsCommand`. Task 8 (`StaffDocDialogViewModel`) constructs these; Task 9 (XAML) binds to them.

- [ ] **Step 1: Write the failing test**

Create `GenDoc.Tests/StaffDocResultRowViewModelTests.cs`:

```csharp
using GenDoc.Services.Documents;
using GenDoc.ViewModels.Staff;

namespace GenDoc.Tests;

public class StaffDocResultRowViewModelTests
{
    [Fact]
    public void CanOpen_SuccessWithDocumentId_IsTrue()
    {
        var row = new StaffDocResultRowViewModel(
            archiveService: null!, documentId: 5, fileName: "report.docx",
            recipientName: "ТЕСТ Т.Т.", templateName: "Рапорт", success: true, errorMessage: null);

        Assert.True(row.CanOpen);
    }

    [Fact]
    public void CanOpen_Failure_IsFalse()
    {
        var row = new StaffDocResultRowViewModel(
            archiveService: null!, documentId: null, fileName: string.Empty,
            recipientName: "ТЕСТ Т.Т.", templateName: "Рапорт", success: false, errorMessage: "Не вдалося згенерувати");

        Assert.False(row.CanOpen);
    }

    [Fact]
    public void CanOpen_SuccessWithoutDocumentId_IsFalse()
    {
        var row = new StaffDocResultRowViewModel(
            archiveService: null!, documentId: null, fileName: string.Empty,
            recipientName: "ТЕСТ Т.Т.", templateName: "Рапорт", success: true, errorMessage: null);

        Assert.False(row.CanOpen);
    }

    [Fact]
    public void Constructor_ExposesAllSuppliedValues()
    {
        var row = new StaffDocResultRowViewModel(
            archiveService: null!, documentId: 7, fileName: "trip.docx",
            recipientName: "ІВАНЕНКО І.І.", templateName: "Посвідчення", success: true, errorMessage: null);

        Assert.Equal("trip.docx", row.FileName);
        Assert.Equal("ІВАНЕНКО І.І.", row.RecipientName);
        Assert.Equal("Посвідчення", row.TemplateName);
        Assert.True(row.Success);
        Assert.Null(row.ErrorMessage);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test D:\GenDoc\GenDoc.Tests\GenDoc.Tests.csproj --filter StaffDocResultRowViewModelTests`
Expected: FAIL — compile error, `GenDoc.ViewModels.Staff.StaffDocResultRowViewModel` does not exist.

- [ ] **Step 3: Implement the class**

Create `GenDoc/ViewModels/Staff/StaffDocResultRowViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Services.Documents;
using Microsoft.Win32;

namespace GenDoc.ViewModels.Staff
{
    // Один рядок кроку «Документи» у StaffDocDialog — і щойно згенерований, і
    // будь-який раніше створений документ для обраних людей (перечитується через
    // IDocumentArchiveService.GetCurrentRowAsync після генерації).
    public partial class StaffDocResultRowViewModel : ObservableObject
    {
        private readonly IDocumentArchiveService _archiveService;
        private readonly int? _documentId;

        public StaffDocResultRowViewModel(
            IDocumentArchiveService archiveService, int? documentId, string fileName,
            string recipientName, string templateName, bool success, string? errorMessage)
        {
            _archiveService = archiveService;
            _documentId = documentId;
            FileName = fileName;
            RecipientName = recipientName;
            TemplateName = templateName;
            Success = success;
            ErrorMessage = errorMessage;
        }

        public string FileName { get; }
        public string RecipientName { get; }
        public string TemplateName { get; }
        public bool Success { get; }
        public string? ErrorMessage { get; }

        public bool CanOpen => Success && _documentId is not null;

        [RelayCommand(CanExecute = nameof(CanOpen))]
        private async Task OpenAsync() => await _archiveService.OpenAsync(_documentId!.Value);

        [RelayCommand(CanExecute = nameof(CanOpen))]
        private async Task SaveAsAsync()
        {
            var dialog = new SaveFileDialog { FileName = FileName };
            if (dialog.ShowDialog() != true) return;

            await _archiveService.SaveAsAsync(_documentId!.Value, dialog.FileName);
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test D:\GenDoc\GenDoc.Tests\GenDoc.Tests.csproj --filter StaffDocResultRowViewModelTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add GenDoc/ViewModels/Staff/StaffDocResultRowViewModel.cs GenDoc.Tests/StaffDocResultRowViewModelTests.cs
git commit -m "feat: add StaffDocResultRowViewModel for the staff-doc results step"
```

---

## Task 8: `StaffDocDialogViewModel` — two-step generation flow

**Files:**
- Modify: `GenDoc/ViewModels/Staff/StaffDocDialogViewModel.cs`

**Interfaces:**
- Consumes: `IStaffService.GenerateDocumentsAsync`/`IssueDocumentsAsync` (unchanged signatures), `IDocumentArchiveService.GetCurrentRowAsync(int, int) : Task<ArchiveRowDto?>` (existing method), `StaffDocResultRowViewModel` (Task 7).
- Produces: `StaffDocDialogViewModel.ShowResults : bool`, `IsFormStep : bool` (computed), `Results : ObservableCollection<StaffDocResultRowViewModel>`, `ResultsSummaryText : string`, `GenerateAnotherCommand`, `FinishCommand`. `ConfirmCommand` now performs generation itself instead of just closing. Task 9 (XAML) binds to all of these. Task 10 (`StaffViewModel.OpenDocDialogAsync`) no longer needs `BuildRequest()`/post-close generation calls.

- [ ] **Step 1: Inject `IStaffService` and store people with names**

Change the constructor and fields at the top of `GenDoc/ViewModels/Staff/StaffDocDialogViewModel.cs`:

```csharp
        private readonly IGenerationService _generationService;
        private readonly IDocumentArchiveService _archiveService;
        private readonly IManualTagFormBuilder _manualTagFormBuilder;
        private readonly Staff.IStaffService _staffService;

        public StaffDocDialogViewModel(
            IGenerationService generationService, IDocumentArchiveService archiveService,
            IManualTagFormBuilder manualTagFormBuilder, Staff.IStaffService staffService)
        {
            _generationService = generationService;
            _archiveService = archiveService;
            _manualTagFormBuilder = manualTagFormBuilder;
            _staffService = staffService;
        }

        public StaffEventKind? Kind { get; private set; }
        private IReadOnlyList<(int Id, string FullName)> _people = Array.Empty<(int, string)>();
```

Remove the old `private IReadOnlyList<int> _recipientIds = Array.Empty<int>();` field — `_people` replaces it (recipient IDs are now `_people.Select(p => p.Id)` where needed).

Note: `Services.Staff.IStaffService` is referenced as `Staff.IStaffService` above because this file's namespace is `GenDoc.ViewModels.Staff` — add `using GenDoc.Services.Staff;` to the file's usings instead and reference it as plain `IStaffService`, matching the style of the file's other `using GenDoc.Services.Documents;`/`using GenDoc.Services.Generation;` lines. Use whichever the compiler accepts cleanly; prefer the plain `using` import.

- [ ] **Step 2: Add the two-step state**

Add these new `[ObservableProperty]`/properties near the existing `errorText` field (around line 63):

```csharp
        [ObservableProperty] private string? note;
        [ObservableProperty] private string? errorText;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsFormStep))]
        private bool showResults;

        public bool IsFormStep => !ShowResults;

        [ObservableProperty] private bool isBusy;
        [ObservableProperty] private string resultsSummaryText = string.Empty;

        public ObservableCollection<StaffDocResultRowViewModel> Results { get; } = new();
```

- [ ] **Step 3: Update `Initialize` to store people (not just IDs) and reset step state**

Change the signature and body:

```csharp
        public void Initialize(StaffEventKind? kind, IReadOnlyList<(int Id, string FullName)> people)
        {
            Kind = kind;
            _people = people;
            ShowDateRange = kind is not null;
            HeaderText = kind switch
            {
                StaffEventKind.BusinessTrip => "Оформити відрядження",
                StaffEventKind.Leave => "Оформити відпустку",
                _ => "Згенерувати документи"
            };
            PeopleText = string.Join(", ", people.Select(p => p.FullName));

            Templates.Clear();
            var source = kind is null ? _generationService.GetPerRecipientTemplates() : _generationService.GetAllTemplates();
            foreach (var (id, name) in source)
            {
                var item = new TemplateCheckOptionViewModel(id, name);
                item.PropertyChanged += async (_, e) =>
                {
                    if (e.PropertyName == nameof(TemplateCheckOptionViewModel.IsChecked))
                    {
                        ConfirmCommand.NotifyCanExecuteChanged();
                        await RefreshManualTagFormAsync();
                    }
                };
                Templates.Add(item);
            }

            DateStart = DateTime.Today;
            DateEnd = DateTime.Today;
            Note = null;
            ErrorText = null;
            ManualTagForm = null;
            ShowResults = false;
            Results.Clear();
        }
```

(This is the same body as before, with `_recipientIds = people.Select(p => p.Id).ToList();` replaced by `_people = people;` and the four new lines `ShowResults = false; Results.Clear();` appended.)

- [ ] **Step 4: Replace `Confirm()` with an async generate-and-show-results flow**

Replace:
```csharp
        [RelayCommand(CanExecute = nameof(CanConfirm))]
        private void Confirm() => CloseDialog(true);
```
with:
```csharp
        [RelayCommand(CanExecute = nameof(CanConfirm))]
        private async Task ConfirmAsync()
        {
            IsBusy = true;
            try
            {
                var templateIds = Templates.Where(t => t.IsChecked).Select(t => t.Id).ToList();
                var manualValues = ManualTagForm?.GetValues() ?? new Dictionary<string, string>();
                var recipientIds = _people.Select(p => p.Id).ToList();

                if (Kind is null)
                {
                    await _staffService.GenerateDocumentsAsync(recipientIds, templateIds, manualValues);
                }
                else
                {
                    await _staffService.IssueDocumentsAsync(
                        Kind.Value, recipientIds, templateIds,
                        DateOnly.FromDateTime(DateStart!.Value), DateOnly.FromDateTime(DateEnd!.Value),
                        Note, manualValues);
                }

                await SaveManualValuesAsync();

                Results.Clear();
                foreach (var person in _people)
                {
                    foreach (var templateId in templateIds)
                    {
                        var templateName = Templates.First(t => t.Id == templateId).Name;
                        var row = await _archiveService.GetCurrentRowAsync(person.Id, templateId);
                        Results.Add(new StaffDocResultRowViewModel(
                            _archiveService, row?.Id, row?.FileName ?? string.Empty,
                            person.FullName, templateName,
                            success: row is not null,
                            errorMessage: row is null ? "Не вдалося згенерувати" : null));
                    }
                }

                var okCount = Results.Count(r => r.Success);
                var errCount = Results.Count - okCount;
                ResultsSummaryText = $"Згенеровано: {okCount} · помилок: {errCount}";

                ShowResults = true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private void GenerateAnother() => ShowResults = false;

        [RelayCommand]
        private void Finish() => CloseDialog(true);
```

- [ ] **Step 5: Remove `BuildRequest()`**

Delete the `BuildRequest()` method (around lines 134-140) — it's no longer used; `ConfirmAsync` now builds and consumes the request data directly. (Task 10 removes its only caller.)

- [ ] **Step 6: Build to verify no compile errors**

Run: `dotnet build D:\GenDoc\GenDoc\GenDoc.csproj`
Expected: fails — `StaffViewModel.cs` still calls `vm.BuildRequest()` and expects `Confirm`'s old close-on-click behavior via `ShowDialog(...) != true`. This is expected; Task 10 fixes the only remaining caller. Confirm the ONLY errors are in `StaffViewModel.cs`.

- [ ] **Step 7: Commit**

```bash
git add GenDoc/ViewModels/Staff/StaffDocDialogViewModel.cs
git commit -m "feat: generate documents and show results inline in StaffDocDialogViewModel"
```

---

## Task 9: `StaffDocDialog.xaml` — two-step layout

**Files:**
- Modify: `GenDoc/Views/Staff/StaffDocDialog.xaml`

**Interfaces:**
- Consumes: `StaffDocDialogViewModel.IsFormStep`/`ShowResults`/`Results`/`ResultsSummaryText`/`GenerateAnotherCommand`/`FinishCommand` (Task 8), `StaffDocResultRowViewModel.FileName`/`RecipientName`/`TemplateName`/`Success`/`ErrorMessage`/`OpenCommand`/`SaveAsCommand` (Task 7).

- [ ] **Step 1: Replace the window's body/footer with the two-step structure**

Replace the ENTIRE contents of `GenDoc/Views/Staff/StaffDocDialog.xaml` with:

```xml
<Window x:Class="GenDoc.Views.Staff.StaffDocDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Style="{StaticResource {x:Type Window}}"
        Title="{Binding HeaderText}" Width="560" MaxHeight="760" SizeToContent="Height"
        Background="{StaticResource PanelBrush}"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize">

    <Window.InputBindings>
        <KeyBinding Key="Escape" Command="{Binding CancelCommand}"/>
    </Window.InputBindings>

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Заголовок -->
        <Border Grid.Row="0" BorderBrush="{StaticResource BorderLineBrush}" BorderThickness="0,0,0,1" Padding="16,12">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <TextBlock Grid.Column="0" Text="{Binding HeaderText}" Foreground="{StaticResource TextPrimaryBrush}"
                           FontSize="14.5" FontWeight="Bold" VerticalAlignment="Center"/>
                <Button Grid.Column="1" Content="&#xE711;" FontFamily="Segoe MDL2 Assets" FontSize="11"
                        Style="{StaticResource CloseButtonStyle}" Command="{Binding CancelCommand}"/>
            </Grid>
        </Border>

        <!-- Індикатор кроків -->
        <Grid Grid.Row="1" Background="{StaticResource BackgroundBrush}">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            <Border Grid.Column="0" Padding="10,8" BorderBrush="{StaticResource AccentBrush}" BorderThickness="0,0,0,2">
                <Border.Style>
                    <Style TargetType="Border">
                        <Setter Property="BorderThickness" Value="0"/>
                        <Style.Triggers>
                            <DataTrigger Binding="{Binding IsFormStep}" Value="True">
                                <Setter Property="BorderThickness" Value="0,0,0,2"/>
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </Border.Style>
                <TextBlock Text="1. Формування" HorizontalAlignment="Center" FontSize="12" FontWeight="SemiBold"
                           Foreground="{StaticResource TextSecondaryBrush}">
                    <TextBlock.Style>
                        <Style TargetType="TextBlock">
                            <Style.Triggers>
                                <DataTrigger Binding="{Binding IsFormStep}" Value="True">
                                    <Setter Property="Foreground" Value="{StaticResource AccentBrush}"/>
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </TextBlock.Style>
                </TextBlock>
            </Border>
            <Border Grid.Column="1" Padding="10,8" BorderBrush="{StaticResource AccentBrush}">
                <Border.Style>
                    <Style TargetType="Border">
                        <Setter Property="BorderThickness" Value="0"/>
                        <Style.Triggers>
                            <DataTrigger Binding="{Binding ShowResults}" Value="True">
                                <Setter Property="BorderThickness" Value="0,0,0,2"/>
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </Border.Style>
                <TextBlock Text="2. Документи" HorizontalAlignment="Center" FontSize="12" FontWeight="SemiBold"
                           Foreground="{StaticResource TextSecondaryBrush}">
                    <TextBlock.Style>
                        <Style TargetType="TextBlock">
                            <Style.Triggers>
                                <DataTrigger Binding="{Binding ShowResults}" Value="True">
                                    <Setter Property="Foreground" Value="{StaticResource AccentBrush}"/>
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </TextBlock.Style>
                </TextBlock>
            </Border>
        </Grid>

        <!-- Крок 1: Формування -->
        <StackPanel Grid.Row="2" Margin="22,16" Visibility="{Binding IsFormStep, Converter={StaticResource BoolToVisibility}}">

            <TextBlock Text="{Binding PeopleText}" FontSize="12.5" TextWrapping="Wrap"
                       Foreground="{StaticResource TextSecondaryBrush}" Margin="0,0,0,14"/>

            <Grid Margin="0,0,0,14" Visibility="{Binding ShowDateRange, Converter={StaticResource BoolToVisibility}}">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="1*"/>
                    <ColumnDefinition Width="10"/>
                    <ColumnDefinition Width="1*"/>
                </Grid.ColumnDefinitions>
                <StackPanel Grid.Column="0">
                    <TextBlock Text="Дата початку" FontSize="11.5" Foreground="{StaticResource TextSecondaryBrush}" Margin="0,0,0,4"/>
                    <DatePicker SelectedDate="{Binding DateStart}"/>
                </StackPanel>
                <StackPanel Grid.Column="2">
                    <TextBlock Text="Дата закінчення" FontSize="11.5" Foreground="{StaticResource TextSecondaryBrush}" Margin="0,0,0,4"/>
                    <DatePicker SelectedDate="{Binding DateEnd}"/>
                </StackPanel>
            </Grid>

            <TextBlock Text="Документи" FontSize="11.5" Foreground="{StaticResource TextSecondaryBrush}" Margin="0,0,0,6"/>
            <Border BorderBrush="{StaticResource BorderLineBrush}" BorderThickness="1" MaxHeight="180" Margin="0,0,0,14">
                <ScrollViewer VerticalScrollBarVisibility="Auto">
                    <ItemsControl ItemsSource="{Binding Templates}" Margin="4">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <CheckBox Content="{Binding Name}" IsChecked="{Binding IsChecked}" Margin="4,5"/>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </ScrollViewer>
            </Border>

            <StackPanel>
                <StackPanel.Style>
                    <Style TargetType="StackPanel">
                        <Style.Triggers>
                            <DataTrigger Binding="{Binding ManualTagForm}" Value="{x:Null}">
                                <Setter Property="Visibility" Value="Collapsed"/>
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </StackPanel.Style>
                <TextBlock Text="Ручні поля обраних шаблонів" FontSize="11.5" Foreground="{StaticResource TextSecondaryBrush}" Margin="0,0,0,6"/>
                <Border BorderBrush="{StaticResource BorderLineBrush}" BorderThickness="1" MaxHeight="220" Margin="0,0,0,14">
                    <ScrollViewer VerticalScrollBarVisibility="Auto">
                        <StackPanel Margin="8" DataContext="{Binding ManualTagForm}">
                            <ItemsControl ItemsSource="{Binding Rows}">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate>
                                        <StackPanel Margin="0,0,0,8">
                                            <TextBlock Text="{Binding Tag}" FontSize="11.5"
                                                       Foreground="{StaticResource TextSecondaryBrush}" Margin="0,0,0,4"/>
                                            <DatePicker SelectedDate="{Binding DateValue}">
                                                <DatePicker.Style>
                                                    <Style TargetType="DatePicker">
                                                        <Style.Triggers>
                                                            <DataTrigger Binding="{Binding Kind}" Value="Text">
                                                                <Setter Property="Visibility" Value="Collapsed"/>
                                                            </DataTrigger>
                                                        </Style.Triggers>
                                                    </Style>
                                                </DatePicker.Style>
                                            </DatePicker>
                                            <TextBox Text="{Binding Value, UpdateSourceTrigger=PropertyChanged}">
                                                <TextBox.Style>
                                                    <Style TargetType="TextBox">
                                                        <Style.Triggers>
                                                            <DataTrigger Binding="{Binding Kind}" Value="Date">
                                                                <Setter Property="Visibility" Value="Collapsed"/>
                                                            </DataTrigger>
                                                        </Style.Triggers>
                                                    </Style>
                                                </TextBox.Style>
                                            </TextBox>
                                        </StackPanel>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>

                            <StackPanel Margin="0,0,0,8">
                                <StackPanel.Style>
                                    <Style TargetType="StackPanel">
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding Signer}" Value="{x:Null}">
                                                <Setter Property="Visibility" Value="Collapsed"/>
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </StackPanel.Style>
                                <TextBlock Text="підписант" FontSize="11.5"
                                           Foreground="{StaticResource TextSecondaryBrush}" Margin="0,0,0,4"/>
                                <ComboBox DataContext="{Binding Signer}"
                                          ItemsSource="{Binding Options}" SelectedItem="{Binding Selected}"
                                          DisplayMemberPath="DisplayLabel"/>
                            </StackPanel>
                        </StackPanel>
                    </ScrollViewer>
                </Border>
            </StackPanel>

            <StackPanel Visibility="{Binding ShowDateRange, Converter={StaticResource BoolToVisibility}}">
                <TextBlock Text="Примітка" FontSize="11.5" Foreground="{StaticResource TextSecondaryBrush}" Margin="0,0,0,4"/>
                <TextBox Text="{Binding Note, UpdateSourceTrigger=PropertyChanged}" AcceptsReturn="True"
                         Height="52" TextWrapping="Wrap" VerticalScrollBarVisibility="Auto" Margin="0,0,0,10"/>
            </StackPanel>

            <TextBlock Text="{Binding ErrorText}" FontSize="11.5" Foreground="{StaticResource DangerBrush}" TextWrapping="Wrap">
                <TextBlock.Style>
                    <Style TargetType="TextBlock">
                        <Style.Triggers>
                            <DataTrigger Binding="{Binding ErrorText}" Value="{x:Null}">
                                <Setter Property="Visibility" Value="Collapsed"/>
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </TextBlock.Style>
            </TextBlock>
        </StackPanel>

        <!-- Крок 2: Документи -->
        <StackPanel Grid.Row="2" Margin="22,16" Visibility="{Binding ShowResults, Converter={StaticResource BoolToVisibility}}">
            <Border Background="{StaticResource AccentSoftBrush}" Padding="10,8" Margin="0,0,0,12">
                <TextBlock Text="{Binding ResultsSummaryText}" FontSize="12.5" FontWeight="SemiBold"/>
            </Border>

            <Border BorderBrush="{StaticResource BorderLineBrush}" BorderThickness="1" MaxHeight="360">
                <ScrollViewer VerticalScrollBarVisibility="Auto">
                    <ItemsControl ItemsSource="{Binding Results}" Margin="4">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Grid Margin="6,8" >
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*"/>
                                        <ColumnDefinition Width="Auto"/>
                                        <ColumnDefinition Width="Auto"/>
                                    </Grid.ColumnDefinitions>
                                    <StackPanel Grid.Column="0">
                                        <TextBlock Text="{Binding TemplateName}" FontSize="12.5" FontWeight="SemiBold"/>
                                        <TextBlock Text="{Binding RecipientName}" FontSize="11.5"
                                                   Foreground="{StaticResource TextSecondaryBrush}"/>
                                        <TextBlock Text="{Binding ErrorMessage}" FontSize="11"
                                                   Foreground="{StaticResource DangerBrush}">
                                            <TextBlock.Style>
                                                <Style TargetType="TextBlock">
                                                    <Style.Triggers>
                                                        <DataTrigger Binding="{Binding Success}" Value="True">
                                                            <Setter Property="Visibility" Value="Collapsed"/>
                                                        </DataTrigger>
                                                    </Style.Triggers>
                                                </Style>
                                            </TextBlock.Style>
                                        </TextBlock>
                                    </StackPanel>
                                    <Button Grid.Column="1" Content="Відкрити" Style="{StaticResource GhostButtonStyle}"
                                            Command="{Binding OpenCommand}" Margin="6,0,0,0" VerticalAlignment="Center"/>
                                    <Button Grid.Column="2" Content="Зберегти як…" Style="{StaticResource GhostButtonStyle}"
                                            Command="{Binding SaveAsCommand}" Margin="6,0,0,0" VerticalAlignment="Center"/>
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </ScrollViewer>
            </Border>
        </StackPanel>

        <!-- Футер: крок 1 -->
        <Border Grid.Row="3" BorderBrush="{StaticResource BorderLineBrush}" BorderThickness="0,1,0,0" Padding="22,16"
                Visibility="{Binding IsFormStep, Converter={StaticResource BoolToVisibility}}">
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                <Button Content="Скасувати" Style="{StaticResource GhostButtonStyle}" Command="{Binding CancelCommand}" IsCancel="True"/>
                <Button Content="Згенерувати" Command="{Binding ConfirmCommand}" IsDefault="True" Margin="10,0,0,0"/>
            </StackPanel>
        </Border>

        <!-- Футер: крок 2 -->
        <Border Grid.Row="3" BorderBrush="{StaticResource BorderLineBrush}" BorderThickness="0,1,0,0" Padding="22,16"
                Visibility="{Binding ShowResults, Converter={StaticResource BoolToVisibility}}">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <Button Grid.Column="0" Content="← Ще один документ" Style="{StaticResource GhostButtonStyle}"
                        Command="{Binding GenerateAnotherCommand}" HorizontalAlignment="Left"/>
                <Button Grid.Column="1" Content="Готово" Command="{Binding FinishCommand}" IsDefault="True"/>
            </Grid>
        </Border>
    </Grid>
</Window>
```

- [ ] **Step 2: Build to verify no XAML compile errors**

Run: `dotnet build D:\GenDoc\GenDoc\GenDoc.csproj`
Expected: Build succeeded, 0 errors (assuming Task 10 has already fixed the caller — if run before Task 10, expect the same `StaffViewModel.cs` errors noted in Task 8 Step 6, nothing new from this file).

- [ ] **Step 3: Commit**

```bash
git add GenDoc/Views/Staff/StaffDocDialog.xaml
git commit -m "feat: two-step Формування/Документи layout for StaffDocDialog"
```

---

## Task 10: `StaffViewModel.OpenDocDialogAsync` — simplify caller

**Files:**
- Modify: `GenDoc/ViewModels/Staff/StaffViewModel.cs`

**Interfaces:**
- Consumes: `StaffDocDialogViewModel.Initialize` (unchanged signature, Task 8).

- [ ] **Step 1: Simplify the method**

In `GenDoc/ViewModels/Staff/StaffViewModel.cs`, replace `OpenDocDialogAsync` (lines 198-221):

```csharp
        private async Task OpenDocDialogAsync(StaffEventKind? kind, IReadOnlyList<(int Id, string FullName)>? people = null)
        {
            var selected = people ?? Rows.Where(r => r.IsChecked).Select(r => (r.Id, r.FullName)).ToList();
            if (selected.Count == 0) return;

            var vm = _serviceProvider.GetRequiredService<StaffDocDialogViewModel>();
            vm.Initialize(kind, selected);
            if (_dialogService.ShowDialog(vm, Application.Current.MainWindow) != true) return;

            var request = vm.BuildRequest();
            if (kind is null)
            {
                await _staffService.GenerateDocumentsAsync(request.RecipientIds, request.TemplateIds, request.ManualValues);
            }
            else
            {
                await _staffService.IssueDocumentsAsync(
                    kind.Value, request.RecipientIds, request.TemplateIds, request.DateStart, request.DateEnd,
                    request.Note, request.ManualValues);
            }

            await vm.SaveManualValuesAsync();
            await RefreshAsync();
        }
```
with:
```csharp
        private async Task OpenDocDialogAsync(StaffEventKind? kind, IReadOnlyList<(int Id, string FullName)>? people = null)
        {
            var selected = people ?? Rows.Where(r => r.IsChecked).Select(r => (r.Id, r.FullName)).ToList();
            if (selected.Count == 0) return;

            var vm = _serviceProvider.GetRequiredService<StaffDocDialogViewModel>();
            vm.Initialize(kind, selected);
            _dialogService.ShowDialog(vm, Application.Current.MainWindow);

            await RefreshAsync();
        }
```

(Generation and manual-value saving now happen inside `StaffDocDialogViewModel.ConfirmAsync` itself, per Task 8. `RefreshAsync()` still runs unconditionally after the dialog closes — whether the user generated documents and clicked "Готово," or clicked "Скасувати" straight away, an unconditional refresh is harmless and keeps this call site simple.)

- [ ] **Step 2: Build to verify no compile errors**

Run: `dotnet build D:\GenDoc\GenDoc\GenDoc.csproj`
Expected: Build succeeded, 0 errors — this resolves the deferred errors from Task 8 Step 6.

- [ ] **Step 3: Run the full test suite**

Run: `dotnet test D:\GenDoc\GenDoc.Tests\GenDoc.Tests.csproj`
Expected: all tests pass.

- [ ] **Step 4: Manual verification**

Launch the app, open «Постійний склад», select one or more people, click "Оформити відрядження" (or "Оформити відпустку", or "Згенерувати документ"): fill dates (if applicable), check at least one template, click "Згенерувати" — confirm the dialog switches to "2. Документи" automatically, shows a summary line and one row per (person × checked template) with "Відкрити"/"Зберегти як…" buttons, that "Відкрити" opens the file, "Зберегти як…" prompts a save location and copies the file there, "← Ще один документ" returns to step 1 with the same people, and "Готово" closes the dialog and refreshes the grid. Repeat once for the "в догонку" (`GenerateDocumentAsync`) path and once for the row context-menu path (`GenerateDocumentForRowAsync`) to confirm both still work.

- [ ] **Step 5: Commit**

```bash
git add GenDoc/ViewModels/Staff/StaffViewModel.cs
git commit -m "refactor: simplify OpenDocDialogAsync now that generation lives in the dialog"
```

---

## Self-Review Notes

- **Spec coverage:** Task 1-2 (schema + model) → Task 3 (form checkbox) → Task 4 (template mapping) → Task 5-6 (filter + column) covers all of Part A exactly as scoped in brainstorming (simple bool flag; filter+column+mapping; no new quick-actions). Task 7-10 covers Part B — two-step in-dialog flow applied uniformly to all three `StaffDocDialog` scenarios, with «Відкрити»/«Зберегти як…» actions reusing the existing `IDocumentArchiveService` methods (`OpenAsync`, `SaveAsAsync`) rather than duplicating file-handling logic, and step 2 showing the full generated-document history for the selected people (via `GetCurrentRowAsync` per recipient/template pair) rather than only the current batch, matching the user's explicit "вже сформовані документи" framing from brainstorming.
- **Placeholder scan:** no TBD/TODO; every step has literal, complete code.
- **Type consistency:** `StaffDocResultRowViewModel`'s constructor signature (Task 7) matches its two call sites — the test in Task 7 Step 1 and the construction inside `ConfirmAsync` in Task 8 Step 4 — element-for-element. `StaffRowOverview`'s widened positional-record shape (Task 5) is appended-last, confirmed to not break any other construction site via the Task 5 Step 4 full-solution build. `IStaffService.GenerateDocumentsAsync`/`IssueDocumentsAsync` signatures are explicitly UNCHANGED across this plan — only their caller moves from `StaffViewModel` to `StaffDocDialogViewModel`.
