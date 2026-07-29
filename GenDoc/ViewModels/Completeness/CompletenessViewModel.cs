using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Completeness;
using GenDoc.Services.Documents;
using GenDoc.ViewModels.Archive;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace GenDoc.ViewModels.Completeness
{
    public record IntakeFilterOption(int Id, string Label);
    public record PackageFilterOption(int Id, string Label);

    // Singleton: фільтри живуть між перемиканнями розділів; матриця перебудовується
    // при зміні набору/пакета, а не тримається завжди в пам'яті.
    public partial class CompletenessViewModel : ObservableObject, ICellActionCoordinator
    {
        private static readonly CompareInfo UkCompare = CultureInfo.GetCultureInfo("uk-UA").CompareInfo;

        private readonly ICompletenessService _completenessService;
        private readonly IDocumentArchiveService _archiveService;
        private readonly IDialogService _dialogService;
        private readonly IServiceProvider _serviceProvider;
        private readonly DispatcherTimer _searchDebounceTimer;

        private readonly List<MatrixRowViewModel> _allRows = new();
        private MatrixData? _matrixData;
        private bool _initialized;
        private bool _suppressFilterReload;
        private bool _suppressHeaderCheck;

        public CompletenessViewModel(
            ICompletenessService completenessService,
            IDocumentArchiveService archiveService,
            IDialogService dialogService,
            IServiceProvider serviceProvider)
        {
            _completenessService = completenessService;
            _archiveService = archiveService;
            _dialogService = dialogService;
            _serviceProvider = serviceProvider;

            _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _searchDebounceTimer.Tick += (_, _) =>
            {
                _searchDebounceTimer.Stop();
                ApplySearch();
            };
        }

        public ObservableCollection<MatrixRowViewModel> Rows { get; } = new();

        // Порядок колонок — джерело правди для генератора DataGrid-колонок у view.
        public List<MatrixTemplateInfo> Columns { get; private set; } = new();

        public event Action? ColumnsChanged;

        public ObservableCollection<IntakeFilterOption> IntakeOptions { get; } = new();
        public ObservableCollection<PackageFilterOption> PackageOptions { get; } = new();

        [ObservableProperty] private IntakeFilterOption? selectedIntake;
        [ObservableProperty] private PackageFilterOption? selectedPackage;

        partial void OnSelectedIntakeChanged(IntakeFilterOption? value)
        {
            if (_suppressFilterReload) return;
            _ = RebuildAsync();
        }

        partial void OnSelectedPackageChanged(PackageFilterOption? value)
        {
            if (_suppressFilterReload) return;
            _ = RebuildAsync();
        }

        [ObservableProperty]
        private string? searchText;

        partial void OnSearchTextChanged(string? value)
        {
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        [ObservableProperty] private bool isBusy;
        [ObservableProperty] private string footerText = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasRows))]
        private int rowCount;

        public bool HasRows => RowCount > 0;

        [ObservableProperty] private bool hasNoIntakes;
        [ObservableProperty] private bool packageIsEmpty;
        [ObservableProperty] private bool intakeHasNoPeople;
        [ObservableProperty] private bool isFullyComplete;

        public bool ShowMatrix => !HasNoIntakes && !PackageIsEmpty && !IntakeHasNoPeople;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(GenerateMissingLabel))]
        private int missingRequiredCount;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(GenerateMissingLabel))]
        private int missingOptionalCount;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RegenerateStaleLabel))]
        private int staleCount;

        public string GenerateMissingLabel => $"Згенерувати все, чого бракує ({MissingRequiredCount})";
        public string RegenerateStaleLabel => $"Перегенерувати застарілі ({StaleCount})";

        public bool CanGenerateMissing => MissingRequiredCount > 0;
        public bool CanRegenerateStale => StaleCount > 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanExportSelected))]
        private int checkedCount;

        public bool CanExportSelected => CheckedCount > 0;

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
                RefreshCheckedCount();
            }
        }

        private void RefreshCheckedCount()
        {
            CheckedCount = Rows.Count(r => r.IsChecked);
            OnPropertyChanged(nameof(HeaderChecked));
        }

        public async Task InitializeAsync()
        {
            if (_initialized)
            {
                await RebuildAsync();
                return;
            }
            _initialized = true;
            await ReloadIntakesAsync();
        }

        private async Task ReloadIntakesAsync()
        {
            var intakes = await GetIntakeOptionsAsync();

            _suppressFilterReload = true;
            IntakeOptions.Clear();
            foreach (var option in intakes) IntakeOptions.Add(option);

            var activePackageDefault = await _completenessService.GetDefaultPackageIdAsync();
            SelectedIntake = IntakeOptions.FirstOrDefault(o => o.Label.Contains("активний"))
                ?? IntakeOptions.FirstOrDefault();
            _suppressFilterReload = false;

            HasNoIntakes = IntakeOptions.Count == 0;
            if (SelectedIntake is not null) await RebuildAsync();
        }

        private async Task<List<IntakeFilterOption>> GetIntakeOptionsAsync()
        {
            // Пакети/набори читаються через сервіс архіву (спільні фільтр-опції вже там реалізовані).
            var options = await _archiveService.GetFilterOptionsAsync();
            return options.Intakes.Select(i => new IntakeFilterOption(i.Id, i.Label)).ToList();
        }

        private async Task ReloadPackagesAsync()
        {
            var options = await _archiveService.GetFilterOptionsAsync();
            _suppressFilterReload = true;
            PackageOptions.Clear();
            foreach (var (id, name) in options.Packages)
                PackageOptions.Add(new PackageFilterOption(id, name));

            var defaultId = await _completenessService.GetDefaultPackageIdAsync();
            SelectedPackage = defaultId is int d
                ? PackageOptions.FirstOrDefault(o => o.Id == d) ?? PackageOptions.FirstOrDefault()
                : PackageOptions.FirstOrDefault();
            _suppressFilterReload = false;
        }

        private async Task RebuildAsync()
        {
            if (PackageOptions.Count == 0) await ReloadPackagesAsync();

            if (SelectedIntake is null || SelectedPackage is null)
            {
                Rows.Clear();
                _allRows.Clear();
                RowCount = 0;
                PackageIsEmpty = false;
                IntakeHasNoPeople = false;
                return;
            }

            IsBusy = true;
            try
            {
                _matrixData = await _completenessService.BuildAsync(SelectedIntake.Id, SelectedPackage.Id);

                PackageIsEmpty = _matrixData.Templates.Count == 0;
                IntakeHasNoPeople = !PackageIsEmpty && _matrixData.People.Count == 0;

                Columns = _matrixData.Templates;
                ColumnsChanged?.Invoke();

                _allRows.Clear();
                foreach (var person in _matrixData.People)
                {
                    var row = new MatrixRowViewModel(person);
                    foreach (var template in _matrixData.Templates)
                    {
                        var requirement = ICompletenessService.Resolve(template, row.FitnessCategory);
                        var cell = new MatrixCellViewModel(this, person.Id, template.TemplateId, row.FitnessCategory, requirement);
                        _matrixData.Docs.TryGetValue((person.Id, template.TemplateId), out var doc);
                        cell.Initialize(doc);
                        row.Cells.Add(cell);
                    }
                    row.RecomputeReadiness();
                    row.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName == nameof(MatrixRowViewModel.IsChecked) && !_suppressHeaderCheck)
                            RefreshCheckedCount();
                    };
                    _allRows.Add(row);
                }

                ApplySearch();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ApplySearch()
        {
            var query = SearchText?.Trim();
            IEnumerable<MatrixRowViewModel> filtered = _allRows;
            if (!string.IsNullOrEmpty(query))
                filtered = _allRows.Where(r => UkCompare.IndexOf(r.SearchHaystack, query, CompareOptions.IgnoreCase) >= 0);

            Rows.Clear();
            foreach (var row in filtered) Rows.Add(row);
            RowCount = Rows.Count;
            RefreshCheckedCount();
            RecomputeAggregates();
        }

        private void RecomputeAggregates()
        {
            MissingRequiredCount = Rows.Sum(r => r.Cells.Count(c => c.IsMissingRequired));
            MissingOptionalCount = Rows.Sum(r => r.Cells.Count(c => c.IsMissingOptional));
            StaleCount = Rows.Sum(r => r.Cells.Count(c => c.IsStale));
            OnPropertyChanged(nameof(CanGenerateMissing));
            OnPropertyChanged(nameof(CanRegenerateStale));

            var requiredPresentTotal = Rows.Sum(r => r.RequiredPresent);
            var requiredTotal = Rows.Sum(r => r.RequiredTotal);
            IsFullyComplete = Rows.Count > 0 && requiredTotal > 0 && requiredPresentTotal == requiredTotal;

            var optionalSuffix = MissingOptionalCount > 0 ? $" · {MissingOptionalCount} опц." : string.Empty;
            FooterText = Rows.Count == 0
                ? string.Empty
                : $"{Rows.Count} осіб · {requiredPresentTotal} документів з {requiredTotal} обов'язкових · {StaleCount} застарілих{optionalSuffix}";
        }

        // ── Меню шаблонів (⚙ Вимоги) ─────────────────────────────────────

        [RelayCommand]
        private async Task OpenRequirementsAsync()
        {
            if (SelectedPackage is null) return;
            var vm = _serviceProvider.GetRequiredService<PackageRequirementsViewModel>();
            await vm.InitializeAsync(SelectedPackage.Id, SelectedIntake?.Id);
            if (_dialogService.ShowDialog(vm, Application.Current.MainWindow) == true)
            {
                WeakReferenceMessenger.Default.Send(new MatrixChangedMessage());
                await RebuildAsync();
            }
        }

        // ── Кнопки batch-дій ──────────────────────────────────────────────

        [RelayCommand(CanExecute = nameof(CanGenerateMissing))]
        private async Task GenerateMissingAsync()
        {
            if (_matrixData is null) return;

            var missingRequired = new List<(int RecipientId, int TemplateId)>();
            var missingOptional = new List<(int RecipientId, int TemplateId)>();
            foreach (var row in Rows)
            {
                foreach (var cell in row.Cells)
                {
                    if (cell.IsMissingRequired) missingRequired.Add((cell.RecipientId, cell.TemplateId));
                    else if (cell.IsMissingOptional) missingOptional.Add((cell.RecipientId, cell.TemplateId));
                }
            }

            var includeOptional = false;
            {
                var confirm = MessageBox.Show(
                    $"Набір «{SelectedIntake?.Label}» · пакет «{SelectedPackage?.Label}» · буде згенеровано:\n" +
                    $"обов'язкових: {missingRequired.Count}\n" +
                    $"Також згенерувати опційні ({missingOptional.Count})? Так — з опційними, Ні — лише обов'язкові.",
                    "Генерація відсутніх документів", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (confirm == MessageBoxResult.Cancel) return;
                includeOptional = confirm == MessageBoxResult.Yes;
            }

            var targets = includeOptional ? missingRequired.Concat(missingOptional).ToList() : missingRequired;
            if (targets.Count == 0) return;

            var manualValues = await CollectManualValuesAsync(targets.Select(t => t.TemplateId).Distinct().ToList());
            if (manualValues is null) return;

            IsBusy = true;
            var done = 0;
            var errors = new List<string>();
            try
            {
                foreach (var (recipientId, templateId) in targets)
                {
                    var result = await _completenessService.GenerateForPairAsync(recipientId, templateId, manualValues);
                    if (result.Success) done++;
                    else errors.Add(result.ErrorMessage ?? "невідома помилка");
                }
            }
            finally
            {
                IsBusy = false;
            }

            ShowBatchSummary("Генерація завершена", done, targets.Count - done, errors);
            await RebuildAsync();
            WeakReferenceMessenger.Default.Send(new MatrixChangedMessage());
        }

        [RelayCommand(CanExecute = nameof(CanRegenerateStale))]
        private async Task RegenerateStaleAsync()
        {
            var staleCells = Rows.SelectMany(r => r.Cells).Where(c => c.IsStale).ToList();
            if (staleCells.Count == 0) return;

            var confirm = MessageBox.Show(
                $"Буде створено нові версії для {staleCells.Count} документів.",
                "Перегенерація застарілих", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.OK) return;

            IsBusy = true;
            var done = 0;
            var errors = new List<string>();
            try
            {
                foreach (var cell in staleCells)
                {
                    if (cell.DocumentId is not int docId) continue;
                    var result = await _archiveService.RegenerateAsync(docId, new Dictionary<string, string>());
                    if (result.Success) done++;
                    else errors.Add(result.ErrorMessage ?? "невідома помилка");
                }
            }
            finally
            {
                IsBusy = false;
            }

            ShowBatchSummary("Перегенерація завершена", done, staleCells.Count - done, errors);
            await RebuildAsync();
            WeakReferenceMessenger.Default.Send(new MatrixChangedMessage());
        }

        [RelayCommand(CanExecute = nameof(CanExportSelected))]
        private async Task ExportSelectedAsync()
        {
            var recipientIds = Rows.Where(r => r.IsChecked).Select(r => r.RecipientId).ToList();
            if (recipientIds.Count == 0 || SelectedPackage is null) return;

            var folderDialog = new OpenFolderDialog { Title = "Папка для експорту пакетів" };
            if (folderDialog.ShowDialog() != true) return;

            IsBusy = true;
            try
            {
                var (people, files, warnings) = await _completenessService.ExportPackagesAsync(
                    recipientIds, SelectedPackage.Id, folderDialog.FolderName);

                var message = $"Експортовано {files} файлів для {people} осіб у {folderDialog.FolderName}";
                if (warnings.Count > 0)
                    message += $"\n\nПопередження:\n{string.Join("\n", warnings.Take(8))}";
                MessageBox.Show(message, "Експорт завершено", MessageBoxButton.OK,
                    warnings.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static void ShowBatchSummary(string title, int done, int failed, List<string> errors)
        {
            var message = $"Згенеровано: {done}";
            if (failed > 0) message += $"\nПомилок: {failed}\n{string.Join("\n", errors.Take(6))}";
            MessageBox.Show(message, title, MessageBoxButton.OK,
                failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }

        private async Task<Dictionary<string, string>?> CollectManualValuesAsync(List<int> templateIds)
        {
            var tags = await _archiveService.GetManualTagsAsync(templateIds);
            if (tags.Count == 0) return new Dictionary<string, string>();

            var dialog = new ManualValuesDialogViewModel(tags);
            if (_dialogService.ShowDialog(dialog, Application.Current.MainWindow) != true) return null;
            return dialog.GetValues();
        }

        // ── ICellActionCoordinator: дії з меню клітинки ──────────────────

        public async Task OpenAsync(MatrixCellViewModel cell)
        {
            if (cell.DocumentId is not int docId) return;
            try { await _archiveService.OpenAsync(docId); }
            catch (System.ComponentModel.Win32Exception)
            {
                MessageBox.Show(
                    "Не вдалося відкрити: немає програми для .docx. Скористайтесь «Зберегти як…».",
                    "Відкриття документа", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public async Task GenerateAsync(MatrixCellViewModel cell)
        {
            var manualValues = await CollectManualValuesAsync(new List<int> { cell.TemplateId });
            if (manualValues is null) return;

            var result = await _completenessService.GenerateForPairAsync(cell.RecipientId, cell.TemplateId, manualValues);
            if (!result.Success)
            {
                MessageBox.Show(result.ErrorMessage, "Генерація", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await RefreshCellAsync(cell);
            WeakReferenceMessenger.Default.Send(new MatrixChangedMessage());
        }

        public async Task RegenerateAsync(MatrixCellViewModel cell)
        {
            if (cell.DocumentId is not int docId) return;

            var confirm = MessageBox.Show(
                "Буде створено нову версію. Попередня залишиться в архіві.",
                "Перегенерація", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.OK) return;

            var result = await _archiveService.RegenerateAsync(docId, new Dictionary<string, string>());
            if (!result.Success)
            {
                MessageBox.Show(result.ErrorMessage, "Перегенерація", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await RefreshCellAsync(cell);
            WeakReferenceMessenger.Default.Send(new MatrixChangedMessage());
        }

        public async Task HistoryAsync(MatrixCellViewModel cell)
        {
            if (cell.DocumentId is not int docId) return;
            var row = Rows.FirstOrDefault(r => r.RecipientId == cell.RecipientId);
            var template = Columns.FirstOrDefault(t => t.TemplateId == cell.TemplateId);

            var vm = new VersionHistoryViewModel(
                _archiveService, cell.RecipientId, cell.TemplateId, docId,
                row?.FullName ?? "—", template?.Name ?? "—");
            await vm.InitializeAsync();
            _dialogService.ShowDialog(vm, Application.Current.MainWindow);

            if (vm.HasChanges) await RefreshCellAsync(cell);
        }

        public async Task SaveAsAsync(MatrixCellViewModel cell)
        {
            if (cell.DocumentId is not int docId) return;

            var row = Rows.FirstOrDefault(r => r.RecipientId == cell.RecipientId);
            var template = Columns.FirstOrDefault(t => t.TemplateId == cell.TemplateId);
            var dialog = new SaveFileDialog
            {
                FileName = $"{row?.FullName} — {template?.Name}.docx"
            };
            if (dialog.ShowDialog() != true) return;

            await _archiveService.SaveAsAsync(docId, dialog.FileName);
        }

        private async Task RefreshCellAsync(MatrixCellViewModel cell)
        {
            var doc = await _completenessService.GetCellAsync(cell.RecipientId, cell.TemplateId);
            cell.Initialize(doc);

            var row = Rows.FirstOrDefault(r => r.RecipientId == cell.RecipientId);
            row?.RecomputeReadiness();
            RecomputeAggregates();
        }
    }
}
