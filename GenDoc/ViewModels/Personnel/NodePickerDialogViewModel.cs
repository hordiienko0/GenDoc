using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GenDoc.ViewModels.Personnel
{
    public partial class PickerNodeViewModel : ObservableObject
    {
        public PickerNodeViewModel(int id, string name, int? intakeId, bool isEnabled)
        {
            Id = id;
            Name = name;
            IntakeId = intakeId;
            IsEnabled = isEnabled;
        }

        public int Id { get; }
        public string Name { get; }
        public int? IntakeId { get; }
        public bool IsEnabled { get; }
        public ObservableCollection<PickerNodeViewModel> Children { get; } = new();

        [ObservableProperty]
        private bool isExpanded = true;

        [ObservableProperty]
        private bool isSelected;

        public NodePickerDialogViewModel? Owner { get; set; }

        partial void OnIsSelectedChanged(bool value)
        {
            if (value) Owner?.OnPicked(this);
        }
    }

    public partial class NodePickerDialogViewModel : DialogViewModelBase
    {
        private readonly Func<PickerNodeViewModel, string?>? _warningProvider;

        private NodePickerDialogViewModel(string title, Func<PickerNodeViewModel, string?>? warningProvider)
        {
            Title = title;
            _warningProvider = warningProvider;
        }

        public string Title { get; }
        public ObservableCollection<PickerNodeViewModel> RootNodes { get; } = new();

        [ObservableProperty]
        private string? errorText;

        [ObservableProperty]
        private string? warningText;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(OkCommand))]
        private int? selectedTargetId;

        public bool CanOk => SelectedTargetId is not null && ErrorText is null;

        internal void OnPicked(PickerNodeViewModel node)
        {
            if (!node.IsEnabled)
            {
                SelectedTargetId = null;
                ErrorText = "Цю папку не можна обрати як ціль.";
                WarningText = null;
                OkCommand.NotifyCanExecuteChanged();
                return;
            }

            ErrorText = null;
            SelectedTargetId = node.Id;
            WarningText = _warningProvider?.Invoke(node);
            OkCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(CanOk))]
        private void Ok() => CloseDialog(true);

        [RelayCommand]
        private void Cancel() => CloseDialog(false);

        // Переміщення папки: заборонені сам вузол, його нащадки (Path-префікс)
        // і папки чужого набору, якщо вузол належить набору.
        public static NodePickerDialogViewModel ForNodeMove(OrgTreeViewModel tree, OrgNodeViewModel moving)
        {
            var dialog = new NodePickerDialogViewModel($"Перемістити «{moving.Name}» до…", null);
            dialog.Build(tree, vm =>
                !vm.Path.StartsWith(moving.Path, StringComparison.Ordinal)
                && (moving.IntakeId is null || vm.IntakeId is null || vm.IntakeId == moving.IntakeId));
            return dialog;
        }

        // Переміщення людей: будь-який вузол; перехід між наборами - попередження.
        public static NodePickerDialogViewModel ForRecipients(
            OrgTreeViewModel tree, IReadOnlyCollection<int?> sourceIntakeIds, int count)
        {
            var dialog = new NodePickerDialogViewModel(
                count == 1 ? "Перемістити особу до…" : $"Перемістити {count} осіб до…",
                node =>
                {
                    var crossing = sourceIntakeIds.Count(i => i != node.IntakeId);
                    return crossing > 0
                        ? $"Увага: {crossing} із {count} осіб буде переміщено між наборами."
                        : null;
                });
            dialog.Build(tree, _ => true);
            return dialog;
        }

        public static NodePickerDialogViewModel ForRestoreParent(OrgTreeViewModel tree)
        {
            var dialog = new NodePickerDialogViewModel(
                "Батьківська папка видалена - оберіть нову для відновлення", null);
            dialog.Build(tree, _ => true);
            return dialog;
        }

        private void Build(OrgTreeViewModel tree, Func<OrgNodeViewModel, bool> isEnabled)
        {
            foreach (var root in tree.RootNodes)
                RootNodes.Add(BuildNode(root, isEnabled));
        }

        private PickerNodeViewModel BuildNode(OrgNodeViewModel source, Func<OrgNodeViewModel, bool> isEnabled)
        {
            var vm = new PickerNodeViewModel(source.Id, source.Name, source.IntakeId, isEnabled(source))
            {
                Owner = this
            };
            foreach (var child in source.Children)
                vm.Children.Add(BuildNode(child, isEnabled));
            return vm;
        }
    }
}
