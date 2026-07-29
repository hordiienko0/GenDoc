using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Services;
using GenDoc.Services.Navigation;
using GenDoc.Services.Personnel;
using GenDoc.ViewModels.Shell;

namespace GenDoc.ViewModels.Personnel
{
    public record BreadcrumbItem(string Text, int? NodeId, bool ShowSeparator)
    {
        public bool IsLink => NodeId is not null;
    }

    // Singleton: зберігає стан списку/пошуку між перемиканнями розділів.
    public partial class PersonnelViewModel : ObservableObject, IGuardedSection, INavigationTarget
    {
        private static readonly CompareInfo UkCompare = CultureInfo.GetCultureInfo("uk-UA").CompareInfo;

        private readonly IPersonnelService _personnelService;
        private readonly IDialogService _dialogService;
        private readonly DispatcherTimer _searchDebounceTimer;

        private List<PersonRowViewModel> _allRows = new();
        private bool _initialized;
        private bool _suppressHeaderCheck;

        public PersonnelViewModel(
            OrgTreeViewModel tree,
            IPersonnelService personnelService,
            IDialogService dialogService)
        {
            Tree = tree;
            _personnelService = personnelService;
            _dialogService = dialogService;

            _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _searchDebounceTimer.Tick += (_, _) =>
            {
                _searchDebounceTimer.Stop();
                ApplySearch();
            };

            Tree.LeaveGuard = TryLeaveEditAsync;
            Tree.SelectedNodeChanged += OnTreeSelectionChanged;
            Tree.PropertyChanged += OnTreePropertyChanged;

            WeakReferenceMessenger.Default.Register<CountsChangedMessage>(this,
                (_, _) => _ = ReloadListAsync());
        }

        public OrgTreeViewModel Tree { get; }

        public ObservableCollection<PersonRowViewModel> Rows { get; } = new();
        public ObservableCollection<BreadcrumbItem> Breadcrumb { get; } = new();

        [ObservableProperty]
        private string? searchText;

        [ObservableProperty]
        private string footerText = string.Empty;

        [ObservableProperty]
        private string emptyMessage = "Оберіть підрозділ у дереві";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasRows))]
        [NotifyPropertyChangedFor(nameof(IsEmpty))]
        private int rowCount;

        public bool HasRows => RowCount > 0;
        public bool IsEmpty => RowCount == 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasChecked))]
        [NotifyPropertyChangedFor(nameof(SelectionInfoText))]
        private int checkedCount;

        public bool HasChecked => CheckedCount > 0;
        public string SelectionInfoText => $"Обрано {CheckedCount}";

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
        private PersonCardViewModel? card;

        public bool IsCardOpen => Card is not null;

        partial void OnCardChanged(PersonCardViewModel? value) => OnPropertyChanged(nameof(IsCardOpen));

        public async Task InitializeAsync()
        {
            if (_initialized)
            {
                // Повернення в розділ: дані могли змінитись (імпорт, кошик).
                await Tree.RefreshCountsAsync();
                await ReloadListAsync();
                return;
            }
            _initialized = true;
            await Tree.EnsureLoadedAsync();
            await ReloadListAsync();
        }

        public async Task ApplyNavigationPayloadAsync(object payload)
        {
            if (payload is not IntakeNavigationPayload nav) return;

            await Tree.EnsureLoadedAsync();
            var node = Tree.FindById(nav.RootOrgNodeId);
            if (node is not null) Tree.SelectNode(node);
        }

        private async void OnTreeSelectionChanged(OrgNodeViewModel? node) => await ReloadListAsync();

        private void OnTreePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(OrgTreeViewModel.ShowDescendants))
                _ = ReloadListAsync();
        }

        partial void OnSearchTextChanged(string? value)
        {
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private async Task ReloadListAsync()
        {
            var node = Tree.SelectedNode;
            BuildBreadcrumb(node);

            if (node is null)
            {
                _allRows = new List<PersonRowViewModel>();
                Rows.Clear();
                RowCount = 0;
                FooterText = string.Empty;
                EmptyMessage = "Оберіть підрозділ у дереві";
                RefreshCheckedState();
                return;
            }

            var items = await _personnelService.QueryByNodeAsync(node.Id, Tree.ShowDescendants);

            _allRows = items.Select(i =>
            {
                var row = new PersonRowViewModel(i);
                row.PropertyChanged += OnRowPropertyChanged;
                return row;
            }).ToList();

            ApplySearch();
            UpdateFooter(node);
        }

        private void ApplySearch()
        {
            var query = SearchText?.Trim();
            IEnumerable<PersonRowViewModel> filtered = _allRows;

            if (!string.IsNullOrEmpty(query))
            {
                filtered = _allRows.Where(r =>
                    UkCompare.IndexOf(r.SearchHaystack, query, CompareOptions.IgnoreCase) >= 0);
            }

            Rows.Clear();
            foreach (var row in filtered) Rows.Add(row);
            RowCount = Rows.Count;
            EmptyMessage = string.IsNullOrEmpty(query)
                ? "У цій гілці ще немає людей"
                : $"Нічого не знайдено за запитом «{query}»";
            RefreshCheckedState();
        }

        private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PersonRowViewModel.IsChecked) && !_suppressHeaderCheck)
                RefreshCheckedState();
        }

        private void RefreshCheckedState()
        {
            CheckedCount = Rows.Count(r => r.IsChecked);
            OnPropertyChanged(nameof(HeaderChecked));
        }

        private void BuildBreadcrumb(OrgNodeViewModel? node)
        {
            Breadcrumb.Clear();
            if (node is null) return;

            var chain = Tree.GetAncestryChain(node);
            if (chain.Count > 4)
            {
                Breadcrumb.Add(new BreadcrumbItem(chain[0].Name, chain[0].Id, false));
                Breadcrumb.Add(new BreadcrumbItem("…", null, true));
                Breadcrumb.Add(new BreadcrumbItem(chain[^2].Name, chain[^2].Id, true));
                Breadcrumb.Add(new BreadcrumbItem(chain[^1].Name, chain[^1].Id, true));
            }
            else
            {
                for (var i = 0; i < chain.Count; i++)
                    Breadcrumb.Add(new BreadcrumbItem(chain[i].Name, chain[i].Id, i > 0));
            }

            FullPathToolTip = Tree.GetFullPathNames(node);
        }

        [ObservableProperty]
        private string fullPathToolTip = string.Empty;

        [RelayCommand]
        private void BreadcrumbClick(BreadcrumbItem? item)
        {
            if (item?.NodeId is not int nodeId) return;
            var node = Tree.FindById(nodeId);
            if (node is not null) Tree.SelectNode(node);
        }

        private void UpdateFooter(OrgNodeViewModel node)
        {
            var text = $"{_allRows.Count} записів у гілці «{node.Name}»";

            if (node.IntakeId is int intakeId && Tree.GetIntake(intakeId) is { } intake)
            {
                var intakeRoot = Tree.FindById(intake.RootOrgNodeId);
                if (intakeRoot is not null)
                    text += $" · {intakeRoot.TotalCount} у наборі №{intake.Number}";
            }

            FooterText = text;
        }

        [RelayCommand]
        private void ClearChecked()
        {
            _suppressHeaderCheck = true;
            foreach (var row in _allRows) row.IsChecked = false;
            _suppressHeaderCheck = false;
            RefreshCheckedState();
        }

        [RelayCommand]
        private async Task MoveCheckedAsync()
        {
            var checkedRows = Rows.Where(r => r.IsChecked).ToList();
            if (checkedRows.Count == 0 || Tree.SelectedNode is null) return;

            var picker = NodePickerDialogViewModel.ForRecipients(
                Tree, checkedRows.Select(r => r.IntakeId).ToList(), checkedRows.Count);
            if (_dialogService.ShowDialog(picker, Application.Current.MainWindow) != true
                || picker.SelectedTargetId is not int targetId) return;

            await _personnelService.MoveManyAsync(
                checkedRows.Select(r => r.Id).ToList(), targetId, Tree.SelectedNode.Name);

            ClearChecked();
            WeakReferenceMessenger.Default.Send(new CountsChangedMessage());
        }

        [RelayCommand]
        private async Task OpenRowAsync(PersonRowViewModel? row)
        {
            if (row is null) return;
            if (Card?.Id == row.Id) return;
            if (!await TryLeaveEditAsync()) return;

            var model = await _personnelService.GetForEditAsync(row.Id);
            if (model is null) return;

            OpenCard(model);
        }

        [RelayCommand]
        private async Task AddAsync()
        {
            if (Tree.SelectedNode is null) return;
            if (!await TryLeaveEditAsync()) return;

            OpenCard(new PersonEditModel
            {
                OrgNodeId = Tree.SelectedNode.Id,
                IntakeId = Tree.SelectedNode.IntakeId
            });
        }

        private void OpenCard(PersonEditModel model)
        {
            var nodeVm = Tree.FindById(model.OrgNodeId);
            var unitDisplay = nodeVm is null
                ? "—"
                : nodeVm.DocumentName ?? Tree.GetFullPathNames(nodeVm);

            var card = new PersonCardViewModel(_personnelService, model, unitDisplay);
            card.Saved += OnCardSaved;
            card.CloseRequested += () => Card = null;
            Card = card;
        }

        private void OnCardSaved(int id)
        {
            var row = _allRows.FirstOrDefault(r => r.Id == id);
            if (row is null)
            {
                // Нова особа — повний перезапит гілки і лічильників.
                WeakReferenceMessenger.Default.Send(new CountsChangedMessage());
                return;
            }

            _ = RefreshRowAsync(row);
            _ = Tree.RefreshCountsAsync();
        }

        private async Task RefreshRowAsync(PersonRowViewModel row)
        {
            var node = Tree.SelectedNode;
            if (node is null) return;
            var items = await _personnelService.QueryByNodeAsync(node.Id, Tree.ShowDescendants);
            var fresh = items.FirstOrDefault(i => i.Id == row.Id);
            if (fresh is not null) row.UpdateFrom(fresh);
            UpdateFooter(node);
        }

        [RelayCommand]
        private async Task CloseCardAsync()
        {
            if (!await TryLeaveEditAsync()) return;
            Card = null;
        }

        // Єдина точка dirty-guard: інша людина, інший вузол, ✕, «Додати», інший розділ.
        public async Task<bool> TryLeaveEditAsync()
        {
            if (Card is null || !Card.IsDirty) return true;

            var result = MessageBox.Show(
                "Зберегти зміни?", "Незбережені зміни",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            switch (result)
            {
                case MessageBoxResult.Yes:
                    if (!await Card.SaveAsync()) return false;
                    Card = null;
                    return true;
                case MessageBoxResult.No:
                    Card = null;
                    return true;
                default:
                    return false;
            }
        }

        Task<bool> IGuardedSection.TryLeaveAsync() => TryLeaveEditAsync();
    }
}
