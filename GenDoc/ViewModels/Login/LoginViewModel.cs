using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Services;

namespace GenDoc.ViewModels.Login;

public partial class LoginViewModel : ObservableObject
{
    private readonly IDatabaseUnlockService _unlockService;
    private readonly IUserProfileService _userProfileService;
    private readonly IDatabaseSchemaInitializer _schemaInitializer;

    public LoginViewModel(
        IDatabaseUnlockService unlockService,
        IUserProfileService userProfileService,
        IDatabaseSchemaInitializer schemaInitializer)
    {
        _unlockService = unlockService;
        _userProfileService = userProfileService;
        _schemaInitializer = schemaInitializer;
        IsNewDatabase = !unlockService.DatabaseExists;
    }

    /// <summary>
    /// Бази ще немає - перший запуск: пароль задається, а не вводиться, тож його треба повторити.
    /// Помилка в паролі нової зашифрованої бази означала б, що наступного разу її вже не відкрити.
    /// </summary>
    public bool IsNewDatabase { get; }

    public string DatabasePath => _unlockService.DatabasePath;

    public string UnlockButtonText => IsNewDatabase ? "Створити базу" : "Розблокувати";

    public Func<string, bool> ConfirmCreateDatabase { get; set; } = message =>
        System.Windows.MessageBox.Show(
            message, "GenDoc - базу даних не знайдено",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;

    [ObservableProperty]
    private LoginStage stage = LoginStage.DatabasePassword;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotBusy))]
    [NotifyCanExecuteChangedFor(nameof(UnlockDatabaseCommand))]
    private bool isBusy;

    public bool NotBusy => !IsBusy;

    [ObservableProperty]
    private ObservableCollection<UserProfileListItem> profiles = new();

    [ObservableProperty]
    private UserProfileListItem? selectedProfile;

    [ObservableProperty]
    private string newProfileFullName = string.Empty;

    public string? DatabasePassword { private get; set; }
    public string? DatabasePasswordConfirm { private get; set; }
    public string? ProfilePassword { private get; set; }
    public string? NewProfilePassword { private get; set; }
    public string? NewProfilePasswordConfirm { private get; set; }

    public bool? LoginResult { get; private set; }
    public event EventHandler? RequestClose;

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task UnlockDatabaseAsync()
    {
        ErrorMessage = null;

        if (string.IsNullOrEmpty(DatabasePassword))
        {
            ErrorMessage = "Введіть пароль бази даних.";
            return;
        }

        if (!_unlockService.DatabaseExists)
        {
            if (IsNewDatabase && DatabasePasswordConfirm != DatabasePassword)
            {
                ErrorMessage = "Паролі не збігаються.";
                return;
            }

            var agreed = ConfirmCreateDatabase(
                $"Базу даних не знайдено за шляхом\n{_unlockService.DatabasePath}\n\n"
                + "Якщо база має бути там, застосунок запущено не з тієї теки - тоді "
                + "натисніть «Ні» й перевірте розташування.\n\n"
                + "Створити нову порожню базу з цим паролем?");

            if (!agreed)
            {
                ErrorMessage = $"Базу даних не знайдено за шляхом {_unlockService.DatabasePath}.";
                return;
            }
        }

        var password = DatabasePassword;
        IsBusy = true;
        try
        {
            var outcome = await Task.Run(() =>
            {
                if (!_unlockService.TryUnlock(password, out var error))
                    return (Error: error, Profiles: (List<UserProfileListItem>?)null);

                _schemaInitializer.EnsureInitialized();
                return (Error: null, Profiles: _userProfileService.GetActiveProfiles());
            });

            if (outcome.Profiles is null)
            {
                ErrorMessage = outcome.Error;
                return;
            }

            Profiles = new ObservableCollection<UserProfileListItem>(outcome.Profiles);
            SelectedProfile = Profiles.Count == 1 ? Profiles[0] : null;
            // Профілів ще немає (перший запуск) - одразу форма створення, а не порожній список «Профіль».
            Stage = Profiles.Count == 0 ? LoginStage.CreateProfile : LoginStage.ProfileSelect;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Не вдалося відкрити базу даних: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
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

        // Профіль щойно створено з паролем, який людина двічі ввела, - TryCreateProfile уже зробив її
        // поточним користувачем. Просити той самий пароль утретє немає сенсу.
        LoginResult = true;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    public bool HasProfiles => Profiles.Count > 0;

    partial void OnProfilesChanged(ObservableCollection<UserProfileListItem> value) => OnPropertyChanged(nameof(HasProfiles));
}
