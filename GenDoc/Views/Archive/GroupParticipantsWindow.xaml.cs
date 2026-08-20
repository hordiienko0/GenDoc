using System.Windows;
using GenDoc.ViewModels.Archive;

namespace GenDoc.Views.Archive;

public partial class GroupParticipantsWindow : Window
{
    public GroupParticipantsWindow(GroupParticipantsViewModel viewModel)
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
