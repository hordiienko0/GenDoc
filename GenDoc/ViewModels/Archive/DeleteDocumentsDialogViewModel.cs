using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.ViewModels.Personnel;

namespace GenDoc.ViewModels.Archive
{
    public partial class DeleteDocumentsDialogViewModel : DialogViewModelBase
    {
        public DeleteDocumentsDialogViewModel(string message, int totalVersions, int selectedCount)
        {
            Message = message;
            TotalVersions = totalVersions;
            ShowAllVersionsOption = totalVersions > selectedCount;
            AllVersionsLabel = $"Видалити всі версії ({totalVersions})";
        }

        public string Message { get; }
        public int TotalVersions { get; }
        public bool ShowAllVersionsOption { get; }
        public string AllVersionsLabel { get; }

        [ObservableProperty]
        private bool deleteAllVersions;

        [RelayCommand]
        private void Ok() => CloseDialog(true);

        [RelayCommand]
        private void Cancel() => CloseDialog(false);
    }
}
