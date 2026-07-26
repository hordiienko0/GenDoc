using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using GenDoc.Services.Recipients;
using GenDoc.ViewModels.Recipients;

namespace GenDoc.Views.Recipients;

public partial class RecipientsView : UserControl
{
    public RecipientsView()
    {
        InitializeComponent();
        Loaded += (_, _) => SearchTextBox.Focus();
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        if (button.ContextMenu is null) return;

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = PlacementMode.Bottom;
        button.ContextMenu.IsOpen = true;
    }

    private void DataGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not RecipientsViewModel viewModel) return;
        if (e.Column.SortMemberPath is not string key) return;
        if (!Enum.TryParse<RecipientSortColumn>(key, out var column)) return;

        viewModel.SortByColumn(column);
    }

    private void DataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not RecipientsViewModel viewModel) return;
        if (sender is not DataGrid grid) return;
        if (grid.SelectedItem is not RecipientRowViewModel item) return;

        if (e.Key == Key.Enter)
        {
            viewModel.EditCommand.Execute(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            viewModel.DeleteRecipientCommand.Execute(item);
            e.Handled = true;
        }
    }
}
