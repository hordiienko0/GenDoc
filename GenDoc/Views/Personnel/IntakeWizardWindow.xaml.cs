using System.Windows;
using GenDoc.ViewModels.Personnel;

namespace GenDoc.Views.Personnel;

public partial class IntakeWizardWindow : Window
{
    public IntakeWizardWindow(IntakeWizardViewModel viewModel)
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
