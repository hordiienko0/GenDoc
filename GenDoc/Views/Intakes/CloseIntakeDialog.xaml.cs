using System.Windows;
using GenDoc.ViewModels.Intakes;

namespace GenDoc.Views.Intakes;

public partial class CloseIntakeDialog : Window
{
    public CloseIntakeDialog(CloseIntakeDialogViewModel viewModel)
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
