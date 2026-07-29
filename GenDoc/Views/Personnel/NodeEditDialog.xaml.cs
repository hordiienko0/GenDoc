using System.Windows;
using GenDoc.ViewModels.Personnel;

namespace GenDoc.Views.Personnel;

public partial class NodeEditDialog : Window
{
    public NodeEditDialog(NodeEditDialogViewModel viewModel)
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
