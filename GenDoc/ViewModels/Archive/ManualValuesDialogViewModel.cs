using CommunityToolkit.Mvvm.Input;
using GenDoc.ViewModels.Generation;
using GenDoc.ViewModels.Personnel;

namespace GenDoc.ViewModels.Archive
{
    public partial class ManualValuesDialogViewModel : DialogViewModelBase
    {
        public ManualValuesDialogViewModel(ManualTagFormViewModel form)
        {
            Form = form;
        }

        public ManualTagFormViewModel Form { get; }

        public Dictionary<string, string> GetValues() => Form.GetValues();

        [RelayCommand]
        private void Ok() => CloseDialog(true);

        [RelayCommand]
        private void Cancel() => CloseDialog(false);
    }
}
