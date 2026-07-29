using System.Windows;
using GenDoc.ViewModels.Archive;

namespace GenDoc.Views.Archive;

public partial class NoteInputDialog : Window
{
    public NoteInputDialog(NoteInputDialogViewModel viewModel)
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
