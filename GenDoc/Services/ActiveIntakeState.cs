using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Models;
using GenDoc.Services.Intakes;

namespace GenDoc.Services
{
    // Singleton-стан активного набору для статус-рядка й екрана «Особовий склад».
    //
    // Активний набір ОДИН на всю базу, а не по профілю (рішення користувача
    // 2026-08-31). Раніше кожен профіль міг обрати собі свій через «Зробити
    // моїм», і два курсові бачили на одному екрані різні числа. Тепер набір
    // визначають дати: GetActiveAsync сам переводить Planned → Active →
    // Completed і віддає найсвіжіший активний.
    public class ActiveIntakeState
    {
        private readonly IIntakeService _intakeService;

        public ActiveIntakeState(IIntakeService intakeService)
        {
            _intakeService = intakeService;
        }

        public Intake? Current { get; private set; }

        public string StatusText
        {
            get
            {
                if (Current is null) return "Активного набору немає";

                var (day, totalDays) = DayOfTotal(Current, DateOnly.FromDateTime(DateTime.Today));
                return $"Активний набір: №{Current.Number} · {Current.DisplayNumber} · день {day} з {totalDays}";
            }
        }

        public bool HasActive => Current is not null;

        // Скільки днів набору минуло і скільки їх усього - інклюзивний підрахунок
        // (день старту й день завершення рахуються обидва), спільний для
        // статус-рядка й картки набору на «Мій набір» (5/6) - формулу не дублюємо.
        public static (int Day, int Total) DayOfTotal(Intake intake, DateOnly today)
        {
            var total = intake.DateEnd.DayNumber - intake.DateStart.DayNumber + 1;
            var day = today.DayNumber - intake.DateStart.DayNumber + 1;
            day = Math.Clamp(day, 1, total);
            return (day, total);
        }

        public async Task RefreshAsync()
        {
            Current = await _intakeService.GetActiveAsync();
            WeakReferenceMessenger.Default.Send(new ActiveIntakeChangedMessage());
        }
    }
}
