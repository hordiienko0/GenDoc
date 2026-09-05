using System.ComponentModel;
using System.Windows.Threading;
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

        private bool _closeConfirmed;

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_closeConfirmed) return;
            if (DataContext is not MainViewModel viewModel) return;

            e.Cancel = true;

            Dispatcher.BeginInvoke(new Action(async () =>
            {
                if (!await viewModel.TryLeaveCurrentSectionAsync()) return;

                _closeConfirmed = true;
                Close();
            }), DispatcherPriority.Background);
        }

        private void Sidebar_MouseEnter(object sender, MouseEventArgs e)
        {
            if (DataContext is MainViewModel viewModel) viewModel.IsSidebarHovered = true;
        }

        private void Sidebar_MouseLeave(object sender, MouseEventArgs e)
        {
            if (DataContext is MainViewModel viewModel) viewModel.IsSidebarHovered = false;
        }

        internal static string AboutText(Version? version, string logsDirectory)
        {
            var versionText = version is null ? "-" : $"{version.Major}.{version.Minor}.{version.Build}";
            return $"GenDoc - Облік особового складу\nВерсія {versionText}\n\nЖурнали помилок:\n{logsDirectory}";
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            MessageBox.Show(AboutText(version, GenDoc.Services.ErrorLog.DefaultDirectory), "Про програму",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OpenLogs_Click(object sender, RoutedEventArgs e)
        {
            var directory = GenDoc.Services.ErrorLog.DefaultDirectory;
            try
            {
                System.IO.Directory.CreateDirectory(directory);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = directory, UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не вдалося відкрити теку журналів:\n{directory}\n{ex.Message}", "Журнали",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void SwitchProfile_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel viewModel) return;
            var confirm = MessageBox.Show(
                "Програму буде закрито й відкрито знову з вікном входу. Продовжити?",
                "Змінити профіль", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
            if (!await viewModel.TryLeaveCurrentSectionAsync()) return;

            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = exe, UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не вдалося перезапустити програму: {ex.Message}", "Змінити профіль",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            _closeConfirmed = true;
            Close();
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
