using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GenDoc.ViewModels.Duty;

namespace GenDoc.Views.Duty;

public partial class DutyRosterView : UserControl
{
    private bool _painting;

    public DutyRosterView()
    {
        InitializeComponent();
    }

    private async void DutyRosterView_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is DutyRosterViewModel vm)
            await vm.InitializeCommand.ExecuteAsync(null);
    }

    /// <summary>Натиск на клітинку починає мазок пензлем; далі мишка «фарбує» клітинки, над якими проходить.</summary>
    private void RowsHost_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not DutyRosterViewModel vm) return;
        if (CellAt(e.OriginalSource as DependencyObject) is not { } cell) return;

        _painting = true;
        vm.BeginPaint(cell);
        RowsHost.CaptureMouse();
        e.Handled = true;
    }

    private async void RowsHost_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_painting || DataContext is not DutyRosterViewModel vm) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            await FinishPaintAsync();
            return;
        }

        if (CellAt(RowsHost.InputHitTest(e.GetPosition(RowsHost)) as DependencyObject) is { } cell)
            vm.ContinuePaint(cell);
    }

    private async void RowsHost_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_painting) return;
        e.Handled = true;
        await FinishPaintAsync();
    }

    private async void RowsHost_LostMouseCapture(object sender, MouseEventArgs e) => await FinishPaintAsync();

    private async Task FinishPaintAsync()
    {
        if (!_painting) return;
        _painting = false;
        if (ReferenceEquals(Mouse.Captured, RowsHost)) RowsHost.ReleaseMouseCapture();
        if (DataContext is DutyRosterViewModel vm) await vm.EndPaintAsync();
    }

    private static DutyCellViewModel? CellAt(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is FrameworkElement { DataContext: DutyCellViewModel cell }) return cell;
            if (element is FrameworkElement { DataContext: DutyRowViewModel }) return null;
            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }
        return null;
    }
}
