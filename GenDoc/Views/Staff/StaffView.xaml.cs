using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GenDoc.ViewModels.Staff;

namespace GenDoc.Views.Staff;

public partial class StaffView : UserControl
{
    public StaffView()
    {
        InitializeComponent();
    }

    private async void StaffView_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is StaffViewModel vm)
            await vm.InitializeCommand.ExecuteAsync(null);
    }

    private void StaffGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<CheckBox>(e.OriginalSource as DependencyObject) is not null) return;

        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is StaffRowViewModel person && DataContext is StaffViewModel vm)
            vm.OpenCardCommand.Execute(person);
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
