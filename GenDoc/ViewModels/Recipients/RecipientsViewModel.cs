using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Services;
using GenDoc.Services.Recipients;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.ViewModels.Recipients
{
    public partial class RecipientsViewModel : ObservableObject
    {
        private static readonly TimeSpan SearchDebounceInterval = TimeSpan.FromMilliseconds(280);

        private readonly IRecipientService _recipientService;
        private readonly IDialogService _dialogService;
        private readonly IServiceProvider _serviceProvider;
        private readonly DispatcherTimer _searchDebounceTimer;

        public RecipientsViewModel(IRecipientService recipientService, IDialogService dialogService, IServiceProvider serviceProvider)
        {
            _recipientService = recipientService;
            _dialogService = dialogService;
            _serviceProvider = serviceProvider;

            _searchDebounceTimer = new DispatcherTimer { Interval = SearchDebounceInterval };
            _searchDebounceTimer.Tick += (_, _) =>
            {
                _searchDebounceTimer.Stop();
                Refresh();
            };

            Refresh();
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
