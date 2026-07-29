using System.Windows;
using GenDoc.ViewModels.Personnel;

namespace GenDoc.Views.Personnel;

public partial class NodePickerDialog : Window
{
    public NodePickerDialog(NodePickerDialogViewModel viewModel)
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
