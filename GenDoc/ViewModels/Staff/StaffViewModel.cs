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

    public partial class StaffViewModel : ObservableObject
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
            var vm = _serviceProvider.GetRequiredService<RecipientEditViewModel>();
            vm.Initialize(null);

            var result = _dialogService.ShowDialog(vm, Application.Current.MainWindow);
            if (result == true) await RefreshAsync();
        }

        [RelayCommand(CanExecute = nameof(HasSelection))]
        private async Task OpenTripAsync() => await OpenDocDialogAsync(StaffEventKind.BusinessTrip);

        [RelayCommand(CanExecute = nameof(HasSelection))]
        private async Task OpenLeaveAsync() => await OpenDocDialogAsync(StaffEventKind.Leave);

        // «В догонку»: генерація документів PerRecipient без оформлення відрядження/відпустки.
        [RelayCommand(CanExecute = nameof(HasSelection))]
        private async Task GenerateDocumentAsync() => await OpenDocDialogAsync(null);

        // Пункт контекстного меню рядка — генерація для однієї людини без чекбоксів.
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

            await RefreshAsync();
        }
    }
}
