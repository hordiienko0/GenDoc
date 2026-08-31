using System.ComponentModel;
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

        // Guard спрацьовував лише на перехід МІЖ розділами: хрестик закривав
        // вікно одразу, і незбережена картка, складання чи майстер імпорту
        // гинули без запитання (аудит 2026-08-28).
        //
        // Скасування - через Cancel, а не через повторний Close(): TryLeave
        // показує модальне вікно, і вихід із Closing із уже початим закриттям
        // призвів би до рекурсії. Тому перший прохід завжди скасовує закриття,
        // а після згоди воно повторюється прапорцем _closeConfirmed.
        private bool _closeConfirmed;

        private async void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_closeConfirmed) return;
            if (DataContext is not MainViewModel viewModel) return;

            e.Cancel = true;
            if (!await viewModel.TryLeaveCurrentSectionAsync()) return;

            _closeConfirmed = true;
            Close();
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
