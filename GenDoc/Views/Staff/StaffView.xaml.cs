using System.Windows;
using System.Windows.Controls;
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
}
