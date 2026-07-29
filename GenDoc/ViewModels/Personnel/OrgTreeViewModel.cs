using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Intakes;
using GenDoc.Services.OrgTree;

namespace GenDoc.ViewModels.Personnel
{
    public enum TreeFilterMode { Active, All, Archived }

    // Singleton: тримає стан дерева (розгорнутість, виділення) між перемиканнями розділів.
    public partial class OrgTreeViewModel : ObservableObject
    {
        private readonly IOrgTreeService _orgTreeService;
        private readonly ICountService _countService;
        private readonly IIntakeService _intakeService;
        private readonly ActiveIntakeState _activeIntakeState;
        private readonly IDialogService _dialogService;

        private readonly Dictionary<int, OrgNodeViewModel> _byId = new();
        private Dictionary<int, Intake> _intakesById = new();
        private bool _loaded;
        private bool _suppressSelection;

        public OrgTreeViewModel(
            IOrgTreeService orgTreeService,
            ICountService countService,
            IIntakeService intakeService,
            ActiveIntakeState activeIntakeState,
            IDialogService dialogService)
        {
            _orgTreeService = orgTreeService;
            _countService = countService;
            _intakeService = intakeService;
            _activeIntakeState = activeIntakeState;
            _dialogService = dialogService;

            WeakReferenceMessenger.Default.Register<CountsChangedMessage>(this,
                (_, _) => _ = RefreshCountsAsync());
        }

        public ObservableCollection<OrgNodeViewModel> RootNodes { get; } = new();

        // Перевірка незбережених змін картки перед зміною контексту; ставить PersonnelViewModel.
        public Func<Task<bool>>? LeaveGuard { get; set; }

        public event Action<OrgNodeViewModel?>? SelectedNodeChanged;

        [ObservableProperty]
        private OrgNodeViewModel? selectedNode;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsFilterActive))]
        [NotifyPropertyChangedFor(nameof(IsFilterAll))]
        [NotifyPropertyChangedFor(nameof(IsFilterArchived))]
        private TreeFilterMode filterMode = TreeFilterMode.Active;

        public bool IsFilterActive => FilterMode == TreeFilterMode.Active;
        public bool IsFilterAll => FilterMode == TreeFilterMode.All;
        public bool IsFilterArchived => FilterMode == TreeFilterMode.Archived;

        [ObservableProperty]
        private bool showDescendants = true;

        public async Task EnsureLoadedAsync()
        {
            if (_loaded) return;
            await ReloadAsync();
            _loaded = true;

            var first = RootNodes.FirstOrDefault();
            if (first is not null && SelectedNode is null)
            {
                first.IsExpanded = true;
                first.IsSelected = true;
            }
        }

        public async Task ReloadAsync()
        {
            var expandedIds = _byId.Values.Where(n => n.IsExpanded).Select(n => n.Id).ToHashSet();
            var selectedId = SelectedNode?.Id;

            var nodes = await _orgTreeService.GetAllAsync();
            var intakeIds = nodes.Where(n => n.IntakeId != null).Select(n => n.IntakeId!.Value).Distinct().ToList();
            _intakesById = new Dictionary<int, Intake>();
            foreach (var id in intakeIds)
            {
                var intake = await _intakeService.GetByIdAsync(id);
                if (intake is not null) _intakesById[id] = intake;
            }

            _byId.Clear();
            RootNodes.Clear();

            foreach (var node in nodes)
            {
                var vm = new OrgNodeViewModel(node, this);
                ApplyIntakeFlags(vm);
                _byId[vm.Id] = vm;

                if (node.ParentId is int parentId && _byId.TryGetValue(parentId, out var parent))
                    parent.Children.Add(vm);
                else
                    RootNodes.Add(vm);
            }

            foreach (var id in expandedIds)
                if (_byId.TryGetValue(id, out var vm)) vm.IsExpanded = true;

            ApplyFilter();
            await RefreshCountsAsync();

            if (selectedId is int sid && _byId.TryGetValue(sid, out var selVm))
            {
                _suppressSelection = true;
                selVm.IsSelected = true;
                _suppressSelection = false;
                SelectedNode = selVm;
            }
        }

        private void ApplyIntakeFlags(OrgNodeViewModel vm)
        {
            if (vm.IntakeId is int intakeId && _intakesById.TryGetValue(intakeId, out var intake))
            {
                vm.IntakeStatus = intake.Status;
                vm.IsIntakeRoot = intake.RootOrgNodeId == vm.Id;
                vm.IsCompletedBranch = intake.Status == IntakeStatus.Completed;
                vm.IsActiveIntakeBranch = intake.Status == IntakeStatus.Active;
            }
            else
            {
                vm.IntakeStatus = null;
                vm.IsIntakeRoot = false;
                vm.IsCompletedBranch = false;
                vm.IsActiveIntakeBranch = false;
            }
        }

        public async Task RefreshCountsAsync()
        {
            var ownCounts = await _countService.GetTreeCountsAsync();

            foreach (var vm in _byId.Values)
            {
                vm.OwnCount = 0;
                vm.TotalCount = 0;
            }

            foreach (var (nodeId, count) in ownCounts)
            {
                if (!_byId.TryGetValue(nodeId, out var vm)) continue;
                vm.OwnCount = count;

                for (var current = vm; current is not null;
                     current = current.ParentId is int pid ? _byId.GetValueOrDefault(pid) : null)
                {
                    current.TotalCount += count;
                }
            }
        }

        [RelayCommand]
        private void SetFilter(string mode)
        {
            FilterMode = mode switch
            {
                "All" => TreeFilterMode.All,
                "Archived" => TreeFilterMode.Archived,
                _ => TreeFilterMode.Active
            };
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            foreach (var vm in _byId.Values)
            {
                vm.IsVisibleInFilter = vm.IntakeStatus is not IntakeStatus status || FilterMode switch
                {
                    TreeFilterMode.Active => status is IntakeStatus.Active or IntakeStatus.Planned,
                    TreeFilterMode.Archived => status == IntakeStatus.Completed,
                    _ => true
                };
            }

            if (SelectedNode is not null && !SelectedNode.IsVisibleInFilter)
            {
                var ancestor = SelectedNode.ParentId is int pid ? _byId.GetValueOrDefault(pid) : null;
                while (ancestor is not null && !ancestor.IsVisibleInFilter)
                    ancestor = ancestor.ParentId is int apid ? _byId.GetValueOrDefault(apid) : null;

                if (ancestor is not null)
                {
                    _suppressSelection = true;
                    SelectedNode.IsSelected = false;
                    _suppressSelection = false;
                    ancestor.IsSelected = true;
                }
            }
        }

        internal async void OnNodeSelected(OrgNodeViewModel node)
        {
            if (_suppressSelection) return;
            var previous = SelectedNode;
            if (ReferenceEquals(previous, node)) return;

            var canLeave = LeaveGuard is null || await LeaveGuard();
            if (!canLeave)
            {
                _suppressSelection = true;
                node.IsSelected = false;
                if (previous is not null) previous.IsSelected = true;
                _suppressSelection = false;
                return;
            }

            SelectedNode = node;
            SelectedNodeChanged?.Invoke(node);
        }

        // Ланцюжок назв від кореня до вузла (для брейдкрамба й тултипів).
        public List<OrgNodeViewModel> GetAncestryChain(OrgNodeViewModel node)
        {
            var chain = new List<OrgNodeViewModel>();
            for (var current = node; current is not null;
                 current = current.ParentId is int pid ? _byId.GetValueOrDefault(pid) : null)
            {
                chain.Insert(0, current);
            }
            return chain;
        }

        public string GetFullPathNames(OrgNodeViewModel node)
            => string.Join(" › ", GetAncestryChain(node).Select(n => n.Name));

        public OrgNodeViewModel? FindById(int id) => _byId.GetValueOrDefault(id);

        public Intake? GetIntake(int intakeId) => _intakesById.GetValueOrDefault(intakeId);

        public void SelectNode(OrgNodeViewModel node)
        {
            for (var current = node.ParentId is int pid ? _byId.GetValueOrDefault(pid) : null;
                 current is not null;
                 current = current.ParentId is int apid ? _byId.GetValueOrDefault(apid) : null)
            {
                current.IsExpanded = true;
            }
            node.IsSelected = true;
        }

        [RelayCommand]
        private async Task AddChildAsync(OrgNodeViewModel? node)
        {
            if (node is null) return;

            var vm = new NodeEditDialogViewModel("Нова вкладена папка");
            if (_dialogService.ShowDialog(vm, Application.Current.MainWindow) != true) return;

            var created = await _orgTreeService.CreateAsync(node.Id, vm.NodeName, vm.DocumentName);
            var childVm = new OrgNodeViewModel(created, this);
            ApplyIntakeFlags(childVm);
            _byId[childVm.Id] = childVm;
            node.Children.Add(childVm);
            node.IsExpanded = true;
            ApplyFilter();
            SelectNode(childVm);
        }

        [RelayCommand]
        private async Task RenameAsync(OrgNodeViewModel? node)
        {
            if (node is null || !node.CanRename) return;

            var vm = new NodeEditDialogViewModel("Перейменувати папку")
            {
                NodeName = node.Name,
                DocumentName = node.DocumentName
            };
            if (_dialogService.ShowDialog(vm, Application.Current.MainWindow) != true) return;

            await _orgTreeService.RenameAsync(node.Id, vm.NodeName, vm.DocumentName);
            node.Name = vm.NodeName.Trim();
            node.DocumentName = string.IsNullOrWhiteSpace(vm.DocumentName) ? null : vm.DocumentName.Trim();
        }

        [RelayCommand]
        private async Task MoveAsync(OrgNodeViewModel? node)
        {
            if (node is null || !node.CanMove) return;

            var picker = NodePickerDialogViewModel.ForNodeMove(this, node);
            if (_dialogService.ShowDialog(picker, Application.Current.MainWindow) != true
                || picker.SelectedTargetId is not int targetId) return;

            var newParent = _byId.GetValueOrDefault(targetId);
            var oldParent = node.ParentId is int opid ? _byId.GetValueOrDefault(opid) : null;
            if (newParent is null) return;

            try
            {
                await _orgTreeService.MoveAsync(node.Id, targetId);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Переміщення", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            oldParent?.Children.Remove(node);
            newParent.Children.Add(node);
            node.ParentId = newParent.Id;
            RecomputeSubtreePaths(node, newParent);
            newParent.IsExpanded = true;
            ApplyFilter();
            await RefreshCountsAsync();
            WeakReferenceMessenger.Default.Send(new CountsChangedMessage());
        }

        private void RecomputeSubtreePaths(OrgNodeViewModel node, OrgNodeViewModel parent)
        {
            node.Path = $"{parent.Path}{node.Id}/";
            node.Depth = parent.Depth + 1;
            if (parent.IntakeId is not null) node.IntakeId = parent.IntakeId;
            ApplyIntakeFlags(node);
            foreach (var child in node.Children)
                RecomputeSubtreePaths(child, node);
        }

        [RelayCommand]
        private async Task DeleteAsync(OrgNodeViewModel? node)
        {
            if (node is null || !node.CanDelete) return;

            var peopleCount = await _orgTreeService.CountPeopleInBranchAsync(node.Id);
            if (peopleCount > 0)
            {
                MessageBox.Show(
                    $"У гілці {peopleCount} осіб. Спершу перемістіть їх.",
                    "Видалення неможливе", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"Видалити папку «{node.Name}» до кошика?",
                "Підтвердження видалення", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            await _orgTreeService.DeleteAsync(node.Id);

            var parent = node.ParentId is int pid ? _byId.GetValueOrDefault(pid) : null;
            var selectionInBranch = SelectedNode is not null
                && SelectedNode.Path.StartsWith(node.Path, StringComparison.Ordinal);
            RemoveSubtreeFromIndex(node);
            parent?.Children.Remove(node);
            if (selectionInBranch)
            {
                SelectedNode = null;
                if (parent is not null) parent.IsSelected = true;
            }
        }

        private void RemoveSubtreeFromIndex(OrgNodeViewModel node)
        {
            _byId.Remove(node.Id);
            foreach (var child in node.Children)
                RemoveSubtreeFromIndex(child);
        }

        [RelayCommand]
        private async Task NewIntakeAsync()
        {
            var wizard = new IntakeWizardViewModel(_intakeService, this);
            await wizard.InitializeAsync();
            if (_dialogService.ShowDialog(wizard, Application.Current.MainWindow) != true
                || wizard.CreatedIntake is null) return;

            await ReloadAsync();
            await _activeIntakeState.RefreshAsync();

            var rootVm = _byId.GetValueOrDefault(wizard.CreatedIntake.RootOrgNodeId);
            if (rootVm is not null) SelectNode(rootVm);
        }
    }
}
