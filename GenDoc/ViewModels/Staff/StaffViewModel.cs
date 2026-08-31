using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Staff;
using GenDoc.ViewModels.Recipients;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.ViewModels.Staff
{
    public enum StaffStateFilter { All, OnSite, BusinessTrip, Leave }

    public record StaffStateFilterOption(StaffStateFilter Filter, string Label);
    public record StaffUnitFilterOption(string? Unit, string Label);

    public partial class StaffViewModel : ObservableObject, Shell.IGuardedSection
    {
        private static readonly CompareInfo UkCompare = CultureInfo.GetCultureInfo("uk-UA").CompareInfo;

        private readonly IStaffService _staffService;
        private readonly IDialogService _dialogService;
        private readonly IServiceProvider _serviceProvider;

        private List<StaffRowOverview> _all = new();

        public StaffViewModel(IStaffService staffService, IDialogService dialogService, IServiceProvider serviceProvider)
        {
            _staffService = staffService;
            _dialogService = dialogService;
            _serviceProvider = serviceProvider;

            StateOptions = new ObservableCollection<StaffStateFilterOption>
            {
                new(StaffStateFilter.All, "Усі стани"),
                new(StaffStateFilter.OnSite, "На місці"),
                new(StaffStateFilter.BusinessTrip, "У відрядженні"),
                new(StaffStateFilter.Leave, "У відпустці")
            };
            selectedState = StateOptions[0];
        }

        public ObservableCollection<StaffRowViewModel> Rows { get; } = new();
        public ObservableCollection<StaffStateFilterOption> StateOptions { get; }
        public ObservableCollection<StaffUnitFilterOption> UnitOptions { get; } = new();

        [ObservableProperty]
        private StaffStateFilterOption? selectedState;

        [ObservableProperty]
        private StaffUnitFilterOption? selectedUnit;

        [ObservableProperty]
        private string? searchText;

        [ObservableProperty]
        private bool showOnlyCourseOfficers;

        /// <summary>Картка праворуч від списку - як в «Особовому складі».
        /// null - панель закрита, видно лише список.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasCard))]
        private StaffCardViewModel? card;

        public bool HasCard => Card is not null;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsEmpty))]
        [NotifyPropertyChangedFor(nameof(HasRows))]
        private int rowCount;

        public bool IsEmpty => RowCount == 0;
        public bool HasRows => RowCount > 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelection))]
        [NotifyCanExecuteChangedFor(nameof(OpenTripCommand))]
        [NotifyCanExecuteChangedFor(nameof(OpenLeaveCommand))]
        [NotifyCanExecuteChangedFor(nameof(GenerateDocumentCommand))]
        [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
        private int selectedCount;

        public bool HasSelection => SelectedCount > 0;

        [ObservableProperty] private bool isBusy;

        partial void OnSelectedStateChanged(StaffStateFilterOption? value) => ApplyFilter();
        partial void OnSelectedUnitChanged(StaffUnitFilterOption? value) => ApplyFilter();
        partial void OnSearchTextChanged(string? value) => ApplyFilter();
        partial void OnShowOnlyCourseOfficersChanged(bool value) => ApplyFilter();

        [RelayCommand]
        private async Task InitializeAsync() => await RefreshAsync();

        [RelayCommand]
        private async Task RefreshAsync()
        {
            IsBusy = true;
            try
            {
                _all = (await _staffService.GetOverviewAsync()).ToList();

                var units = await _staffService.GetUnitsAsync();
                var keepUnit = SelectedUnit?.Unit;
                UnitOptions.Clear();
                UnitOptions.Add(new StaffUnitFilterOption(null, "Усі підрозділи"));
                foreach (var u in units) UnitOptions.Add(new StaffUnitFilterOption(u, u));
                SelectedUnit = UnitOptions.FirstOrDefault(o => o.Unit == keepUnit) ?? UnitOptions[0];

                ApplyFilter();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ApplyFilter()
        {
            IEnumerable<StaffRowOverview> filtered = _all;

            if (SelectedUnit?.Unit is string unit)
                filtered = filtered.Where(o => o.UnitName == unit);

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
            if (!string.IsNullOrEmpty(query))
            {
                filtered = filtered.Where(o =>
                    UkCompare.IndexOf(o.FullName, query, CompareOptions.IgnoreCase) >= 0
                    || UkCompare.IndexOf(o.Position, query, CompareOptions.IgnoreCase) >= 0);
            }

            Rows.Clear();
            foreach (var overview in filtered)
            {
                var row = new StaffRowViewModel(overview);
                row.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(StaffRowViewModel.IsChecked)) RefreshSelectedCount();
                };
                Rows.Add(row);
            }

            RowCount = Rows.Count;
            RefreshSelectedCount();
        }

        private void RefreshSelectedCount() => SelectedCount = Rows.Count(r => r.IsChecked);

        [RelayCommand]
        private void ClearSelection()
        {
            foreach (var row in Rows) row.IsChecked = false;
        }

        [RelayCommand(CanExecute = nameof(HasSelection))]
        private async Task DeleteSelectedAsync()
        {
            var selected = Rows.Where(r => r.IsChecked).ToList();
            if (selected.Count == 0) return;

            var result = MessageBox.Show(
                $"Видалити {selected.Count} записів до кошика?",
                "Підтвердження видалення",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            await _staffService.DeleteManyAsync(selected.Select(r => r.Id).ToList());
            await RefreshAsync();
        }

        [RelayCommand]
        private async Task AddPersonAsync()
        {
            if (!await TryLeaveCardAsync()) return;

            var card = _serviceProvider.GetRequiredService<StaffCardViewModel>();
            card.StartNew();
            Card = card;
        }

        /// <summary>Клік по рядку відкриває картку на ПЕРЕГЛЯД - редагування
        /// вмикає олівець, як в «Особовому складі». До цього постійний склад не
        /// редагувався взагалі.</summary>
        [RelayCommand]
        private async Task OpenCardAsync(StaffRowViewModel? row)
        {
            if (row is null) return;
            if (Card is not null && Card.Id == row.Id && !Card.IsEditing) return;
            if (!await TryLeaveCardAsync()) return;

            var card = _serviceProvider.GetRequiredService<StaffCardViewModel>();
            if (!card.Load(row.Id))
            {
                MessageBox.Show(
                    "Картку не знайдено - можливо, її видалили в іншій сесії.",
                    "Картку не знайдено", MessageBoxButton.OK, MessageBoxImage.Information);
                await RefreshAsync();
                return;
            }

            Card = card;
        }

        [RelayCommand]
        private async Task CloseCardAsync()
        {
            if (!await TryLeaveCardAsync()) return;
            Card = null;
        }

        [RelayCommand]
        private async Task SaveCardAsync()
        {
            if (Card is null) return;
            if (!Card.TrySave()) return;

            await RefreshAsync();
        }

        [RelayCommand]
        private async Task CancelCardAsync()
        {
            if (Card is null) return;

            // Скасування НОВОЇ картки закриває панель: лишати порожню форму
            // «ні в перегляді, ні в редагуванні» безглуздо.
            if (Card.IsNew)
            {
                Card = null;
                return;
            }

            Card.CancelCommand.Execute(null);
            await Task.CompletedTask;
        }

        /// <summary>Єдина точка dirty-guard: інша людина, «+ Додати», ✕, інший
        /// розділ, закриття вікна.</summary>
        public async Task<bool> TryLeaveCardAsync()
        {
            if (Card is null || !Card.IsDirty) return true;

            var result = MessageBox.Show(
                "Зберегти зміни?", "Незбережені зміни",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            switch (result)
            {
                case MessageBoxResult.Yes:
                    if (!Card.TrySave()) return false;
                    await RefreshAsync();
                    Card = null;
                    return true;
                case MessageBoxResult.No:
                    Card = null;
                    return true;
                default:
                    return false;
            }
        }

        Task<bool> Shell.IGuardedSection.TryLeaveAsync() => TryLeaveCardAsync();

        [RelayCommand(CanExecute = nameof(HasSelection))]
        private async Task OpenTripAsync() => await OpenDocDialogAsync(StaffEventKind.BusinessTrip);

        [RelayCommand(CanExecute = nameof(HasSelection))]
        private async Task OpenLeaveAsync() => await OpenDocDialogAsync(StaffEventKind.Leave);

        // «В догонку»: генерація документів PerRecipient без оформлення відрядження/відпустки.
        [RelayCommand(CanExecute = nameof(HasSelection))]
        private async Task GenerateDocumentAsync() => await OpenDocDialogAsync(null);

        // Пункт контекстного меню рядка - генерація для однієї людини без чекбоксів.
        [RelayCommand]
        private async Task GenerateDocumentForRowAsync(StaffRowViewModel? row)
        {
            if (row is null) return;
            await OpenDocDialogAsync(null, new List<(int Id, string FullName)> { (row.Id, row.FullName) });
        }

        private async Task OpenDocDialogAsync(StaffEventKind? kind, IReadOnlyList<(int Id, string FullName)>? people = null)
        {
            var selected = people ?? Rows.Where(r => r.IsChecked).Select(r => (r.Id, r.FullName)).ToList();
            if (selected.Count == 0) return;

            var vm = _serviceProvider.GetRequiredService<StaffDocDialogViewModel>();
            vm.Initialize(kind, selected);
            _dialogService.ShowDialog(vm, Application.Current.MainWindow);

            await RefreshAsync();
        }
    }
}
