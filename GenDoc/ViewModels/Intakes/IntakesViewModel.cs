using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Intakes;
using GenDoc.Services.Navigation;
using GenDoc.ViewModels.Personnel;
using GenDoc.ViewModels.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.ViewModels.Intakes
{
    public record IntakeStatusFilterOption(IntakeStatus? Status, string Label);

    public partial class IntakesViewModel : ObservableObject
    {
        private readonly IIntakeService _intakeService;
        private readonly Services.Completeness.ICompletenessService _completenessService;
        private readonly IDialogService _dialogService;
        private readonly IServiceProvider _serviceProvider;
        private readonly ActiveIntakeState _activeIntakeState;
        private readonly OrgTreeViewModel _tree;
        private readonly IUserSettingsService _userSettings;

        private List<IntakeOverview> _all = new();
        private CancellationTokenSource? _summaryCts;

        public IntakesViewModel(
            IIntakeService intakeService,
            Services.Completeness.ICompletenessService completenessService,
            IDialogService dialogService,
            IServiceProvider serviceProvider,
            ActiveIntakeState activeIntakeState,
            OrgTreeViewModel tree,
            ICurrentUserContext currentUserContext,
            IUserSettingsService userSettings)
        {
            _intakeService = intakeService;
            _completenessService = completenessService;
            _dialogService = dialogService;
            _serviceProvider = serviceProvider;
            _activeIntakeState = activeIntakeState;
            _tree = tree;
            _userSettings = userSettings;
            _ = currentUserContext; // резервується для майбутнього - Закрити/Відкрити пишуть автора через IAuditLogService

            StatusOptions = new ObservableCollection<IntakeStatusFilterOption>
            {
                new(null, "Усі статуси"),
                new(IntakeStatus.Active, "Активні"),
                new(IntakeStatus.Planned, "Заплановані"),
                new(IntakeStatus.Completed, "Завершені")
            };
            selectedStatus = StatusOptions[0];

            WeakReferenceMessenger.Default.Register<IntakesViewModel, MatrixChangedMessage>(this,
                static (recipient, message) => recipient.InvalidateSummaries());
        }

        public ObservableCollection<IntakeCardViewModel> Cards { get; } = new();
        public ObservableCollection<IntakeStatusFilterOption> StatusOptions { get; }
        public ObservableCollection<IntakeYearOption> YearOptions { get; } = new();

        [ObservableProperty]
        private IntakeStatusFilterOption? selectedStatus;

        [ObservableProperty]
        private IntakeYearOption? selectedYear;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(NotBusy))]
        private bool isBusy;

        public bool NotBusy => !IsBusy;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EmptyText))]
        private bool hasAnyIntakes;

        public bool IsEmpty => Cards.Count == 0;

        public string EmptyText => !HasAnyIntakes
            ? "Ще немає жодного набору"
            : "Немає наборів за обраними фільтрами";

        partial void OnSelectedStatusChanged(IntakeStatusFilterOption? value) => ApplyFilter();
        partial void OnSelectedYearChanged(IntakeYearOption? value) => ApplyFilter();

        [RelayCommand]
        private async Task InitializeAsync() => await RefreshAsync();

        [RelayCommand]
        private async Task RefreshAsync()
        {
            IsBusy = true;
            try
            {
                _all = (await _intakeService.GetOverviewsAsync()).ToList();
                HasAnyIntakes = _all.Count > 0;

                var years = await _intakeService.GetYearsAsync();
                var keepYear = SelectedYear?.Year;
                YearOptions.Clear();
                YearOptions.Add(new IntakeYearOption(null, "Усі роки"));
                foreach (var y in years) YearOptions.Add(new IntakeYearOption(y, y.ToString()));
                SelectedYear = YearOptions.FirstOrDefault(o => o.Year == keepYear) ?? YearOptions[0];

                ApplyFilter();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ApplyFilter()
        {
            IEnumerable<IntakeOverview> filtered = _all;
            if (SelectedStatus?.Status is IntakeStatus status)
                filtered = filtered.Where(i => i.Status == status);
            if (SelectedYear?.Year is int year)
                filtered = filtered.Where(i => i.DateStart.Year == year);

            Cards.Clear();
            foreach (var overview in filtered) Cards.Add(new IntakeCardViewModel(overview));
            OnPropertyChanged(nameof(IsEmpty));

            RestartSummaryLoad();
        }

        private void InvalidateSummaries()
        {
            foreach (var card in Cards) card.IsSummaryLoading = card.HasPackage;
            RestartSummaryLoad();
        }

        private void RestartSummaryLoad()
        {
            _summaryCts?.Cancel();
            var cts = new CancellationTokenSource();
            _summaryCts = cts;
            _ = LoadSummariesAsync(Cards.ToList(), cts.Token);
        }

        private async Task LoadSummariesAsync(IReadOnlyList<IntakeCardViewModel> cards, CancellationToken token)
        {
            foreach (var card in cards)
            {
                if (token.IsCancellationRequested) return;
                if (!card.HasPackage) continue;

                var intake = await _intakeService.GetByIdAsync(card.Id);
                if (token.IsCancellationRequested) return;
                if (intake?.DefaultPackageId is not int packageId) continue;

                var summary = await _completenessService.GetIntakeSummaryAsync(card.Id, packageId);
                if (token.IsCancellationRequested) return;
                card.ApplySummary(summary.Percent, summary.IncompletePeople);
            }
        }

        [RelayCommand]
        private async Task CreateIntakeAsync()
        {
            await _tree.EnsureLoadedAsync();

            var vm = _serviceProvider.GetRequiredService<IntakeWizardViewModel>();
            await vm.InitializeAsync();
            if (_dialogService.ShowDialog(vm, Application.Current.MainWindow) != true || vm.CreatedIntake is null) return;

            await _activeIntakeState.RefreshAsync();
            await _tree.ReloadAsync();
            WeakReferenceMessenger.Default.Send(new CountsChangedMessage());
            await RefreshAsync();
        }

        [RelayCommand]
        private void OpenMatrix(IntakeCardViewModel? card)
        {
            if (card is null) return;
            WeakReferenceMessenger.Default.Send(new NavigateToSectionMessage(
                MainViewModel.CompletenessSectionTitle,
                new IntakeNavigationPayload(card.Id, card.RootOrgNodeId, null)));
        }

        [RelayCommand]
        private void OpenPersonnel(IntakeCardViewModel? card)
        {
            if (card is null) return;
            WeakReferenceMessenger.Default.Send(new NavigateToSectionMessage(
                MainViewModel.PersonnelSectionTitle,
                new IntakeNavigationPayload(card.Id, card.RootOrgNodeId, null)));
        }

        [RelayCommand]
        private void OpenGeneration(IntakeCardViewModel? card)
        {
            if (card is null) return;
            WeakReferenceMessenger.Default.Send(new NavigateToSectionMessage(
                MainViewModel.GenerationSectionTitle,
                new IntakeNavigationPayload(card.Id, card.RootOrgNodeId, null)));
        }

        [RelayCommand]
        private async Task MakeMineAsync(IntakeCardViewModel? card)
        {
            if (card is null) return;
            await _userSettings.UpdateAsync(s => s.ActiveIntakeId = card.Id);
            await _activeIntakeState.RefreshAsync();
        }

        [RelayCommand]
        private async Task CloseIntakeAsync(IntakeCardViewModel? card)
        {
            if (card is null) return;
            try
            {
                var info = await _intakeService.GetCloseInfoAsync(card.Id);
                var vm = _serviceProvider.GetRequiredService<CloseIntakeDialogViewModel>();
                vm.Initialize(info);
                if (_dialogService.ShowDialog(vm, Application.Current.MainWindow) != true) return;

                await _intakeService.CloseAsync(vm.BuildRequest());
                await _activeIntakeState.RefreshAsync();
                await _tree.ReloadAsync();
                WeakReferenceMessenger.Default.Send(new CountsChangedMessage());
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не вдалося закрити набір: {ex.Message}", "Закриття набору",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        private async Task ReopenIntakeAsync(IntakeCardViewModel? card)
        {
            if (card is null) return;

            var confirm = MessageBox.Show(
                $"Відкрити повторно набір {card.CodeText.TrimStart('·', ' ')}? " +
                "Звільнені кімнати та переміщення до «Випускників» відновлені НЕ будуть.",
                "Повторне відкриття набору", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            await _intakeService.ReopenAsync(card.Id);
            await _activeIntakeState.RefreshAsync();
            WeakReferenceMessenger.Default.Send(new CountsChangedMessage());
            await RefreshAsync();
        }
    }
}
