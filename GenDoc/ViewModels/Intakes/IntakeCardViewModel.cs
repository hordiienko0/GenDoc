using CommunityToolkit.Mvvm.ComponentModel;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Intakes;

namespace GenDoc.ViewModels.Intakes
{
    public partial class IntakeCardViewModel : ObservableObject
    {
        public IntakeCardViewModel(IntakeOverview overview)
        {
            Id = overview.Id;
            RootOrgNodeId = overview.RootOrgNodeId;
            Status = overview.Status;
            HasPackage = overview.HasPackage;
            DateStart = overview.DateStart;
            DateEnd = overview.DateEnd;
            DateClosed = overview.DateClosed;

            TitleText = $"Набір №{overview.Number}";
            CodeText = $"· {overview.DisplayNumber}";

            peopleCount = overview.PeopleCount;
            isSummaryLoading = overview.CompletenessPercent < 0;
            completenessPercent = Math.Max(overview.CompletenessPercent, 0);
            incompletePeopleCount = overview.IncompletePeopleCount;
        }

        public int Id { get; }
        public int RootOrgNodeId { get; }
        public IntakeStatus Status { get; }
        public bool HasPackage { get; }
        public DateOnly DateStart { get; }
        public DateOnly DateEnd { get; }
        public DateOnly? DateClosed { get; }

        public string TitleText { get; }
        public string CodeText { get; }

        public bool IsCompleted => Status == IntakeStatus.Completed;
        public bool IsNotCompleted => !IsCompleted;
        public bool IsActiveIntake => Status == IntakeStatus.Active;

        public string StatusText => Status switch
        {
            IntakeStatus.Active => "АКТИВНИЙ",
            IntakeStatus.Completed => "ЗАВЕРШЕНИЙ",
            _ => "ПЛАНУЄТЬСЯ"
        };

        public string PeriodText
        {
            get
            {
                var period = $"Період: {DateStart:dd.MM.yyyy} – {DateEnd:dd.MM.yyyy}";

                if (Status == IntakeStatus.Completed)
                    return DateClosed is DateOnly closed ? $"{period} · завершено {closed:dd.MM.yyyy}" : period;

                var today = DateOnly.FromDateTime(DateTime.Today);

                if (Status == IntakeStatus.Planned)
                {
                    var daysUntil = Math.Max(0, DateStart.DayNumber - today.DayNumber);
                    return $"{period} · старт через {daysUntil} {PluralHelper.Pluralize(daysUntil, "день", "дні", "днів")}";
                }

                var totalDays = DateEnd.DayNumber - DateStart.DayNumber + 1;
                var day = Math.Clamp(today.DayNumber - DateStart.DayNumber + 1, 1, totalDays);
                return $"{period} · день {day} з {totalDays}";
            }
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PeopleText))]
        private int peopleCount;

        public string PeopleText => $"{PeopleCount} {PluralHelper.Pluralize(PeopleCount, "особа", "особи", "осіб")}";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IncompleteText))]
        [NotifyPropertyChangedFor(nameof(IsSummaryReady))]
        private bool isSummaryLoading;

        public bool IsSummaryReady => !IsSummaryLoading;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CompletenessFraction))]
        private int completenessPercent;

        public double CompletenessFraction => Math.Clamp(CompletenessPercent, 0, 100) / 100.0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IncompleteText))]
        private int incompletePeopleCount;

        public string IncompleteText
        {
            get
            {
                if (!HasPackage) return "набір не сформовано";
                if (IsSummaryLoading || IncompletePeopleCount <= 0) return string.Empty;
                return $"{IncompletePeopleCount} {PluralHelper.Pluralize(IncompletePeopleCount, "особа", "особи", "осіб")} неповні";
            }
        }

        public void ApplySummary(int percent, int incompletePeople)
        {
            CompletenessPercent = percent;
            IncompletePeopleCount = incompletePeople;
            IsSummaryLoading = false;
        }
    }
}
