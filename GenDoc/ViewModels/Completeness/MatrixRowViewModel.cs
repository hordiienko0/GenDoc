using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using GenDoc.Models;

namespace GenDoc.ViewModels.Completeness
{
    public partial class MatrixRowViewModel : ObservableObject
    {
        private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("uk-UA");

        public MatrixRowViewModel(Recipient recipient)
        {
            RecipientId = recipient.Id;
            FullName = string.Join(' ', new[] { recipient.LastName, recipient.FirstName, recipient.MiddleName }
                .Where(p => !string.IsNullOrWhiteSpace(p)));
            SubText = string.Join(" · ", new[]
            {
                recipient.Rank,
                recipient.OrgNode?.DocumentName ?? recipient.OrgNode?.Name ?? recipient.Unit?.Name
            }.Where(p => !string.IsNullOrWhiteSpace(p)));
            FitnessCategory = recipient.FitnessCategory ?? "придатний";
        }

        public int RecipientId { get; }
        public string FullName { get; }
        public string SubText { get; }
        public string FitnessCategory { get; }

        public string SearchHaystack => FullName;

        public ObservableCollection<MatrixCellViewModel> Cells { get; } = new();

        [ObservableProperty]
        private bool isChecked;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ReadyText))]
        [NotifyPropertyChangedFor(nameof(ReadyRatio))]
        [NotifyPropertyChangedFor(nameof(HasMissingRequired))]
        [NotifyPropertyChangedFor(nameof(FirstColumnBackground))]
        [NotifyPropertyChangedFor(nameof(ReadyBarFill))]
        private int requiredPresent;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ReadyText))]
        [NotifyPropertyChangedFor(nameof(ReadyRatio))]
        [NotifyPropertyChangedFor(nameof(ReadyBarFill))]
        private int requiredTotal;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasMissingRequired))]
        [NotifyPropertyChangedFor(nameof(FirstColumnBackground))]
        [NotifyPropertyChangedFor(nameof(ReadyBarFill))]
        private int missingRequiredCount;

        public string ReadyText => $"{RequiredPresent} з {RequiredTotal}";
        public double ReadyRatio => RequiredTotal == 0 ? 1.0 : (double)RequiredPresent / RequiredTotal;
        public bool HasMissingRequired => MissingRequiredCount > 0;

        // Ледь помітний теплий фон колонки «ОСОБА», якщо в рядку є хоч одна відсутня обов'язкова клітинка.
        public Brush FirstColumnBackground => HasMissingRequired
            ? (Application.Current.Resources["WarningSoftBrush"] as Brush ?? Brushes.Transparent)
            : Brushes.Transparent;

        public Brush ReadyBarFill => HasMissingRequired
            ? (Application.Current.Resources["WarningBrush"] as Brush ?? Brushes.Gray)
            : ReadyRatio >= 1.0
                ? (Application.Current.Resources["SuccessBrush"] as Brush ?? Brushes.Green)
                : (Application.Current.Resources["AccentBrush"] as Brush ?? Brushes.Blue);

        public void RecomputeReadiness()
        {
            var required = Cells.Where(c => c.Requirement == Models.Enums.TemplateRequirement.Required).ToList();
            RequiredTotal = required.Count;
            RequiredPresent = required.Count(c => c.IsSatisfied);
            MissingRequiredCount = required.Count(c => c.IsMissingRequired);
        }
    }
}
