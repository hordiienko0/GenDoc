using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GenDoc.ViewModels.Personnel;

namespace GenDoc.Views.Personnel;

public partial class PersonnelView : UserControl
{
    public static readonly RoutedCommand FocusSearchCommand = new(nameof(FocusSearchCommand), typeof(PersonnelView));

    public PersonnelView()
    {
        InitializeComponent();
    }

    private void FocusSearch_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private async void PersonnelView_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PersonnelViewModel vm)
            await vm.InitializeAsync();
    }

    private void OrgTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is OrgNodeViewModel node)
        {
            node.IsSelected = true;
            item.Focus();
        }
    }

    private void PeopleGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<CheckBox>(e.OriginalSource as DependencyObject) is not null) return;

        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is PersonRowViewModel person && DataContext is PersonnelViewModel vm)
            vm.OpenRowCommand.Execute(person);
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }
}
