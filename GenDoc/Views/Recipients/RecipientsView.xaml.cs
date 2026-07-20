using System.Windows.Controls;
using System.Windows.Input;
using GenDoc.Services.Recipients;
using GenDoc.ViewModels.Recipients;

namespace GenDoc.Views.Recipients;

public partial class RecipientsView : UserControl
{
    public RecipientsView()
    {
        InitializeComponent();
    }

    private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not RecipientsViewModel viewModel) return;
        if ((sender as DataGrid)?.SelectedItem is not RecipientListItem item) return;

        viewModel.EditCommand.Execute(item);
    }
}
