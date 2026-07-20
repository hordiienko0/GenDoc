using System.Windows;
using GenDoc.Native;
using GenDoc.ViewModels.Shell;

namespace GenDoc
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DwmHelper.EnableDarkTitleBar(this);
            DataContext = viewModel;
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("У розробці", "GenDoc", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
