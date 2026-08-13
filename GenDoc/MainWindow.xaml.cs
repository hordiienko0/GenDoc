using System.Windows;
using System.Windows.Input;
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

        // Наведення на згорнуту панель розкриває її поверх вмісту. Це стан миші,
        // тобто справа в'юхи; в'ю-модель лише зберігає прапорець, від якого
        // залежать ширина панелі й видимість підписів.
        private void Sidebar_MouseEnter(object sender, MouseEventArgs e)
        {
            if (DataContext is MainViewModel viewModel) viewModel.IsSidebarHovered = true;
        }

        private void Sidebar_MouseLeave(object sender, MouseEventArgs e)
        {
            if (DataContext is MainViewModel viewModel) viewModel.IsSidebarHovered = false;
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("У розробці", "GenDoc", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                MaximizeRestoreButton_Click(sender, e);
                return;
            }

            DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
