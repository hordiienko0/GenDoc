using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Models;
using GenDoc.Services.Intakes;

namespace GenDoc.Services
{
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
                return $"Активний набір: {IntakeLabel.Of(Current.Number, Current.DisplayNumber)} · день {day} з {totalDays}";
            }
        }

        public bool HasActive => Current is not null;

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
