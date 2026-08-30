using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Models;
using GenDoc.Services;
using GenDoc.Services.Completeness;
using GenDoc.Services.Documents;
using GenDoc.Services.Intakes;
using GenDoc.Services.Navigation;

namespace GenDoc.ViewModels.Home;

// Варіант Б (5/6): домашній розділ «Мій набір» - що зараз важливо особисто
// для мене: активний набір, готовність документів, останній запуск. Замість
// того, щоб самому йти по «Наборах», «Комплектності» й «Архіві» - зведення
// одразу з переходами в потрібний розділ.
public partial class HomeViewModel : ObservableObject
{
    private readonly ActiveIntakeState _activeIntakeState;
    private readonly ICompletenessService _completenessService;
    private readonly IDocumentArchiveService _archive;
    private readonly IIntakeService _intakeService;
    private readonly ICurrentUserContext _currentUser;

    private int _lastRunId;

    public HomeViewModel(
        ActiveIntakeState activeIntakeState,
        ICompletenessService completenessService,
        IDocumentArchiveService archive,
        IIntakeService intakeService,
        ICurrentUserContext currentUser)
    {
        _activeIntakeState = activeIntakeState;
        _completenessService = completenessService;
        _archive = archive;
        _intakeService = intakeService;
        _currentUser = currentUser;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoIntake))]
    private bool hasIntake;

    // Порожній стан картки малюється окремою панеллю, а не тим самим слотом:
    // ContentControl із заданим ContentTemplate малює шаблон навіть при
    // Content = null, тож видимість обох станів задаємо явно.
    public bool HasNoIntake => !HasIntake;

    [ObservableProperty]
    private string intakeTitle = string.Empty;

    [ObservableProperty]
    private string peopleCountText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MissingCountText))]
    private int missingCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StaleCountText))]
    private int staleCount;

    // «Бракує» і «застарілих» - різні речі, і бейдж навігації складає їх в одне
    // число. На картці вони мусять бути розділені, інакше напис «Бракує
    // документів: 12» називає бракуючими ті, що насправді є, просто застаріли
    // (аудит 2026-08-28).
    public string MissingCountText => $"Бракує документів: {MissingCount}";

    public string StaleCountText => StaleCount == 0
        ? "Застарілих немає"
        : $"Застарілих: {StaleCount}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoLastRun))]
    private bool hasLastRun;

    public bool HasNoLastRun => !HasLastRun;

    [ObservableProperty]
    private string lastRunText = string.Empty;

    public async Task InitializeAsync()
    {
        await _activeIntakeState.RefreshAsync();
        var intake = _activeIntakeState.Current;
        HasIntake = intake is not null;

        if (intake is not null)
        {
            IntakeTitle = BuildIntakeTitle(intake, DateOnly.FromDateTime(DateTime.Today));

            var overviews = await _intakeService.GetOverviewsAsync();
            var peopleCount = overviews.FirstOrDefault(o => o.Id == intake.Id)?.PeopleCount ?? 0;
            PeopleCountText = $"{peopleCount} {PluralHelper.Pluralize(peopleCount, "особа", "особи", "осіб")}";

            var (missing, stale) = await _completenessService.GetBadgeBreakdownAsync();
            MissingCount = missing;
            StaleCount = stale;
        }
        else
        {
            IntakeTitle = string.Empty;
            PeopleCountText = string.Empty;
            MissingCount = 0;
            StaleCount = 0;
        }

        // Картка називається «Мій набір», тож і запуск має бути з нього: із
        // intakeId = null сюди потрапляв останній прогін по БУДЬ-ЯКОМУ набору, і
        // «Показати в архіві» вело на чужий (аудит 2026-08-28). Запуски без
        // набору (постійний склад) сервіс однаково домішує - так задумано.
        var runs = await _archive.GetRunsAsync(intake?.Id, null, _currentUser.CurrentUserId);
        var lastRun = runs.FirstOrDefault();
        HasLastRun = lastRun is not null;

        if (lastRun is not null)
        {
            _lastRunId = lastRun.Id;
            // GetRunsAsync повертає "Вибірково" замість назви пакета для запусків
            // без пакета (v24) - тут перетворюємо назад на null, щоб текст
            // складався за тим самим правилом, що й показ у "Архіві".
            var packageName = lastRun.PackageName == "Вибірково" ? null : lastRun.PackageName;
            LastRunText = BuildLastRunText(lastRun.RunAt, packageName, lastRun.GeneratedCount);
        }
        else
        {
            _lastRunId = 0;
            LastRunText = string.Empty;
        }
    }

    internal static string BuildIntakeTitle(Intake intake, DateOnly today)
    {
        var (day, total) = ActiveIntakeState.DayOfTotal(intake, today);
        return $"{intake.DisplayNumber} · день {day} з {total}";
    }

    internal static string BuildLastRunText(DateTime runAt, string? packageName, int generated)
    {
        var packagePart = packageName is null ? "Вибірково" : $"пакет «{packageName}»";
        return $"{runAt:dd.MM.yyyy HH:mm} · {packagePart} · згенеровано {generated}";
    }

    [RelayCommand]
    private void OpenPersonnel() => WeakReferenceMessenger.Default.Send(
        new NavigateToSectionMessage(Shell.MainViewModel.PersonnelSectionTitle, null));

    [RelayCommand]
    private void OpenGeneration() => WeakReferenceMessenger.Default.Send(
        new NavigateToSectionMessage(Shell.MainViewModel.GenerationSectionTitle, null));

    [RelayCommand]
    private void OpenCompleteness() => WeakReferenceMessenger.Default.Send(
        new NavigateToSectionMessage(Shell.MainViewModel.CompletenessSectionTitle, null));

    [RelayCommand]
    private void OpenArchive() => WeakReferenceMessenger.Default.Send(
        new NavigateToSectionMessage(Shell.MainViewModel.ArchiveSectionTitle, null));

    [RelayCommand]
    private void OpenIntakes() => WeakReferenceMessenger.Default.Send(
        new NavigateToSectionMessage(Shell.MainViewModel.IntakesSectionTitle, null));

    [RelayCommand]
    private void OpenLastRun()
    {
        if (_lastRunId <= 0) return;
        WeakReferenceMessenger.Default.Send(new NavigateToSectionMessage(
            Shell.MainViewModel.ArchiveSectionTitle, new ArchiveRunNavigationPayload(_lastRunId)));
    }
}
