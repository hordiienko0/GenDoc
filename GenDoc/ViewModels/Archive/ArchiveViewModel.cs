using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Documents;
using Microsoft.Win32;

namespace GenDoc.ViewModels.Archive
{
    public record FilterOption(int? Id, string Label);

    // Singleton: фільтри й сторінка живуть між перемиканнями розділів.
    public partial class ArchiveViewModel : ObservableObject
    {
        private const int PageSize = 200;
        private static readonly CompareInfo UkCompare = CultureInfo.GetCultureInfo("uk-UA").CompareInfo;
        private static bool _openWarningShownThisSession;

        private readonly IDocumentArchiveService _archiveService;
        private readonly IDialogService _dialogService;
        private readonly ActiveIntakeState _activeIntakeState;
        private readonly DispatcherTimer _searchDebounceTimer;

        private readonly List<ArchiveRowViewModel> _loadedRows = new();
        private bool _initialized;
        private bool _suppressFilterReload;
        private bool _suppressHeaderCheck;

        /// <summary>Ключ для запам'ятовування минулих значень і підписанта.
        /// Окремий від генерації: перегенерація з архіву — свій контекст.</summary>
        private const string ManualTagContextKey = "archive-regenerate";

        private readonly Services.Generation.IManualTagFormBuilder _manualTagFormBuilder;

        public ArchiveViewModel(
            IDocumentArchiveService archiveService,
            IDialogService dialogService,
            ActiveIntakeState activeIntakeState,
            Services.Generation.IManualTagFormBuilder manualTagFormBuilder)
        {
            _archiveService = archiveService;
            _dialogService = dialogService;
            _activeIntakeState = activeIntakeState;
            _manualTagFormBuilder = manualTagFormBuilder;

            _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _searchDebounceTimer.Tick += (_, _) =>
            {
                _searchDebounceTimer.Stop();
                ApplySearch();
            };
        }

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
        // IsArchiveEmpty/IsFilteredEmpty теж читають RowCount, тож без цих двох
        // сповіщень напис «Нічого не знайдено» лишався поверх уже завантажених рядків.
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

        public async Task InitializeAsync()
        {
            if (_initialized)
            {
                await ResetAndReloadAsync();
                return;
            }
            _initialized = true;

            await ReloadFilterOptionsAsync();
            await ResetAndReloadAsync();
        }

        private async Task ReloadFilterOptionsAsync()
        {
            var options = await _archiveService.GetFilterOptionsAsync();
            _suppressFilterReload = true;

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

            // Дефолт — активний набір, якщо він є.
            var activeIntakeId = _activeIntakeState.Current?.Id;
            SelectedIntake = activeIntakeId is int aid
                ? IntakeOptions.FirstOrDefault(o => o.Id == aid) ?? IntakeOptions[0]
                : IntakeOptions[0];
            SelectedTemplate = TemplateOptions[0];
            SelectedPackage = PackageOptions[0];
            SelectedAuthor = AuthorOptions[0];
            SelectedYear = YearOptions[0];

            _suppressFilterReload = false;
        }

        private void OnFilterChanged()
        {
            if (_suppressFilterReload) return;
            _ = ResetAndReloadAsync();
            if (IsRunsTab) _ = ReloadRunsAsync();
        }

        private ArchiveFilter BuildFilter(int skip) => new(
            SelectedIntake?.Id, SelectedTemplate?.Id, SelectedPackage?.Id,
            SelectedAuthor?.Id, SelectedYear?.Id, skip, PageSize, SelectedFolderPath);

        /// <summary>Дерево папок ліворуч. Порожній рядок = «Усі документи».</summary>
        public ObservableCollection<ArchiveFolderNodeViewModel> FolderTree { get; } = new();

        [ObservableProperty] private string? selectedFolderPath;

        [ObservableProperty] private string folderScopeText = "Усі документи";

        public const double FolderPanelMinWidth = 140;
        public const double FolderPanelMaxWidth = 480;
        private const double CollapsedFolderPanelWidth = 46;

        /// <summary>Колонка панелі ЗАВЖДИ явна, ніколи не Auto — та сама причина,
        /// що й у конструкторі шаблонів: Auto міряється нескінченністю, і довга
        /// назва папки роздула б панель за край вікна.</summary>
        public GridLength FolderPanelColumnWidth =>
            new(IsFolderPanelCollapsed ? CollapsedFolderPanelWidth : FolderPanelWidth);

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FolderPanelColumnWidth))]
        private double folderPanelWidth = 200;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FolderPanelColumnWidth))]
        [NotifyPropertyChangedFor(nameof(IsFolderPanelExpanded))]
        private bool isFolderPanelCollapsed = true;

        /// <summary>Зустрічна властивість замість конвертера-інвертора: у темі
        /// його немає, а заводити цілий конвертер заради одного місця — зайве.</summary>
        public bool IsFolderPanelExpanded => !IsFolderPanelCollapsed;

        // Щойно оператор сам чіпнув панель, автоматика більше не втручається:
        // інакше його вибір скидався б після кожного перезавантаження списку.
        private bool _folderPanelTouchedByUser;

        [RelayCommand]
        private void ToggleFolderPanel()
        {
            _folderPanelTouchedByUser = true;
            IsFolderPanelCollapsed = !IsFolderPanelCollapsed;
        }

        /// <summary>Дерево з єдиного вузла нічого не дає, а панель забирає ширину
        /// в таблиці — тож доки гілка одна, панель складена. Кнопка розгортання
        /// лишається: нічого не зникає, лише не заважає.</summary>
        private void AutoCollapseTrivialTree()
        {
            if (_folderPanelTouchedByUser) return;
            IsFolderPanelCollapsed = FolderTree.Count <= 1;
        }

        [RelayCommand]
        private async Task SelectFolderAsync(ArchiveFolderNodeViewModel? node)
        {
            // Повторний клік по вже обраній гілці знімає вибір — інакше
            // повернутися до повного списку можна було б лише кнопкою збоку.
            var path = node is null || node.Path == SelectedFolderPath ? null : node.Path;

            foreach (var root in FolderTree)
                foreach (var candidate in root.SelfAndDescendants())
                    candidate.IsSelected = path is not null && candidate.Path == path;

            SelectedFolderPath = path;
            FolderScopeText = path ?? "Усі документи";

            await ResetAndReloadAsync();
        }

        [RelayCommand]
        private Task ClearFolderAsync() => SelectFolderAsync(null);

        private async Task ReloadFolderTreeAsync()
        {
            var nodes = await _archiveService.GetFolderTreeAsync(BuildFilter(0));

            FolderTree.Clear();
            foreach (var node in nodes)
                FolderTree.Add(new ArchiveFolderNodeViewModel(node, SelectedFolderPath));

            AutoCollapseTrivialTree();
        }

        private async Task ResetAndReloadAsync()
        {
            ClearChecked();
            _loadedRows.Clear();
            await LoadPageAsync();
            await ReloadStatsAsync();
            await ReloadFolderTreeAsync();
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
                ApplySearch();
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
            StatsText = $"{stats.Count} документів · {stats.TotalBytes / 1024.0 / 1024.0:0.#} МБ";
            ArchiveHasAnyDocuments = stats.Count > 0
                || _loadedRows.Count > 0
                || (await _archiveService.GetStatsAsync(new ArchiveFilter(null, null, null, null, null, 0, 1))).Count > 0;
        }

        // Пошук по ПІБ і FileName — у пам'яті, культура uk-UA (SQLite NOCASE ≠ кирилиця).
        private void ApplySearch()
        {
            var query = SearchText?.Trim();
            IEnumerable<ArchiveRowViewModel> filtered = _loadedRows;
            if (!string.IsNullOrEmpty(query))
            {
                filtered = _loadedRows.Where(r =>
                    UkCompare.IndexOf(r.SearchHaystack, query, CompareOptions.IgnoreCase) >= 0);
            }

            Rows.Clear();
            foreach (var row in filtered) Rows.Add(row);
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
            _suppressFilterReload = false;
            _ = ResetAndReloadAsync();
        }

        // ── Вибір чекбоксами ─────────────────────────────────────────────

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
        }

        public void ClearChecked()
        {
            _suppressHeaderCheck = true;
            foreach (var row in _loadedRows) row.IsChecked = false;
            _suppressHeaderCheck = false;
            RefreshCheckedState();
        }

        // Клік по рядку: одиночний вибір; Ctrl+клік — додає.
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

        // ── Правила доступності кнопок ───────────────────────────────────

        public bool CanOpen => CheckedCount == 1 && CheckedRows.All(r => r.HasContent);
        public bool CanSaveAs => CheckedCount >= 1 && CheckedRows.All(r => r.HasContent);
        public bool CanRegenerate => CheckedCount >= 1
            && CheckedRows.All(r => r.SourceType == DocumentSourceType.Generated && r.TemplateAlive);
        public bool CanUpload => CheckedCount == 1;
        public bool CanAttach => CheckedCount == 1;
        public bool CanHistory => CheckedCount == 1;
        public bool CanDelete => CheckedCount >= 1;

        public string? SingleSelectionTooltip => CheckedCount == 1 ? null : "Оберіть один документ";
        public string? OpenTooltip => CheckedCount != 1
            ? "Оберіть один документ"
            : CanOpen ? null : "Файл не збережено — доступні Перегенерувати й Підгрузити";
        public string? SaveAsTooltip => CheckedCount == 0
            ? "Оберіть документи"
            : CanSaveAs ? null : "Серед обраних є документи без збереженого файлу";
        public string? RegenerateTooltip => CheckedCount == 0
            ? "Оберіть документи"
            : CanRegenerate ? null : "Перегенерація можлива лише для згенерованих документів з живим шаблоном";

        // ── Дії ─────────────────────────────────────────────────────────

        [RelayCommand]
        private async Task OpenAsync()
        {
            var row = CheckedRows.FirstOrDefault();
            if (row is null || !CanOpen) return;

            try
            {
                var result = await _archiveService.OpenAsync(row.Id);
                if (!result.Success)
                {
                    MessageBox.Show(result.ErrorMessage, "Відкриття документа",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
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
                var dialog = new SaveFileDialog { FileName = rows[0].Dto.FileName };
                if (dialog.ShowDialog() != true) return;

                IsBusy = true;
                try
                {
                    var result = await _archiveService.SaveAsAsync(rows[0].Id, dialog.FileName);
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
                var (saved, errors) = await _archiveService.SaveManyAsync(
                    rows.Select(r => r.Id).ToList(), folderDialog.FolderName);

                var message = $"Збережено {saved} документів у {folderDialog.FolderName}";
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

            // Ручні мітки: одна форма на весь батч.
            var manualTags = await _archiveService.GetManualTagsAsync(
                rows.Select(r => r.TemplateId).Distinct().ToList());
            var manualValues = new Dictionary<string, string>();
            if (manualTags.Count > 0)
            {
                // Та сама форма, що й у звичайній генерації: дати пікером, тексти
                // підставлені з минулого разу, підписант зі списку.
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
                    var result = await _archiveService.RegenerateAsync(row.Id, manualValues);
                    if (result.Success) done++;
                    else errors.Add($"{row.ShortName}: {result.ErrorMessage}");
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
                await _archiveService.AttachAsync(row.Id, fileDialog.FileName, noteDialog.Note);
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

            var confirm = MessageBox.Show(
                $"Перемістити {rows.Count} документ(ів) у кошик?",
                "Видалення документів", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            await _archiveService.DeleteAsync(rows.Select(r => r.Id).ToList());
            await ResetAndReloadAsync();
        }

        // ── Таб «Запуски» ────────────────────────────────────────────────

        private async Task ReloadRunsAsync()
        {
            var runs = await _archiveService.GetRunsAsync(SelectedIntake?.Id, SelectedYear?.Id);
            Runs.Clear();
            foreach (var run in runs)
                Runs.Add(new RunGroupViewModel(run));
            RunsEmpty = Runs.Count == 0;
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

            // Акордеон: розгорнутий лише один запуск; елементи вантажаться ліниво.
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
                var result = await _archiveService.OpenAsync(docId);
                if (!result.Success)
                    MessageBox.Show(result.ErrorMessage, "Відкриття документа",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                MessageBox.Show(
                    "Не вдалося відкрити: немає програми для .docx. Скористайтесь «Зберегти як…».",
                    "Відкриття документа", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        private async Task SaveRunItemAsAsync(RunItemRowViewModel? item)
        {
            if (item?.Dto.DocumentId is not int docId || !item.CanOpen) return;

            var dialog = new SaveFileDialog { FileName = item.Dto.FileName };
            if (dialog.ShowDialog() != true) return;

            var result = await _archiveService.SaveAsAsync(docId, dialog.FileName);
            if (!result.Success)
                MessageBox.Show(result.ErrorMessage, "Зберегти як",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // ── Таб «Групові» ────────────────────────────────────────────────

        public ObservableCollection<GroupDocumentRowViewModel> GroupRows { get; } = new();

        // FilterOption несе один Id — для групового фільтра цього не досить, бо
        // XLSX- і DOCX-шаблони нумеруються незалежно й можуть збігтись числом.
        // Тому тут тримаємо GroupTemplateOption напряму, а не підганяємо спільний FilterOption.
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

        private async Task ReloadGroupAsync()
        {
            if (_suppressGroupFilterReload) return;

            if (GroupTemplateOptions.Count == 0)
            {
                _suppressGroupFilterReload = true;
                var options = await _archiveService.GetGroupTemplateOptionsAsync();
                GroupTemplateOptions.Clear();
                GroupTemplateOptions.Add(new GroupTemplateOption(null, null, "Шаблон: усі"));
                foreach (var option in options)
                    GroupTemplateOptions.Add(option);
                SelectedGroupTemplate = GroupTemplateOptions[0];
                _suppressGroupFilterReload = false;
            }

            IsBusy = true;
            try
            {
                ClearGroupChecked();
                var rows = await _archiveService.QueryGroupAsync(new GroupArchiveFilter(
                    SelectedGroupTemplate?.ExportTemplateId, SelectedGroupTemplate?.DocxTemplateId, null, 0, PageSize));
                GroupRows.Clear();
                foreach (var dto in rows)
                {
                    var row = new GroupDocumentRowViewModel(dto);
                    row.PropertyChanged += OnGroupRowPropertyChanged;
                    GroupRows.Add(row);
                }
                GroupRowCount = GroupRows.Count;
            }
            finally
            {
                IsBusy = false;
            }
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanOpenGroup))]
        [NotifyPropertyChangedFor(nameof(CanSaveGroupAs))]
        [NotifyPropertyChangedFor(nameof(CanHistoryGroup))]
        [NotifyPropertyChangedFor(nameof(CanDeleteGroup))]
        private int groupCheckedCount;

        private bool _suppressGroupHeaderCheck;

        private void OnGroupRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GroupDocumentRowViewModel.IsChecked) && !_suppressGroupHeaderCheck)
                GroupCheckedCount = GroupRows.Count(r => r.IsChecked);
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
                var dialog = new SaveFileDialog { FileName = rows[0].Dto.FileName };
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
                // Дзеркалить SaveManyAsync (особисті документи, вище): рахуємо успіхи й
                // помилки окремо, замість безумовного «Збережено N», яке для рядка без
                // вмісту брехало б про успіх.
                var saved = 0;
                var errors = new List<string>();
                foreach (var row in rows)
                {
                    var result = await _archiveService.SaveGroupAsAsync(
                        row.Id, System.IO.Path.Combine(folderDialog.FolderName, row.Dto.FileName));
                    if (result.Success) saved++;
                    else errors.Add($"{row.Dto.FileName}: {result.ErrorMessage}");
                }

                var message = $"Збережено {saved} відомостей у {folderDialog.FolderName}";
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
                $"Перемістити {rows.Count} відомост(ей) у кошик?",
                "Видалення відомостей", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            await _archiveService.DeleteGroupAsync(rows.Select(r => r.Id).ToList());
            await ReloadGroupAsync();
        }
    }
}
