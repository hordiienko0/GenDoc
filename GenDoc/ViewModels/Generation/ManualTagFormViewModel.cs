using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GenDoc.ViewModels.Generation
{
    public enum ManualTagKind { Text, Date }

    public partial class ManualTagRowViewModel : ObservableObject
    {
        private readonly Func<DateOnly, string>? _dateFormatter;

        public ManualTagRowViewModel(string tag, string? initialValue)
        {
            Tag = tag;
            Kind = ManualTagKind.Text;
            value = initialValue ?? string.Empty;
        }

        public ManualTagRowViewModel(string tag, DateOnly initialDate, Func<DateOnly, string> formatter)
        {
            Tag = tag;
            Kind = ManualTagKind.Date;
            _dateFormatter = formatter;
            dateValue = initialDate.ToDateTime(TimeOnly.MinValue);
            value = formatter(initialDate);
        }

        public string Tag { get; }

        public string Label => Services.Generation.ManualTagLabel.Human(Tag);

        public ManualTagKind Kind { get; }

        [ObservableProperty] private string value = string.Empty;
        [ObservableProperty] private DateTime? dateValue;

        partial void OnDateValueChanged(DateTime? oldValue, DateTime? newValue)
        {
            if (Kind == ManualTagKind.Date && newValue is DateTime dt && _dateFormatter is not null)
                Value = _dateFormatter(DateOnly.FromDateTime(dt));
        }
    }

    public record StaffPickerOption(int RecipientId, string Rank, string SignatureName, string DisplayLabel);

    public partial class SignerPickerViewModel : ObservableObject
    {
        public SignerPickerViewModel(IEnumerable<StaffPickerOption> options, StaffPickerOption? initial)
        {
            Options = new ObservableCollection<StaffPickerOption>(options);
            selected = initial;
        }

        public ObservableCollection<StaffPickerOption> Options { get; }

        [ObservableProperty] private StaffPickerOption? selected;
    }

    public class ManualTagFormViewModel
    {
        public const string SignerRankTag = "звання_підписанта";
        public const string SignerNameTag = "піб_підписанта";

        public ManualTagFormViewModel(
            ObservableCollection<ManualTagRowViewModel> rows,
            SignerPickerViewModel? signer,
            SignerPickerViewModel? courseOfficer = null,
            string? signerRankTag = null,
            string? signerNameTag = null)
        {
            Rows = rows;
            Signer = signer;
            CourseOfficer = courseOfficer;

            _signerRankTag = signerRankTag ?? SignerRankTag;
            _signerNameTag = signerNameTag ?? SignerNameTag;
        }

        private readonly string _signerRankTag;
        private readonly string _signerNameTag;

        public ObservableCollection<ManualTagRowViewModel> Rows { get; }
        public SignerPickerViewModel? Signer { get; }

        public SignerPickerViewModel? CourseOfficer { get; }

        public bool HasContent => Rows.Count > 0 || Signer is not null || CourseOfficer is not null;

        public Dictionary<string, string> GetValues()
        {
            var values = Rows.ToDictionary(r => r.Tag, r => r.Value);

            if (Signer?.Selected is { } signer)
            {
                values[_signerRankTag] = signer.Rank;
                values[_signerNameTag] = signer.SignatureName;
            }

            return values;
        }
    }
}
