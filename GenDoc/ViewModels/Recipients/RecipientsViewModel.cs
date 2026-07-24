using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Services.Recipients;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace GenDoc.ViewModels.Recipients
{
    public record ExportMenuOption(string Header, int? TemplateId);

    public partial class RecipientsViewModel : ObservableObject
    {
        private static readonly TimeSpan SearchDebounceInterval = TimeSpan.FromMilliseconds(280);

        private readonly IRecipientService _recipientService;
        private readonly IExportService _exportService;
        private readonly IExportTemplateService _exportTemplateService;
        private readonly IDialogService _dialogService;
        private readonly IServiceProvider _serviceProvider;
        private readonly DispatcherTimer _searchDebounceTimer;

        public RecipientsViewModel(
            IRecipientService recipientService,
            IExportService exportService,
            IExportTemplateService exportTemplateService,
            IDialogService dialogService,
            IServiceProvider serviceProvider)
        {
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
            foreach (var template in _exportTemplateService.GetTemplates())
                options.Add(new ExportMenuOption(template.Name, template.Id));

            return options;
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasResults))]
        [NotifyPropertyChangedFor(nameof(IsEmpty))]
        [NotifyPropertyChangedFor(nameof(CountLabel))]
        private ObservableCollection<RecipientListItem> recipients = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EmptyMessage))]
        private string? searchText;

        [ObservableProperty]
        private RecipientListItem? selectedRecipient;

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
        private void Edit(RecipientListItem? item)
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

            var isBuiltIn = option.Header == ExportTemplateService.BuiltInTemplateName;
            var dialog = new SaveFileDialog
            {
                FileName = isBuiltIn ? $"анкетні_дані_{DateTime.Now:ddMMyyyy}.xlsx" : $"{option.Header}.xlsx",
                Filter = "Excel файли (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx"
            };
            if (dialog.ShowDialog() != true) return;

            var items = _recipientService.SearchEntities(SearchText, SortColumn, SortDescending);
            var result = await _exportService.ExportByTemplateAsync(option.TemplateId.Value, items, dialog.FileName);

            if (result.Success)
            {
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
        private void DeleteRecipient(RecipientListItem? item)
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
                Recipients = new ObservableCollection<RecipientListItem>(_recipientService.Search(SearchText, SortColumn, SortDescending));
            }
            finally
            {
                Mouse.OverrideCursor = null;
                IsSearching = false;
            }
        }
    }
}
