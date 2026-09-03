using CommunityToolkit.Mvvm.ComponentModel;
using GenDoc.Services;
using GenDoc.Services.Personnel;

namespace GenDoc.ViewModels.Personnel
{
    public partial class PersonRowViewModel : ObservableObject
    {
        public PersonRowViewModel(PersonListItem item)
        {
            Item = item;
        }

        public PersonListItem Item { get; private set; }

        public int Id => Item.Id;
        public string LastName => Item.LastName;
        public string FirstName => Item.FirstName;
        public string? MiddleName => Item.MiddleName;
        public string Rank => Item.Rank;

        public string RankShort => Services.RankAbbreviation.Short(Item.Rank);
        public string Position => Item.Position;
        public string FitnessDisplay => string.IsNullOrWhiteSpace(Item.FitnessCategory) ? "-" : Item.FitnessCategory;

        public bool HasFitness => !string.IsNullOrWhiteSpace(Item.FitnessCategory);

        public bool IsLimitedFitness => !FitnessCategoryHelper.IsRegular(Item.FitnessCategory);

        public string FitnessBadgeText =>
            string.Equals(Item.FitnessCategory, "обмежено придатний", StringComparison.OrdinalIgnoreCase)
                ? "обмежено"
                : FitnessDisplay;
        public string RoomDisplay => Item.RoomDisplay;
        public int OrgNodeId => Item.OrgNodeId;
        public int? IntakeId => Item.IntakeId;

        public string ShortName
        {
            get
            {
                var initials = new[] { FirstName, MiddleName }
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => $"{char.ToUpperInvariant(p![0])}.");
                var suffix = string.Join("", initials);
                var last = LastName.ToUpper(System.Globalization.CultureInfo.GetCultureInfo("uk-UA"));
                return suffix.Length > 0 ? $"{last} {suffix}" : last;
            }
        }

        public string SearchHaystack => string.Join(' ',
            new[] { LastName, FirstName, MiddleName, Position }.Where(p => !string.IsNullOrWhiteSpace(p)));

        [ObservableProperty]
        private bool isChecked;

        public void UpdateFrom(PersonListItem item)
        {
            Item = item;
            OnPropertyChanged(string.Empty);
        }
    }
}
