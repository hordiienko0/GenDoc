using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Services;
using GenDoc.Services.Intakes;
using GenDoc.Services.Navigation;
using GenDoc.ViewModels.Personnel;
using GenDoc.ViewModels.Shell;

namespace GenDoc.ViewModels.Intakes
{
    public record GraduateTargetOption(int NodeId, string Name);

    public partial class CloseIntakeDialogViewModel : DialogViewModelBase
    {
        private IntakeCloseInfo? _info;

        [ObservableProperty] private string headerText = string.Empty;
        [ObservableProperty] private bool hasIncomplete;
        [ObservableProperty] private string incompleteText = string.Empty;
        [ObservableProperty] private string releaseRoomsText = string.Empty;

        public ObservableCollection<GraduateTargetOption> GraduateTargets { get; } = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
        private bool releaseRooms = true;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
        private bool movePersonnel;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
        private GraduateTargetOption? selectedTarget;

        [ObservableProperty] private string? errorText;

        public void Initialize(IntakeCloseInfo info)
        {
            _info = info;
            HeaderText = $"Закрити {info.DisplayNumber}";
            HasIncomplete = info.IncompletePeopleCount > 0;
            IncompleteText = HasIncomplete
                ? $"{info.IncompletePeopleCount} {PluralHelper.Pluralize(info.IncompletePeopleCount, "особа", "особи", "осіб")} " +
                  "без повного комплекту документів"
                : string.Empty;
            ReleaseRoomsText = $"Звільнити кімнати, закріплені за набором " +
                $"({info.OccupiedRoomCount} {PluralHelper.Pluralize(info.OccupiedRoomCount, "кімната", "кімнати", "кімнат")})";

            GraduateTargets.Clear();
            foreach (var (nodeId, name) in info.GraduateTargets)
                GraduateTargets.Add(new GraduateTargetOption(nodeId, name));

            ReleaseRooms = true;
            MovePersonnel = false;
            SelectedTarget = GraduateTargets.FirstOrDefault();
            ErrorText = null;
        }

        private bool CanConfirm => !MovePersonnel || SelectedTarget is not null;

        [RelayCommand(CanExecute = nameof(CanConfirm))]
        private void Confirm() => CloseDialog(true);

        [RelayCommand]
        private void Cancel() => CloseDialog(false);

        [RelayCommand]
        private void ViewMatrix()
        {
            if (_info is null) return;
            var intakeId = _info.IntakeId;
            CloseDialog(false);
            WeakReferenceMessenger.Default.Send(new NavigateToSectionMessage(
                MainViewModel.CompletenessSectionTitle, new IntakeNavigationPayload(intakeId, 0, null)));
        }

        public IntakeCloseRequest BuildRequest() => new(
            _info!.IntakeId, ReleaseRooms, MovePersonnel,
            MovePersonnel ? SelectedTarget?.NodeId : null);
    }
}
