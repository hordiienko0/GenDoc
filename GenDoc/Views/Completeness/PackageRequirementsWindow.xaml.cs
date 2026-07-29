using System.Windows;
using GenDoc.ViewModels.Completeness;

namespace GenDoc.Views.Completeness;

public partial class PackageRequirementsWindow : Window
{
    public PackageRequirementsWindow(PackageRequirementsViewModel viewModel)
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
