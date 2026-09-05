using System.Windows;
using GenDoc.ViewModels.Archive;

namespace GenDoc.Views.Archive;

public partial class DeleteDocumentsDialog : Window
{
    public DeleteDocumentsDialog(DeleteDocumentsDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += (_, _) =>
        {
            DialogResult = viewModel.DialogResultValue;
            Close();
        };
    }
}
