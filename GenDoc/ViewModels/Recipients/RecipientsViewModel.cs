using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Services.Recipients;
using GenDoc.ViewModels.Archive;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace GenDoc.ViewModels.Recipients
{
    public record ExportMenuOption(string Header, int? TemplateId, bool IsBuiltIn = false);

    public partial class RecipientsViewModel : ObservableObject
    {
        private static readonly TimeSpan SearchDebounceInterval = TimeSpan.FromMilliseconds(280);

        private readonly IRecipientService _recipientService;
        private readonly IExportService _exportService;
        private readonly IExportTemplateService _exportTemplateService;
        private readonly IDialogService _dialogService;
        private readonly Services.Generation.IManualTagFormBuilder _manualTagFormBuilder;

        /// <summary>Ключ, під яким запам'ятовуються минулі значення саме для
        /// цього місця — щоб вони не змішувалися з іншими екранами.</summary>
        private const string ManualTagContextKey = "recipients-export";
        private readonly IServiceProvider _serviceProvider;
        private readonly DispatcherTimer _searchDebounceTimer;

        public RecipientsViewModel(
            IRecipientService recipientService,
            IExportService exportService,
            IExportTemplateService exportTemplateService,
            IDialogService dialogService,
            IServiceProvider serviceProvider,
            Services.Generation.IManualTagFormBuilder manualTagFormBuilder)
        {
            _manualTagFormBuilder = manualTagFormBuilder;
            _recipientService = recipientService;
            _exportService = exportService;
            _exportTemplateService = exportTemplateService;
            _dialogService = dialogService;
            _serviceProvider = serviceProvider;

            _searchDebounceTimer = new DispatcherTimer { Interval = SearchDebounceInterval };
            _searchDebounceTimer.Tick += (_, _) =>
            {
                _searchDebounceTimer.Stop();
                Refresh();
            };

            ExportOptions = BuildExportOptions();

            Refresh();
        }

        public ObservableCollection<ExportMenuOption> ExportOptions { get; }

        private ObservableCollection<ExportMenuOption> BuildExportOptions()
        {
            var options = new ObservableCollection<ExportMenuOption> { new("Простий список (.xlsx)", null) };
            foreach (var template in _exportTemplateService.GetTemplateListItems())
                options.Add(new ExportMenuOption(template.Name, template.Id, template.IsBuiltIn));

            return options;
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasResults))]
        [NotifyPropertyChangedFor(nameof(IsEmpty))]
        [NotifyPropertyChangedFor(nameof(CountLabel))]
        private ObservableCollection<RecipientRowViewModel> recipients = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EmptyMessage))]
        private string? searchText;

        [ObservableProperty]
        private RecipientRowViewModel? selectedRecipient;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CountLabel))]
        private int totalCount;

        [ObservableProperty]
        private bool isSearching;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FullNameHeaderText))]
        [NotifyPropertyChangedFor(nameof(RankHeaderText))]
        [NotifyPropertyChangedFor(nameof(PositionHeaderText))]
        [NotifyPropertyChangedFor(nameof(UnitNameHeaderText))]
        [NotifyPropertyChangedFor(nameof(RoomHeaderText))]
        private RecipientSortColumn sortColumn = RecipientSortColumn.FullName;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FullNameHeaderText))]
        [NotifyPropertyChangedFor(nameof(RankHeaderText))]
        [NotifyPropertyChangedFor(nameof(PositionHeaderText))]
        [NotifyPropertyChangedFor(nameof(UnitNameHeaderText))]
        [NotifyPropertyChangedFor(nameof(RoomHeaderText))]
        private bool sortDescending;

        public bool HasResults => Recipients.Count > 0;
        public bool IsEmpty => !HasResults;
        public string CountLabel => $"{Recipients.Count} з {TotalCount} записів";
        public string EmptyMessage => string.IsNullOrWhiteSpace(SearchText)
            ? "Особовий склад порожній"
            : $"Нічого не знайдено за запитом «{SearchText}»";

        public int SelectedCount => Recipients.Count(r => r.IsSelected);
        public bool HasSelection => SelectedCount > 0;
        public string DeleteSelectedButtonText => $"Видалити ({SelectedCount})";

        public string FullNameHeaderText => "ПІБ" + SortArrow(RecipientSortColumn.FullName);
        public string RankHeaderText => "ЗВАННЯ" + SortArrow(RecipientSortColumn.Rank);
        public string PositionHeaderText => "ПОСАДА" + SortArrow(RecipientSortColumn.Position);
        public string UnitNameHeaderText => "ПІДРОЗДІЛ" + SortArrow(RecipientSortColumn.UnitName);
        public string RoomHeaderText => "КІМНАТА" + SortArrow(RecipientSortColumn.Room);

        private string SortArrow(RecipientSortColumn column)
            => SortColumn != column ? string.Empty : SortDescending ? " ▼" : " ▲";

        partial void OnSearchTextChanged(string? value)
        {
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        public void SortByColumn(RecipientSortColumn column)
        {
            if (SortColumn == column)
            {
                SortDescending = !SortDescending;
            }
            else
            {
                SortColumn = column;
                SortDescending = false;
            }

            Refresh();
        }

        [RelayCommand]
        private void Add()
        {
            var vm = _serviceProvider.GetRequiredService<RecipientEditViewModel>();
            vm.Initialize(null);

            var result = _dialogService.ShowDialog(vm, Application.Current.MainWindow);
            if (result == true) Refresh();
        }

        [RelayCommand]
        private void DeleteSelected()
        {
            var selected = Recipients.Where(r => r.IsSelected).ToList();
            if (selected.Count == 0) return;

            var result = MessageBox.Show(
                $"Видалити {selected.Count} записів до кошика?",
                "Підтвердження видалення",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            foreach (var row in selected)
                _recipientService.Delete(row.Id);

            Refresh();
        }

        [RelayCommand]
        private void Edit(RecipientRowViewModel? item)
        {
            var target = item ?? SelectedRecipient;
            if (target is null) return;

            var vm = _serviceProvider.GetRequiredService<RecipientEditViewModel>();
            vm.Initialize(target.Id);

            var result = _dialogService.ShowDialog(vm, Application.Current.MainWindow);
            if (result == true) Refresh();
        }

        [RelayCommand]
        private async Task ExportOption(ExportMenuOption? option)
        {
            if (option is null) return;

            if (option.TemplateId is null)
            {
                await ExportAsync();
                return;
            }

            var manualTags = _exportTemplateService.GetManualTags(option.TemplateId.Value);
            var manualValues = new Dictionary<string, string>();
            if (manualTags.Count > 0)
            {
                // Та сама форма, що в генерації: дати пікером, тексти з минулого разу.
                var form = await _manualTagFormBuilder.BuildAsync(manualTags, ManualTagContextKey);
                var manualDialog = new ManualValuesDialogViewModel(form);
                if (_dialogService.ShowDialog(manualDialog, Application.Current.MainWindow) != true) return;

                manualValues = manualDialog.GetValues();
                await _manualTagFormBuilder.SaveAsync(ManualTagContextKey, form);
            }

            var dialog = new SaveFileDialog
            {
                FileName = option.IsBuiltIn ? $"анкетні_дані_{DateTime.Now:ddMMyyyy}.xlsx" : $"{option.Header}.xlsx",
                Filter = "Excel файли (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx"
            };
            if (dialog.ShowDialog() != true) return;

            var items = _recipientService.SearchEntities(SearchText, SortColumn, SortDescending);
            var result = await _exportService.ExportByTemplateAsync(option.TemplateId.Value, items, manualValues, dialog.FileName);

            if (result.Success)
            {
                var message = $"Експортовано {result.RowCount} записів у файл:\n{result.FilePath}";
                if (result.UnfilledTags is { Count: > 0 } unfilled)
                    message += $"\n\nНе заповнено теги: {string.Join(", ", unfilled)}";

                MessageBox.Show(message, "Експорт завершено", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    $"Не вдалося виконати експорт:\n{result.ErrorMessage}",
                    "Помилка експорту", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private async Task ExportAsync()
        {
            var dialog = new SaveFileDialog
            {
                FileName = $"особовий_склад_{DateTime.Now:ddMMyyyy}.xlsx",
                Filter = "Excel файли (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx"
            };
            if (dialog.ShowDialog() != true) return;

            // Та сама вибірка, що зараз у гріді (пошук/сортування враховані) —
            // а не весь особовий склад.
            var items = _recipientService.SearchEntities(SearchText, SortColumn, SortDescending);

            var columns = new List<ExportColumn<Recipient>>
            {
                new("Прізвище", r => r.LastName),
                new("Ім'я", r => r.FirstName),
                new("По батькові", r => r.MiddleName),
                new("Звання", r => r.Rank),
                new("Посада", r => r.Position),
                new("Підрозділ", r => r.Unit?.Name),
                new("Особовий номер", r => r.ServiceNumber),
                new("Дата народження", r => r.DateOfBirth?.ToDateTime(TimeOnly.MinValue)),
                new("Кімната", FormatRoom),
            };

            var result = await _exportService.ExportToXlsxAsync(items, columns, dialog.FileName, "Особовий склад");

            if (result.Success)
            {
                _recipientService.LogExport(result.RowCount, result.FilePath!);

                MessageBox.Show(
                    $"Експортовано {result.RowCount} записів у файл:\n{result.FilePath}",
                    "Експорт завершено", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    $"Не вдалося виконати експорт:\n{result.ErrorMessage}",
                    "Помилка експорту", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string? FormatRoom(Recipient r)
        {
            if (r.Room is null || string.IsNullOrWhiteSpace(r.Room.Number)) return null;
            return string.IsNullOrWhiteSpace(r.Room.Building) ? r.Room.Number : $"{r.Room.Building} {r.Room.Number}";
        }

        [RelayCommand]
        private void DeleteRecipient(RecipientRowViewModel? item)
        {
            if (item is null) return;

            var result = MessageBox.Show(
                $"Видалити «{item.FullName}» до кошика?",
                "Підтвердження видалення",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            _recipientService.Delete(item.Id);
            Refresh();
        }

        private void Refresh()
        {
            IsSearching = true;
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                TotalCount = _recipientService.Search(null, SortColumn, SortDescending).Count;

                var rows = _recipientService.Search(SearchText, SortColumn, SortDescending)
                    .Select(item => new RecipientRowViewModel(item))
                    .ToList();

                foreach (var row in rows)
                    row.PropertyChanged += OnRowPropertyChanged;

                Recipients = new ObservableCollection<RecipientRowViewModel>(rows);
                NotifySelectionChanged();
            }
            finally
            {
                Mouse.OverrideCursor = null;
                IsSearching = false;
            }
        }

        private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RecipientRowViewModel.IsSelected)) NotifySelectionChanged();
        }

        private void NotifySelectionChanged()
        {
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(DeleteSelectedButtonText));
        }
    }
}
