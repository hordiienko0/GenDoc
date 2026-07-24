using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using GenDoc.Native;
using GenDoc.ViewModels.Login;

namespace GenDoc.Views.Login;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;

    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        DwmHelper.EnableDarkTitleBar(this);
        _viewModel = viewModel;
        DataContext = _viewModel;
        _viewModel.RequestClose += OnRequestClose;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += (_, _) => FocusStageField();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LoginViewModel.Stage)) return;

        if (_viewModel.Stage == LoginStage.ProfileSelect)
        {
            ProfilePasswordBox.Clear();
        }

        FocusStageField();
    }

    private void FocusStageField()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            switch (_viewModel.Stage)
            {
                case LoginStage.DatabasePassword:
                    DatabasePasswordBox.Focus();
                    break;
                case LoginStage.ProfileSelect:
                    ProfilePasswordBox.Focus();
                    break;
                case LoginStage.CreateProfile:
                    NewProfileFullNameBox.Focus();
                    break;
            }
        });
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        var command = _viewModel.Stage switch
        {
            LoginStage.DatabasePassword => _viewModel.UnlockDatabaseCommand,
            LoginStage.ProfileSelect => _viewModel.SelectLoginCommand,
            LoginStage.CreateProfile => _viewModel.CreateProfileCommand,
            _ => null
        };

        if (command is { } cmd && cmd.CanExecute(null))
        {
            cmd.Execute(null);
            e.Handled = true;
        }
    }

    private void OnRequestClose(object? sender, EventArgs e)
    {
        DialogResult = _viewModel.LoginResult;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void DatabasePasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        => _viewModel.DatabasePassword = ((PasswordBox)sender).Password;

    private void ProfilePasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        => _viewModel.ProfilePassword = ((PasswordBox)sender).Password;

    private void NewProfilePasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        => _viewModel.NewProfilePassword = ((PasswordBox)sender).Password;

    private void NewProfilePasswordConfirmBox_PasswordChanged(object sender, RoutedEventArgs e)
        => _viewModel.NewProfilePasswordConfirm = ((PasswordBox)sender).Password;
}