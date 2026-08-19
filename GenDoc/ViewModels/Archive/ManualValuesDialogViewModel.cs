using CommunityToolkit.Mvvm.Input;
using GenDoc.ViewModels.Generation;
using GenDoc.ViewModels.Personnel;

namespace GenDoc.ViewModels.Archive
{
    /// <summary>
    /// Ручні мітки для перегенерації з архіву - одна форма на весь батч.
    ///
    /// Форму будує той самий ManualTagFormBuilder, що й для звичайної генерації,
    /// і це не заради стрункості: раніше цей діалог мав власний примітивний
    /// список і через те розходився з генерацією в трьох речах - дати набиралися
    /// текстом (у документи потрапляли і «13.08.2026», і «13.8.26»), поля не
    /// підставлялися з минулого разу, і не було вибору підписанта. Тепер
    /// перегенерація поводиться так само, як генерація.
    /// </summary>
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
