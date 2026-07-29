using System.Windows;
using System.Windows.Controls;
using GenDoc.ViewModels.Intakes;

namespace GenDoc.Views.Intakes;

public partial class IntakesView : UserControl
{
    public IntakesView()
    {
        InitializeComponent();
    }

    private async void IntakesView_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is IntakesViewModel vm)
            await vm.InitializeCommand.ExecuteAsync(null);
    }
}
