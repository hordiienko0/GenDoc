using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.ViewModels.Personnel;

namespace GenDoc.ViewModels.Archive
{
    public partial class NoteInputDialogViewModel : DialogViewModelBase
    {
        public NoteInputDialogViewModel(string title, string fileName)
        {
            Title = title;
            FileName = fileName;
        }

        public string Title { get; }
        public string FileName { get; }

        [ObservableProperty]
        private string? note;

        [RelayCommand]
        private void Ok() => CloseDialog(true);

        [RelayCommand]
        private void Cancel() => CloseDialog(false);
    }
}
