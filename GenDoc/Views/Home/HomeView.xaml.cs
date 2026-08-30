using System.Windows;
using System.Windows.Controls;
using GenDoc.ViewModels.Home;

namespace GenDoc.Views.Home;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    private async void HomeView_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is HomeViewModel vm)
            await vm.InitializeAsync();
    }
}
