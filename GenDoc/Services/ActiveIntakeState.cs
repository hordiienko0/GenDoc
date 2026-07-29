using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Models;
using GenDoc.Services.Intakes;

namespace GenDoc.Services
{
    // Singleton-стан активного набору для статус-рядка й екрана «Особовий склад».
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

                var totalDays = Current.DateEnd.DayNumber - Current.DateStart.DayNumber + 1;
                var day = DateOnly.FromDateTime(DateTime.Today).DayNumber - Current.DateStart.DayNumber + 1;
                day = Math.Clamp(day, 1, totalDays);
                return $"Активний набір: №{Current.Number} · {Current.DisplayNumber} · день {day} з {totalDays}";
            }
        }

        public bool HasActive => Current is not null;

        public async Task RefreshAsync()
        {
            Current = await _intakeService.GetActiveAsync();
            WeakReferenceMessenger.Default.Send(new ActiveIntakeChangedMessage());
        }
    }
}
