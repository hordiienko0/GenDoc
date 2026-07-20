using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Services;

namespace GenDoc.ViewModels.Login;

public partial class LoginViewModel : ObservableObject
{
    private readonly IDatabaseUnlockService _unlockService;
    private readonly IUserProfileService _userProfileService;

    public LoginViewModel(IDatabaseUnlockService unlockService, IUserProfileService userProfileService)
    {
        _unlockService = unlockService;
        _userProfileService = userProfileService;
    }

    [ObservableProperty]
    private LoginStage stage = LoginStage.DatabasePassword;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private ObservableCollection<UserProfileListItem> profiles = new();

    [ObservableProperty]
    private UserProfileListItem? selectedProfile;

    [ObservableProperty]
    private string newProfileFullName = string.Empty;

    // PasswordBox навмисно не биндиться напряму (WPF не дає Password як DependencyProperty
    // з міркувань безпеки) — code-behind вікна пише сюди значення при PasswordChanged.
    public string? DatabasePassword { private get; set; }
    public string? ProfilePassword { private get; set; }
    public string? NewProfilePassword { private get; set; }
    public string? NewProfilePasswordConfirm { private get; set; }

    public bool? LoginResult { get; private set; }
    public event EventHandler? RequestClose;

    [RelayCommand]
    private void UnlockDatabase()
    {
        ErrorMessage = null;

        if (string.IsNullOrEmpty(DatabasePassword))
        {
            ErrorMessage = "Введіть пароль бази даних.";
            return;
        }

        if (!_unlockService.TryUnlock(DatabasePassword, out var error))
        {
            ErrorMessage = error;
            return;
        }

        Profiles = new ObservableCollection<UserProfileListItem>(_userProfileService.GetActiveProfiles());
        Stage = LoginStage.ProfileSelect;
    }

    [RelayCommand]
    private void SelectLogin()
    {
        ErrorMessage = null;

        if (SelectedProfile is null)
        {
            ErrorMessage = "Оберіть профіль.";
            return;
        }

        if (string.IsNullOrEmpty(ProfilePassword))
        {
            ErrorMessage = "Введіть пароль профілю.";
            return;
        }

        if (!_userProfileService.TryLogin(SelectedProfile.Id, ProfilePassword, out var error))
        {
            ErrorMessage = error;
            return;
        }

        LoginResult = true;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ShowCreateProfile()
    {
        ErrorMessage = null;
        NewProfileFullName = string.Empty;
        Stage = LoginStage.CreateProfile;
    }

    [RelayCommand]
    private void CancelCreateProfile()
    {
        ErrorMessage = null;
        Stage = LoginStage.ProfileSelect;
    }

    [RelayCommand]
    private void CreateProfile()
    {
        ErrorMessage = null;

        if (NewProfilePassword != NewProfilePasswordConfirm)
        {
            ErrorMessage = "Паролі не збігаються.";
            return;
        }

        if (!_userProfileService.TryCreateProfile(NewProfileFullName, NewProfilePassword ?? string.Empty, out var error))
        {
            ErrorMessage = error;
            return;
        }

        LoginResult = true;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}