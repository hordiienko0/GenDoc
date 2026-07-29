using System.Windows;
using GenDoc.ViewModels.Archive;

namespace GenDoc.Views.Archive;

public partial class VersionHistoryWindow : Window
{
    public VersionHistoryWindow(VersionHistoryViewModel viewModel)
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
