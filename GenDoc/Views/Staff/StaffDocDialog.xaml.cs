using System.Windows;
using GenDoc.ViewModels.Staff;

namespace GenDoc.Views.Staff;

public partial class StaffDocDialog : Window
{
    public StaffDocDialog(StaffDocDialogViewModel viewModel)
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
