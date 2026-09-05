using System.Windows;
using GenDoc.ViewModels.Archive;
using GenDoc.ViewModels.Completeness;
using GenDoc.ViewModels.Intakes;
using GenDoc.ViewModels.Personnel;
using GenDoc.ViewModels.Recipients;
using GenDoc.ViewModels.Staff;
using GenDoc.Views.Archive;
using GenDoc.Views.Completeness;
using GenDoc.Views.Intakes;
using GenDoc.Views.Personnel;
using GenDoc.Views.Recipients;
using GenDoc.Views.Staff;

namespace GenDoc.Services;

public class DialogService : IDialogService
{
    private readonly Dictionary<Type, Type> _viewModelToWindow = new()
    {
        [typeof(RecipientEditViewModel)] = typeof(RecipientEditWindow),
        [typeof(NodeEditDialogViewModel)] = typeof(NodeEditDialog),
        [typeof(NodePickerDialogViewModel)] = typeof(NodePickerDialog),
        [typeof(IntakeWizardViewModel)] = typeof(IntakeWizardWindow),
        [typeof(NoteInputDialogViewModel)] = typeof(NoteInputDialog),
        [typeof(DeleteDocumentsDialogViewModel)] = typeof(DeleteDocumentsDialog),
        [typeof(ManualValuesDialogViewModel)] = typeof(ManualValuesDialog),
        [typeof(VersionHistoryViewModel)] = typeof(VersionHistoryWindow),
        [typeof(GroupVersionHistoryViewModel)] = typeof(GroupVersionHistoryWindow),
        [typeof(PackageRequirementsViewModel)] = typeof(PackageRequirementsWindow),
        [typeof(CloseIntakeDialogViewModel)] = typeof(CloseIntakeDialog),
        [typeof(StaffDocDialogViewModel)] = typeof(StaffDocDialog),
        [typeof(GenerateDocumentsDialogViewModel)] = typeof(GenerateDocumentsDialog),
        [typeof(GroupParticipantsViewModel)] = typeof(GroupParticipantsWindow),
    };

    public bool? ShowDialog<TViewModel>(TViewModel viewModel, Window? owner = null)
        where TViewModel : notnull
    {
        if (!_viewModelToWindow.TryGetValue(viewModel.GetType(), out var windowType))
            throw new InvalidOperationException($"Для {viewModel.GetType().Name} не зареєстровано вікно діалогу.");

        var window = (Window)Activator.CreateInstance(windowType, viewModel)!;
        if (owner is not null)
        {
            window.Owner = owner;
        }

        return window.ShowDialog();
    }
}
