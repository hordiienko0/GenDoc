using System.Windows;
using GenDoc.ViewModels.Personnel;

namespace GenDoc.Views.Personnel;

public partial class PersonnelExportDialog : Window
{
    public PersonnelExportDialog(PersonnelExportDialogViewModel viewModel)
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
