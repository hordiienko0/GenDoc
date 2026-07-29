using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GenDoc.ViewModels.Personnel
{
    public partial class NodeEditDialogViewModel : DialogViewModelBase
    {
        public NodeEditDialogViewModel(string title)
        {
            Title = title;
        }

        public string Title { get; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(OkCommand))]
        private string nodeName = string.Empty;

        [ObservableProperty]
        private string? documentName;

        public bool CanOk => !string.IsNullOrWhiteSpace(NodeName);

        [RelayCommand(CanExecute = nameof(CanOk))]
        private void Ok() => CloseDialog(true);

        [RelayCommand]
        private void Cancel() => CloseDialog(false);
    }
}
