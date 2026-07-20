using System.Windows;
using System.Windows.Controls;
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
    }

    private void OnRequestClose(object? sender, EventArgs e)
    {
        DialogResult = _viewModel.LoginResult;
        Close();
    }

    private void DatabasePasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        => _viewModel.DatabasePassword = ((PasswordBox)sender).Password;

    private void ProfilePasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        => _viewModel.ProfilePassword = ((PasswordBox)sender).Password;

    private void NewProfilePasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        => _viewModel.NewProfilePassword = ((PasswordBox)sender).Password;

    private void NewProfilePasswordConfirmBox_PasswordChanged(object sender, RoutedEventArgs e)
        => _viewModel.NewProfilePasswordConfirm = ((PasswordBox)sender).Password;
}