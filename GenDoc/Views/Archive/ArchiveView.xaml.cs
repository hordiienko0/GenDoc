using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GenDoc.ViewModels.Archive;

namespace GenDoc.Views.Archive;

public partial class ArchiveView : UserControl
{
    public ArchiveView()
    {
        InitializeComponent();
    }

    private async void ArchiveView_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ArchiveViewModel vm)
            await vm.InitializeAsync();
    }

    private void DocsGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<CheckBox>(e.OriginalSource as DependencyObject) is not null) return;

        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is ArchiveRowViewModel rowVm && DataContext is ArchiveViewModel vm)
            vm.HandleRowClick(rowVm, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
    }

    private async void DocsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<CheckBox>(e.OriginalSource as DependencyObject) is not null) return;

        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is ArchiveRowViewModel rowVm && DataContext is ArchiveViewModel vm)
            await vm.OpenByRowAsync(rowVm);
    }

    private void GroupGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<CheckBox>(e.OriginalSource as DependencyObject) is not null) return;

        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is GroupDocumentRowViewModel rowVm && DataContext is ArchiveViewModel vm)
            vm.HandleGroupRowClick(rowVm, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
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
