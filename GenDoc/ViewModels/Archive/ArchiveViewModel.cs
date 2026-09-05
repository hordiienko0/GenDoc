using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Documents;
using Microsoft.Win32;

namespace GenDoc.ViewModels.Archive
{
    public record FilterOption(int? Id, string Label);

    public partial class ArchiveViewModel : ObservableObject, Services.Navigation.INavigationTarget
    {
        internal const int PageSize = 200;
        private static bool _openWarningShownThisSession;

        private readonly IDocumentArchiveService _archiveService;
        private readonly IDialogService _dialogService;
        private readonly ActiveIntakeState _activeIntakeState;
        private readonly IUserSettingsService _userSettings;
        private readonly ICurrentUserContext _currentUser;
        private readonly DispatcherTimer _searchDebounceTimer;

        private readonly List<ArchiveRowViewModel> _loadedRows = new();
        private bool _initialized;
        private bool _suppressFilterReload;
        private bool _suppressHeaderCheck;

        private const string ManualTagContextKey = "archive-regenerate";

        private readonly Services.Generation.IManualTagFormBuilder _manualTagFormBuilder;
        private readonly Services.Generation.IOutputFolderService _outputFolderService;

        public ArchiveViewModel(
            IDocumentArchiveService archiveService,
            IDialogService dialogService,
            ActiveIntakeState activeIntakeState,
            Services.Generation.IManualTagFormBuilder manualTagFormBuilder,
            Services.Generation.IOutputFolderService outputFolderService,
            IUserSettingsService userSettings,
            ICurrentUserContext currentUser)
        {
            _outputFolderService = outputFolderService;
            _archiveService = archiveService;
            _dialogService = dialogService;
            _activeIntakeState = activeIntakeState;
            _manualTagFormBuilder = manualTagFormBuilder;
            _userSettings = userSettings;
            _currentUser = currentUser;

            _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _searchDebounceTimer.Tick += (_, _) =>
            {
                _searchDebounceTimer.Stop();
                FilterReload = ApplySearchAsync();
            };
        }

        internal Task ApplySearchAsync() => IsDocsTab ? ResetAndReloadAsync() : Task.CompletedTask;

        public ObservableCollection<ArchiveRowViewModel> Rows { get; } = new();
        public ObservableCollection<RunGroupViewModel> Runs { get; } = new();

        public ObservableCollection<FilterOption> IntakeOptions { get; } = new();
        public ObservableCollection<FilterOption> TemplateOptions { get; } = new();
        public ObservableCollection<FilterOption> PackageOptions { get; } = new();
        public ObservableCollection<FilterOption> AuthorOptions { get; } = new();
        public ObservableCollection<FilterOption> YearOptions { get; } = new();

        [ObservableProperty] private FilterOption? selectedIntake;
        [ObservableProperty] private FilterOption? selectedTemplate;
        [ObservableProperty] private FilterOption? selectedPackage;
        [ObservableProperty] private FilterOption? selectedAuthor;
        [ObservableProperty] private FilterOption? selectedYear;

        partial void OnSelectedIntakeChanged(FilterOption? value) => OnFilterChanged();
        partial void OnSelectedTemplateChanged(FilterOption? value) => OnFilterChanged();
        partial void OnSelectedPackageChanged(FilterOption? value) => OnFilterChanged();
        partial void OnSelectedAuthorChanged(FilterOption? value) => OnFilterChanged();
        partial void OnSelectedYearChanged(FilterOption? value) => OnFilterChanged();

        [ObservableProperty] private bool mineOnly;

        private bool _suppressMineOnlyReload;

        partial void OnMineOnlyChanged(bool value)
        {
            if (_suppressMineOnlyReload) return;
            _ = _userSettings.UpdateAsync(s => s.ArchiveMineOnly = value);

            if (IsDocsTab) _ = ResetAndReloadAsync();
            else if (IsRunsTab) _ = ReloadRunsAsync();
            else if (IsGroupTab) _ = ReloadGroupAsync();
        }

        [ObservableProperty]
        private string? searchText;

        partial void OnSearchTextChanged(string? value)
        {
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsDocsTab))]
        [NotifyPropertyChangedFor(nameof(IsRunsTab))]
        [NotifyPropertyChangedFor(nameof(IsGroupTab))]
        private int selectedTabIndex;

        public bool IsDocsTab => SelectedTabIndex == 0;
        public bool IsRunsTab => SelectedTabIndex == 1;
        public bool IsGroupTab => SelectedTabIndex == 2;

        [RelayCommand]
        private async Task SetTabAsync(string index)
        {
            if (!int.TryParse(index, out var i) || i == SelectedTabIndex) return;
            SelectedTabIndex = i;

            if (i == 0) await ResetAndReloadAsync();
            if (i == 1) await ReloadRunsAsync();
            if (i == 2) await ReloadGroupAsync();
        }

        [ObservableProperty] private string statsText = string.Empty;
        [ObservableProperty] private bool canLoadMore;
        [ObservableProperty] private bool showOpenWarning;
        [ObservableProperty] private bool isBusy;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsListEmpty))]
        [NotifyPropertyChangedFor(nameof(HasRows))]
        [NotifyPropertyChangedFor(nameof(IsArchiveEmpty))]
        [NotifyPropertyChangedFor(nameof(IsFilteredEmpty))]
        private int rowCount;

        public bool HasRows => RowCount > 0;
        public bool IsListEmpty => RowCount == 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsArchiveEmpty))]
        [NotifyPropertyChangedFor(nameof(IsFilteredEmpty))]
        private bool archiveHasAnyDocuments;

        public bool IsArchiveEmpty => IsListEmpty && !ArchiveHasAnyDocuments;
        public bool IsFilteredEmpty => IsListEmpty && ArchiveHasAnyDocuments;

        [ObservableProperty] private bool runsEmpty;

        private Task? _initialization;

        public Task InitializeAsync()
        {
            if (_initialization is { IsCompleted: false }) return _initialization;
            _initialization = InitializeCoreAsync();
            return _initialization;
        }

        private async Task InitializeCoreAsync()
        {
            if (!_initialized)
            {
                _initialized = true;
                _suppressMineOnlyReload = true;
                var settings = await _userSettings.GetForCurrentUserAsync();
                MineOnly = settings.ArchiveMineOnly;
                _suppressMineOnlyReload = false;
            }

            await ReloadFilterOptionsAsync();
            await ReloadGroupTemplateOptionsAsync();
            await ReloadActiveTabAsync();
        }

        private Task ReloadActiveTabAsync() => SelectedTabIndex switch
        {
            1 => ReloadRunsAsync(),
            2 => ReloadGroupAsync(),
            _ => ResetAndReloadAsync()
        };

        private static FilterOption Keep(
            ObservableCollection<FilterOption> options, FilterOption? previous, FilterOption? fallback = null)
            => (previous is null ? null : options.FirstOrDefault(o => o.Id == previous.Id))
               ?? fallback
               ?? options[0];

        private async Task ReloadFilterOptionsAsync()
        {
            var options = await _archiveService.GetFilterOptionsAsync();
            _suppressFilterReload = true;
            try
            {
                var previousIntake = SelectedIntake;
                var previousTemplate = SelectedTemplate;
                var previousPackage = SelectedPackage;
                var previousAuthor = SelectedAuthor;
                var previousYear = SelectedYear;

                IntakeOptions.Clear();
                IntakeOptions.Add(new FilterOption(null, "Набір: усі"));
                foreach (var (id, label) in options.Intakes)
                    IntakeOptions.Add(new FilterOption(id, label));

                TemplateOptions.Clear();
                TemplateOptions.Add(new FilterOption(null, "Шаблон: усі"));
                foreach (var (id, name) in options.Templates)
                    TemplateOptions.Add(new FilterOption(id, name));

                PackageOptions.Clear();
                PackageOptions.Add(new FilterOption(null, "Пакет: усі"));
                foreach (var (id, name) in options.Packages)
                    PackageOptions.Add(new FilterOption(id, name));

                AuthorOptions.Clear();
                AuthorOptions.Add(new FilterOption(null, "Автор: усі"));
                foreach (var (id, name) in options.Authors)
                    AuthorOptions.Add(new FilterOption(id, name));

                YearOptions.Clear();
                YearOptions.Add(new FilterOption(null, "Період: усі"));
                foreach (var year in options.Years)
                    YearOptions.Add(new FilterOption(year, year.ToString()));

                var activeIntake = _activeIntakeState.Current?.Id is int aid
                    ? IntakeOptions.FirstOrDefault(o => o.Id == aid)
                    : null;
                SelectedIntake = Keep(IntakeOptions, previousIntake, activeIntake);
                SelectedTemplate = Keep(TemplateOptions, previousTemplate);
                SelectedPackage = Keep(PackageOptions, previousPackage);
                SelectedAuthor = Keep(AuthorOptions, previousAuthor);
                SelectedYear = Keep(YearOptions, previousYear);
            }
            finally
            {
                _suppressFilterReload = false;
            }
        }

        internal Task FilterReload { get; private set; } = Task.CompletedTask;

        private void OnFilterChanged()
        {
            if (_suppressFilterReload) return;
            FilterReload = ReloadActiveTabAsync();
        }

        private ArchiveFilter BuildFilter(int skip, bool withSearch = true) => new(
            SelectedIntake?.Id, SelectedTemplate?.Id, SelectedPackage?.Id,
            MineOnly ? _currentUser.CurrentUserId : SelectedAuthor?.Id,
            SelectedYear?.Id, skip, PageSize,
            withSearch ? SearchNormalization.PrepareQuery(SearchText) : null);

        private bool IsSearchActive => SearchNormalization.PrepareQuery(SearchText) is not null;

        private async Task ResetAndReloadAsync()
        {
            ClearChecked();
            _loadedRows.Clear();
            await LoadPageAsync();
            await ReloadStatsAsync();
        }

        private async Task LoadPageAsync()
        {
            IsBusy = true;
            try
            {
                var page = await _archiveService.QueryAsync(BuildFilter(_loadedRows.Count));
                foreach (var dto in page)
                {
                    var row = new ArchiveRowViewModel(dto);
                    row.PropertyChanged += OnRowPropertyChanged;
                    _loadedRows.Add(row);
                }
                CanLoadMore = page.Count == PageSize;
                ShowLoadedRows();
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private Task LoadMoreAsync() => LoadPageAsync();

        private async Task ReloadStatsAsync()
        {
            var stats = await _archiveService.GetStatsAsync(BuildFilter(0));
            var size = $"{stats.TotalBytes / 1024.0 / 1024.0:0.#} МБ";
            if (IsSearchActive)
            {
                var total = await _archiveService.GetStatsAsync(BuildFilter(0, withSearch: false));
                StatsText = $"показано {stats.Count} з {total.Count} {(total.Count == 1 ? "документа" : "документів")} · {size}";
            }
            else
            {
                StatsText = $"{stats.Count} {Documents(stats.Count)} · {size}";
            }

            ArchiveHasAnyDocuments = stats.Count > 0
                || _loadedRows.Count > 0
                || (await _archiveService.GetStatsAsync(new ArchiveFilter(null, null, null, null, null, 0, 1))).Count > 0;
        }

        private void ShowLoadedRows()
        {
            Rows.Clear();
            foreach (var row in _loadedRows) Rows.Add(row);
            RowCount = Rows.Count;
            RefreshCheckedState();
        }

        [RelayCommand]
        private void ResetFilters()
        {
            _suppressFilterReload = true;
            SelectedIntake = IntakeOptions.FirstOrDefault();
            SelectedTemplate = TemplateOptions.FirstOrDefault();
            SelectedPackage = PackageOptions.FirstOrDefault();
            SelectedAuthor = AuthorOptions.FirstOrDefault();
            SelectedYear = YearOptions.FirstOrDefault();
            SearchText = null;
            _searchDebounceTimer.Stop();

            if (MineOnly)
            {
                _suppressMineOnlyReload = true;
                MineOnly = false;
                _suppressMineOnlyReload = false;
                _ = _userSettings.UpdateAsync(s => s.ArchiveMineOnly = false);
            }

            _suppressFilterReload = false;
            FilterReload = ReloadActiveTabAsync();
        }

        private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ArchiveRowViewModel.IsChecked) && !_suppressHeaderCheck)
                RefreshCheckedState();
        }

        public bool? HeaderChecked
        {
            get
            {
                if (Rows.Count == 0 || CheckedCount == 0) return false;
                if (CheckedCount == Rows.Count) return true;
                return null;
            }
            set
            {
                if (_suppressHeaderCheck) return;
                var target = value == true;
                _suppressHeaderCheck = true;
                foreach (var row in Rows) row.IsChecked = target;
                _suppressHeaderCheck = false;
                RefreshCheckedState();
            }
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanOpen))]
        [NotifyPropertyChangedFor(nameof(CanSaveAs))]
        [NotifyPropertyChangedFor(nameof(CanRegenerate))]
        [NotifyPropertyChangedFor(nameof(CanUpload))]
        [NotifyPropertyChangedFor(nameof(CanAttach))]
        [NotifyPropertyChangedFor(nameof(CanHistory))]
        [NotifyPropertyChangedFor(nameof(CanDelete))]
        [NotifyPropertyChangedFor(nameof(CanPrint))]
        [NotifyPropertyChangedFor(nameof(ShowInFolderTooltip))]
        [NotifyPropertyChangedFor(nameof(OpenTooltip))]
        [NotifyPropertyChangedFor(nameof(SaveAsTooltip))]
        [NotifyPropertyChangedFor(nameof(RegenerateTooltip))]
        [NotifyPropertyChangedFor(nameof(SingleSelectionTooltip))]
        private int checkedCount;

        private List<ArchiveRowViewModel> CheckedRows => Rows.Where(r => r.IsChecked).ToList();

        private void RefreshCheckedState()
        {
            CheckedCount = Rows.Count(r => r.IsChecked);
            OnPropertyChanged(nameof(HeaderChecked));
            _ = RefreshDiskPathAsync();
        }

        public void ClearChecked()
        {
            _suppressHeaderCheck = true;
            foreach (var row in _loadedRows) row.IsChecked = false;
            _suppressHeaderCheck = false;
            RefreshCheckedState();
        }

        public void HandleRowClick(ArchiveRowViewModel row, bool ctrl)
        {
            _suppressHeaderCheck = true;
            if (!ctrl)
            {
                foreach (var other in Rows.Where(r => r.IsChecked && r != row))
                    other.IsChecked = false;
                row.IsChecked = true;
            }
            else
            {
                row.IsChecked = !row.IsChecked;
            }
            _suppressHeaderCheck = false;
            RefreshCheckedState();
        }

        private static string Documents(int count) => PluralHelper.Pluralize(count, "документ", "документи", "документів");
        private static string Sheets(int count) => PluralHelper.Pluralize(count, "відомість", "відомості", "відомостей");

        internal static string DeleteConfirmationText(int count, int version, int? previousVersion)
        {
            if (count == 1 && previousVersion is int previous)
                return $"Буде видалено в.{version}; актуальною стане в.{previous}.";
            return $"Перемістити {count} {Documents(count)} у кошик?";
        }

        internal static string DeleteGroupConfirmationText(int count)
            => $"Перемістити {count} {Sheets(count)} у кошик?";

        public bool CanOpen => CheckedCount == 1 && CheckedRows.All(r => r.HasContent);
        public bool CanSaveAs => CheckedCount >= 1 && CheckedRows.All(r => r.HasContent);
        public bool CanRegenerate => CheckedCount >= 1
            && CheckedRows.All(r => r.TemplateAlive && r.RecipientAlive);
        public bool CanUpload => CheckedCount == 1;
        public bool CanAttach => CheckedCount == 1;
        public bool CanHistory => CheckedCount == 1;
        public bool CanDelete => CheckedCount >= 1;

        public bool CanPrint => CheckedCount >= 1 && CheckedRows.All(r => r.HasContent);

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanShowInFolder))]
        [NotifyPropertyChangedFor(nameof(ShowInFolderTooltip))]
        private string? checkedRowDiskPath;

        public bool CanShowInFolder => CheckedRowDiskPath is not null;
        public string? ShowInFolderTooltip => CheckedCount != 1
            ? "Оберіть один документ"
            : CanShowInFolder ? CheckedRowDiskPath : "Файл не збережено на диску в типовій теці";

        private async Task RefreshDiskPathAsync()
        {
            var row = CheckedCount == 1 ? CheckedRows.FirstOrDefault() : null;
            CheckedRowDiskPath = row is null ? null : await _outputFolderService.ResolveOnDiskAsync(row.Dto.FileName);
        }

        [RelayCommand]
        private async Task PrintAsync()
        {
            var rows = CheckedRows.Where(r => r.HasContent).ToList();
            if (rows.Count > 1)
            {
                var confirm = MessageBox.Show(
                    $"Надрукувати {rows.Count} {Documents(rows.Count)}? Документи підуть на друк по черзі.",
                    "Друк", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.OK) return;
            }

            IsBusy = true;
            try
            {
                foreach (var row in rows)
                {
                    try
                    {
                        var result = await _archiveService.PrintAsync(row.Id);
                        if (!result.Success)
                        {
                            MessageBox.Show(result.ErrorMessage, "Друк", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        ShowWarning(result, "Друк", row.ShortName);
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        MessageBox.Show("Не вдалося надрукувати: немає програми для цього типу файлу.", "Друк",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static void ShowWarning(ArchiveOpResult result, string title, string? subject = null)
        {
            if (result.Warning is null) return;
            var text = subject is null ? result.Warning : $"{subject}: {result.Warning}";
            MessageBox.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        [RelayCommand]
        private void ShowInFolder()
        {
            if (CheckedRowDiskPath is null) return;
            try { System.Diagnostics.Process.Start("explorer.exe", ShellCommands.ExplorerSelectArguments(CheckedRowDiskPath)); } catch { }
        }

        public string? SingleSelectionTooltip => CheckedCount == 1 ? null : "Оберіть один документ";
        public string? OpenTooltip => CheckedCount != 1
            ? "Оберіть один документ"
            : CanOpen ? null : "Файл не збережено - доступні Перегенерувати й Підгрузити";
        public string? SaveAsTooltip => CheckedCount == 0
            ? "Оберіть документи"
            : CanSaveAs ? null : "Серед обраних є документи без збереженого файлу";
        public string? RegenerateTooltip => CheckedCount == 0
            ? "Оберіть документи"
            : CanRegenerate ? null : "Перегенерація можлива лише для документів з живим шаблоном і особою не в кошику";

        [RelayCommand]
        private async Task OpenAsync()
        {
            var row = CheckedRows.FirstOrDefault();
            if (row is null || !CanOpen) return;
            await OpenByRowAsync(row);
        }

        [RelayCommand]
        private Task OpenCheckedAsync() => IsGroupTab ? OpenGroupAsync() : IsDocsTab ? OpenAsync() : Task.CompletedTask;

        [RelayCommand]
        private Task DeleteCheckedAsync() => IsGroupTab ? DeleteGroupAsync() : IsDocsTab ? DeleteAsync() : Task.CompletedTask;

        public async Task OpenByRowAsync(ArchiveRowViewModel row)
        {
            if (!row.HasContent) return;
            try
            {
                var result = await _archiveService.OpenAsync(row.Id);
                if (!result.Success)
                {
                    MessageBox.Show(result.ErrorMessage, "Відкриття документа",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                ShowWarning(result, "Відкриття документа");
                if (!_openWarningShownThisSession)
                {
                    _openWarningShownThisSession = true;
                    ShowOpenWarning = true;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                MessageBox.Show(
                    "Не вдалося відкрити: немає програми для .docx. Скористайтесь «Зберегти як…».",
                    "Відкриття документа", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        private void CloseOpenWarning() => ShowOpenWarning = false;

        [RelayCommand]
        private async Task SaveAsAsync()
        {
            var rows = CheckedRows;
            if (rows.Count == 0 || !CanSaveAs) return;

            if (rows.Count == 1)
            {
                var dialog = new SaveFileDialog { FileName = System.IO.Path.GetFileName(rows[0].Dto.FileName) };
                if (dialog.ShowDialog() != true) return;

                IsBusy = true;
                try
                {
                    var result = await _archiveService.SaveAsAsync(rows[0].Id, dialog.FileName);
                    if (!result.Success)
                        MessageBox.Show(result.ErrorMessage, "Зберегти як",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    else ShowWarning(result, "Зберегти як");
                }
                finally { IsBusy = false; }
                return;
            }

            var folderDialog = new OpenFolderDialog { Title = "Папка для експорту" };
            if (folderDialog.ShowDialog() != true) return;

            IsBusy = true;
            try
            {
                var (saved, errors) = await _archiveService.SaveManyAsync(
                    rows.Select(r => r.Id).ToList(), folderDialog.FolderName);

                var message = $"Збережено {saved} {Documents(saved)} у {folderDialog.FolderName}";
                if (errors.Count > 0)
                    message += $"\nПомилок: {errors.Count}\n{string.Join("\n", errors.Take(5))}";
                MessageBox.Show(message, "Експорт завершено", MessageBoxButton.OK,
                    errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task RegenerateAsync()
        {
            var rows = CheckedRows;
            if (rows.Count == 0 || !CanRegenerate) return;

            var confirm = MessageBox.Show(
                "Буде створено версію N+1. Попередня залишиться в архіві.",
                "Перегенерація", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.OK) return;

            var manualTags = await _archiveService.GetManualTagsAsync(
                rows.Select(r => r.TemplateId).Distinct().ToList());
            var manualValues = new Dictionary<string, string>();
            if (manualTags.Count > 0)
            {
                var form = await _manualTagFormBuilder.BuildAsync(manualTags, ManualTagContextKey);
                var dialog = new ManualValuesDialogViewModel(form);
                if (_dialogService.ShowDialog(dialog, Application.Current.MainWindow) != true) return;

                manualValues = dialog.GetValues();
                await _manualTagFormBuilder.SaveAsync(ManualTagContextKey, form);
            }

            IsBusy = true;
            var done = 0;
            var errors = new List<string>();
            try
            {
                foreach (var row in rows)
                {
                    try
                    {
                        var result = await _archiveService.RegenerateAsync(row.Id, manualValues);
                        if (result.Success) done++;
                        else errors.Add($"{row.ShortName}: {result.ErrorMessage}");
                    }
                    catch (Exception ex)
                    {
                        ErrorLog.Write(ex, _currentUser.CurrentUserFullName);
                        errors.Add($"{row.ShortName}: {ex.Message}");
                    }
                }
            }
            finally
            {
                IsBusy = false;
            }

            var summary = $"Згенеровано: {done}";
            if (errors.Count > 0) summary += $"\nПомилок: {errors.Count}\n{string.Join("\n", errors.Take(5))}";
            MessageBox.Show(summary, "Перегенерація завершена", MessageBoxButton.OK,
                errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

            await RefreshRowsAsync(rows);
            await ReloadStatsAsync();
        }

        private async Task RefreshRowsAsync(IEnumerable<ArchiveRowViewModel> rows)
        {
            foreach (var row in rows)
            {
                var fresh = await _archiveService.GetCurrentRowAsync(row.RecipientId, row.TemplateId);
                if (fresh is not null) row.UpdateFrom(fresh);
            }
        }

        [RelayCommand]
        private async Task UploadAsync()
        {
            var row = CheckedRows.FirstOrDefault();
            if (row is null || !CanUpload) return;

            var fileDialog = new OpenFileDialog
            {
                Filter = "Документи (*.docx;*.pdf)|*.docx;*.pdf"
            };
            if (fileDialog.ShowDialog() != true) return;

            var noteDialog = new NoteInputDialogViewModel(
                "Підгрузити версію", System.IO.Path.GetFileName(fileDialog.FileName));
            if (_dialogService.ShowDialog(noteDialog, Application.Current.MainWindow) != true) return;

            IsBusy = true;
            try
            {
                var result = await _archiveService.UploadManualAsync(row.Id, fileDialog.FileName, noteDialog.Note);
                if (!result.Success)
                {
                    MessageBox.Show(result.ErrorMessage, "Підгрузити версію",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            finally
            {
                IsBusy = false;
            }

            await RefreshRowsAsync(new[] { row });
            await ReloadStatsAsync();
        }

        [RelayCommand]
        private async Task AttachAsync()
        {
            var row = CheckedRows.FirstOrDefault();
            if (row is null || !CanAttach) return;

            var fileDialog = new OpenFileDialog
            {
                Filter = "Скани (*.pdf;*.jpg;*.png)|*.pdf;*.jpg;*.png"
            };
            if (fileDialog.ShowDialog() != true) return;

            var noteDialog = new NoteInputDialogViewModel(
                "Прикріпити скан", System.IO.Path.GetFileName(fileDialog.FileName));
            if (_dialogService.ShowDialog(noteDialog, Application.Current.MainWindow) != true) return;

            IsBusy = true;
            try
            {
                var result = await _archiveService.AttachAsync(row.Id, fileDialog.FileName, noteDialog.Note);
                if (!result.Success)
                {
                    MessageBox.Show(result.ErrorMessage, "Прикріпити скан",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex, _currentUser.CurrentUserFullName);
                MessageBox.Show($"Не вдалося прикріпити скан: {ex.Message}", "Прикріпити скан",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            finally
            {
                IsBusy = false;
            }

            await RefreshRowsAsync(new[] { row });
        }

        [RelayCommand]
        private async Task HistoryAsync()
        {
            var row = CheckedRows.FirstOrDefault();
            if (row is null || !CanHistory) return;

            var vm = new VersionHistoryViewModel(
                _archiveService, row.RecipientId, row.TemplateId, row.Id,
                row.ShortName, row.Dto.TemplateName);
            await vm.InitializeAsync();
            _dialogService.ShowDialog(vm, Application.Current.MainWindow);

            if (vm.HasChanges)
                await RefreshRowsAsync(new[] { row });
        }

        [RelayCommand]
        private async Task DeleteAsync()
        {
            var rows = CheckedRows;
            if (rows.Count == 0) return;

            var ids = rows.Select(r => r.Id).ToList();
            var totalVersions = await _archiveService.CountVersionsAsync(ids);

            int? previousVersion = null;
            if (rows.Count == 1 && totalVersions > 1)
            {
                previousVersion = (await _archiveService.GetVersionsAsync(rows[0].RecipientId, rows[0].TemplateId))
                    .Where(v => v.Id != rows[0].Id)
                    .OrderByDescending(v => v.Version)
                    .Select(v => (int?)v.Version)
                    .FirstOrDefault();
            }

            var message = DeleteConfirmationText(rows.Count, rows[0].Version, previousVersion);
            if (rows.Count > 1 && totalVersions > rows.Count)
                message += $"\nУ {rows.Count} обраних є попередні версії - вони стануть актуальними.";

            var dialog = new DeleteDocumentsDialogViewModel(message, totalVersions, rows.Count);
            if (_dialogService.ShowDialog(dialog, Application.Current?.MainWindow) != true) return;

            await _archiveService.DeleteAsync(ids, dialog.DeleteAllVersions);
            await ResetAndReloadAsync();
        }

        [RelayCommand]
        private void GoToGeneration()
            => WeakReferenceMessenger.Default.Send(
                new Services.Navigation.NavigateToSectionMessage(Shell.MainViewModel.GenerationSectionTitle, null));

        public async Task ApplyNavigationPayloadAsync(object payload)
        {
            if (payload is not Services.Navigation.ArchiveRunNavigationPayload nav) return;
            await InitializeAsync();

            var target = (await _archiveService.GetRunsAsync(null, null)).FirstOrDefault(r => r.Id == nav.RunId);
            _suppressFilterReload = true;
            try
            {
                SelectedIntake = IntakeOptions.FirstOrDefault(o => o.Id == target?.IntakeId)
                                 ?? IntakeOptions.FirstOrDefault(o => o.Id is null);
                SelectedYear = YearOptions.FirstOrDefault(o => o.Id == target?.RunAt.Year)
                               ?? YearOptions.FirstOrDefault(o => o.Id is null);
            }
            finally
            {
                _suppressFilterReload = false;
            }

            SelectedTabIndex = 1;
            await ReloadRunsAsync();
            var run = Runs.FirstOrDefault(r => r.Id == nav.RunId);
            if (run is not null && !run.IsExpanded) await ToggleRunAsync(run);
        }

        private async Task ReloadRunsAsync()
        {
            var expandedId = Runs.FirstOrDefault(r => r.IsExpanded)?.Id;
            var runs = await _archiveService.GetRunsAsync(
                SelectedIntake?.Id, SelectedYear?.Id, MineOnly ? _currentUser.CurrentUserId : null);
            Runs.Clear();
            foreach (var run in runs)
                Runs.Add(new RunGroupViewModel(run));
            RunsEmpty = Runs.Count == 0;

            if (expandedId is int id && Runs.FirstOrDefault(r => r.Id == id) is { } expanded)
                await ToggleRunAsync(expanded);
        }

        [RelayCommand]
        private async Task ToggleRunAsync(RunGroupViewModel? run)
        {
            if (run is null) return;

            if (run.IsExpanded)
            {
                run.IsExpanded = false;
                return;
            }

            foreach (var other in Runs.Where(r => r.IsExpanded))
                other.IsExpanded = false;

            if (!run.ItemsLoaded)
            {
                var items = await _archiveService.GetRunItemsAsync(run.Id);
                run.Items.Clear();
                foreach (var item in items)
                    run.Items.Add(new RunItemRowViewModel(item));
                run.ItemsLoaded = true;
            }

            run.IsExpanded = true;
        }

        [RelayCommand]
        private async Task OpenRunItemAsync(RunItemRowViewModel? item)
        {
            if (item?.Dto.DocumentId is not int docId || !item.CanOpen) return;
            try
            {
                var result = item.IsGroup
                    ? await _archiveService.OpenGroupAsync(docId)
                    : await _archiveService.OpenAsync(docId);
                if (!result.Success)
                    MessageBox.Show(result.ErrorMessage, "Відкриття документа",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                else ShowWarning(result, "Відкриття документа");
            }
            catch (System.ComponentModel.Win32Exception)
            {
                MessageBox.Show(
                    "Не вдалося відкрити: немає програми для цього типу файлу. Скористайтесь «Зберегти як…».",
                    "Відкриття документа", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        private async Task SaveRunItemAsAsync(RunItemRowViewModel? item)
        {
            if (item?.Dto.DocumentId is not int docId || !item.CanOpen) return;

            var dialog = new SaveFileDialog { FileName = System.IO.Path.GetFileName(item.Dto.FileName) };
            if (dialog.ShowDialog() != true) return;

            var result = item.IsGroup
                ? await _archiveService.SaveGroupAsAsync(docId, dialog.FileName)
                : await _archiveService.SaveAsAsync(docId, dialog.FileName);
            if (!result.Success)
                MessageBox.Show(result.ErrorMessage, "Зберегти як",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            else ShowWarning(result, "Зберегти як");
        }

        public ObservableCollection<GroupDocumentRowViewModel> GroupRows { get; } = new();

        public ObservableCollection<GroupTemplateOption> GroupTemplateOptions { get; } = new();

        [ObservableProperty]
        private GroupTemplateOption? selectedGroupTemplate;

        partial void OnSelectedGroupTemplateChanged(GroupTemplateOption? value) => _ = ReloadGroupAsync();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(GroupIsEmpty))]
        [NotifyPropertyChangedFor(nameof(GroupHasRows))]
        private int groupRowCount;

        public bool GroupIsEmpty => GroupRowCount == 0;
        public bool GroupHasRows => GroupRowCount > 0;

        private bool _suppressGroupFilterReload;

        private async Task ReloadGroupTemplateOptionsAsync()
        {
            var options = await _archiveService.GetGroupTemplateOptionsAsync();
            var previous = SelectedGroupTemplate;
            _suppressGroupFilterReload = true;
            try
            {
                GroupTemplateOptions.Clear();
                GroupTemplateOptions.Add(new GroupTemplateOption(null, null, "Шаблон: усі"));
                foreach (var option in options)
                    GroupTemplateOptions.Add(option);
                SelectedGroupTemplate =
                    (previous is null
                        ? null
                        : GroupTemplateOptions.FirstOrDefault(o =>
                            o.ExportTemplateId == previous.ExportTemplateId && o.DocxTemplateId == previous.DocxTemplateId))
                    ?? GroupTemplateOptions[0];
            }
            finally
            {
                _suppressGroupFilterReload = false;
            }
        }

        [ObservableProperty] private bool groupCanLoadMore;

        private async Task ReloadGroupAsync()
        {
            if (_suppressGroupFilterReload) return;

            if (GroupTemplateOptions.Count == 0) await ReloadGroupTemplateOptionsAsync();

            ClearGroupChecked();
            GroupRows.Clear();
            GroupRowCount = 0;
            await LoadGroupPageAsync();
        }

        private async Task LoadGroupPageAsync()
        {
            IsBusy = true;
            try
            {
                var rows = await _archiveService.QueryGroupAsync(new GroupArchiveFilter(
                    SelectedGroupTemplate?.ExportTemplateId, SelectedGroupTemplate?.DocxTemplateId, SelectedYear?.Id,
                    GroupRows.Count, PageSize,
                    MineOnly ? _currentUser.CurrentUserId : null, SelectedIntake?.Id));
                foreach (var dto in rows)
                {
                    var row = new GroupDocumentRowViewModel(dto);
                    row.PropertyChanged += OnGroupRowPropertyChanged;
                    GroupRows.Add(row);
                }
                GroupCanLoadMore = rows.Count == PageSize;
                GroupRowCount = GroupRows.Count;
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private Task LoadMoreGroupAsync() => LoadGroupPageAsync();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanOpenGroup))]
        [NotifyPropertyChangedFor(nameof(CanSaveGroupAs))]
        [NotifyPropertyChangedFor(nameof(CanHistoryGroup))]
        [NotifyPropertyChangedFor(nameof(CanDeleteGroup))]
        [NotifyPropertyChangedFor(nameof(CanPrintGroup))]
        [NotifyPropertyChangedFor(nameof(CanShowGroupParticipants))]
        private int groupCheckedCount;

        private bool _suppressGroupHeaderCheck;

        private void OnGroupRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GroupDocumentRowViewModel.IsChecked) && !_suppressGroupHeaderCheck)
            {
                GroupCheckedCount = GroupRows.Count(r => r.IsChecked);
                _ = RefreshGroupDiskPathAsync();
            }
        }

        private List<GroupDocumentRowViewModel> CheckedGroupRows => GroupRows.Where(r => r.IsChecked).ToList();

        public void HandleGroupRowClick(GroupDocumentRowViewModel row, bool ctrl)
        {
            _suppressGroupHeaderCheck = true;
            if (!ctrl)
            {
                foreach (var other in GroupRows.Where(r => r.IsChecked && r != row))
                    other.IsChecked = false;
                row.IsChecked = true;
            }
            else
            {
                row.IsChecked = !row.IsChecked;
            }
            _suppressGroupHeaderCheck = false;
            GroupCheckedCount = GroupRows.Count(r => r.IsChecked);
            _ = RefreshGroupDiskPathAsync();
        }

        private void ClearGroupChecked()
        {
            _suppressGroupHeaderCheck = true;
            foreach (var row in GroupRows) row.IsChecked = false;
            _suppressGroupHeaderCheck = false;
            GroupCheckedCount = 0;
        }

        public bool CanOpenGroup => GroupCheckedCount == 1 && CheckedGroupRows.All(r => r.HasContent);
        public bool CanSaveGroupAs => GroupCheckedCount >= 1 && CheckedGroupRows.All(r => r.HasContent);
        public bool CanHistoryGroup => GroupCheckedCount == 1;
        public bool CanDeleteGroup => GroupCheckedCount >= 1;

        public bool CanPrintGroup => GroupCheckedCount >= 1 && CheckedGroupRows.All(r => r.HasContent);

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanShowGroupInFolder))]
        [NotifyPropertyChangedFor(nameof(ShowGroupInFolderTooltip))]
        private string? checkedGroupDiskPath;

        public bool CanShowGroupInFolder => CheckedGroupDiskPath is not null;
        public string? ShowGroupInFolderTooltip => GroupCheckedCount != 1
            ? "Оберіть одну відомість"
            : CanShowGroupInFolder ? CheckedGroupDiskPath : "Файл не збережено на диску в типовій теці";

        private async Task RefreshGroupDiskPathAsync()
        {
            var row = GroupCheckedCount == 1 ? CheckedGroupRows.FirstOrDefault() : null;
            CheckedGroupDiskPath = row is null ? null : await _outputFolderService.ResolveOnDiskAsync(row.Dto.FileName);
        }

        [RelayCommand]
        private async Task PrintGroupAsync()
        {
            IsBusy = true;
            try
            {
                foreach (var row in CheckedGroupRows.Where(r => r.HasContent))
                {
                    try
                    {
                        var result = await _archiveService.PrintGroupAsync(row.Id);
                        if (!result.Success)
                        {
                            MessageBox.Show(result.ErrorMessage, "Друк", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        ShowWarning(result, "Друк", row.Dto.TemplateName);
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        MessageBox.Show("Не вдалося надрукувати: немає програми для цього типу файлу.", "Друк",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        public bool CanShowGroupParticipants => GroupCheckedCount == 1;

        [RelayCommand]
        private async Task ShowGroupParticipantsAsync()
        {
            var row = CheckedGroupRows.FirstOrDefault();
            if (row is null) return;
            var participants = await _archiveService.GetGroupParticipantsAsync(row.Id);
            var vm = new GroupParticipantsViewModel(
                $"{row.Dto.TemplateName} · в.{row.Dto.Version}", participants);
            _dialogService.ShowDialog(vm, Application.Current.MainWindow);
        }

        [RelayCommand]
        private void ShowGroupInFolder()
        {
            if (CheckedGroupDiskPath is null) return;
            try { System.Diagnostics.Process.Start("explorer.exe", ShellCommands.ExplorerSelectArguments(CheckedGroupDiskPath)); } catch { }
        }

        [RelayCommand]
        private async Task OpenGroupAsync()
        {
            var row = CheckedGroupRows.FirstOrDefault();
            if (row is null || !CanOpenGroup) return;

            try
            {
                var result = await _archiveService.OpenGroupAsync(row.Id);
                if (!result.Success)
                    MessageBox.Show(result.ErrorMessage, "Відкриття документа",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                else ShowWarning(result, "Відкриття документа");
            }
            catch (System.ComponentModel.Win32Exception)
            {
                MessageBox.Show(
                    "Не вдалося відкрити: немає програми для .xlsx. Скористайтесь «Зберегти як…».",
                    "Відкриття документа", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        private async Task SaveGroupAsAsync()
        {
            var rows = CheckedGroupRows;
            if (rows.Count == 0 || !CanSaveGroupAs) return;

            if (rows.Count == 1)
            {
                var dialog = new SaveFileDialog { FileName = System.IO.Path.GetFileName(rows[0].Dto.FileName) };
                if (dialog.ShowDialog() != true) return;

                IsBusy = true;
                try
                {
                    var result = await _archiveService.SaveGroupAsAsync(rows[0].Id, dialog.FileName);
                    if (!result.Success)
                        MessageBox.Show(result.ErrorMessage, "Зберегти як",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                finally { IsBusy = false; }
                return;
            }

            var folderDialog = new OpenFolderDialog { Title = "Папка для експорту" };
            if (folderDialog.ShowDialog() != true) return;

            IsBusy = true;
            try
            {
                var saved = 0;
                var errors = new List<string>();
                foreach (var row in rows)
                {
                    var result = await _archiveService.SaveGroupAsAsync(
                        row.Id, System.IO.Path.Combine(folderDialog.FolderName, row.Dto.FileName));
                    if (result.Success) saved++;
                    else errors.Add($"{row.Dto.FileName}: {result.ErrorMessage}");
                }

                var message = $"Збережено {saved} {Sheets(saved)} у {folderDialog.FolderName}";
                if (errors.Count > 0)
                    message += $"\nПомилок: {errors.Count}\n{string.Join("\n", errors.Take(5))}";
                MessageBox.Show(message, "Експорт завершено", MessageBoxButton.OK,
                    errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task HistoryGroupAsync()
        {
            var row = CheckedGroupRows.FirstOrDefault();
            if (row is null || !CanHistoryGroup) return;

            var vm = new GroupVersionHistoryViewModel(
                _archiveService,
                row.ExportTemplateId == 0 ? null : row.ExportTemplateId,
                row.Dto.DocxTemplateId,
                row.Dto.IntakeId,
                row.Dto.TemplateName);
            await vm.InitializeAsync();
            _dialogService.ShowDialog(vm, Application.Current.MainWindow);

            if (vm.HasChanges) await ReloadGroupAsync();
        }

        [RelayCommand]
        private async Task DeleteGroupAsync()
        {
            var rows = CheckedGroupRows;
            if (rows.Count == 0) return;

            var confirm = MessageBox.Show(
                DeleteGroupConfirmationText(rows.Count),
                "Видалення відомостей", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            await _archiveService.DeleteGroupAsync(rows.Select(r => r.Id).ToList());
            await ReloadGroupAsync();
        }
    }
}
