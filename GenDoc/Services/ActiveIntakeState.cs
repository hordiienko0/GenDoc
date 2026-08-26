using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Models;
using GenDoc.Services.Intakes;

namespace GenDoc.Services
{
    // Singleton-стан активного набору для статус-рядка й екрана «Особовий склад».
    public class ActiveIntakeState
    {
        private readonly IIntakeService _intakeService;
        private readonly IUserSettingsService _userSettings;

        public ActiveIntakeState(IIntakeService intakeService, IUserSettingsService userSettings)
        {
            _intakeService = intakeService;
            _userSettings = userSettings;
        }

        public Intake? Current { get; private set; }

        public string StatusText
        {
            get
            {
                if (Current is null) return "Активного набору немає";

                var totalDays = Current.DateEnd.DayNumber - Current.DateStart.DayNumber + 1;
                var day = DateOnly.FromDateTime(DateTime.Today).DayNumber - Current.DateStart.DayNumber + 1;
                day = Math.Clamp(day, 1, totalDays);
                return $"Активний набір: №{Current.Number} · {Current.DisplayNumber} · день {day} з {totalDays}";
            }
        }

        public bool HasActive => Current is not null;

        public async Task RefreshAsync()
        {
            var settings = await _userSettings.GetForCurrentUserAsync();
            Intake? mine = settings.ActiveIntakeId is int id
                ? await _intakeService.GetByIdAsync(id)   // null, якщо набір видалили
                : null;
            Current = Pick(mine, mine is null ? await _intakeService.GetActiveAsync() : null);
            WeakReferenceMessenger.Default.Send(new ActiveIntakeChangedMessage());
        }

        // Чисте правило: свій набір, поки він існує; інакше глобальний активний.
        internal static Intake? Pick(Intake? mine, Intake? globalActive) => mine ?? globalActive;
    }
}
