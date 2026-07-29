using System.Windows;
using GenDoc.ViewModels.Archive;

namespace GenDoc.Views.Archive;

public partial class ManualValuesDialog : Window
{
    public ManualValuesDialog(ManualValuesDialogViewModel viewModel)
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
