using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GenDoc.ViewModels.Generation
{
    public enum ManualTagKind { Text, Date, Period }

    public partial class ManualTagRowViewModel : ObservableObject
    {
        private const string PeriodLabel = "Період (з – по)";

        private readonly Func<DateOnly, string>? _dateFormatter;
        private readonly string? _labelOverride;
        private bool _syncingPeriod;

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

        private ManualTagRowViewModel(string tag, ManualTagKind kind, string? initialValue, string? labelOverride)
        {
            Tag = tag;
            Kind = kind;
            _labelOverride = labelOverride;
            value = initialValue ?? string.Empty;
        }

        public static ManualTagRowViewModel Period(string tag, string? storedValue)
        {
            var row = new ManualTagRowViewModel(tag, ManualTagKind.Period, storedValue, PeriodLabel);
            row.SyncPeriodPickersFromValue();
            return row;
        }

        public string Tag { get; }

        public string Label => _labelOverride ?? Services.Generation.ManualTagLabel.Human(Tag);

        public ManualTagKind Kind { get; }

        [ObservableProperty] private string value = string.Empty;
        [ObservableProperty] private DateTime? dateValue;
        [ObservableProperty] private DateTime? periodFrom;
        [ObservableProperty] private DateTime? periodTo;

        partial void OnDateValueChanged(DateTime? oldValue, DateTime? newValue)
        {
            if (Kind == ManualTagKind.Date && newValue is DateTime dt && _dateFormatter is not null)
                Value = _dateFormatter(DateOnly.FromDateTime(dt));
        }

        partial void OnPeriodFromChanged(DateTime? value) => WritePeriodValue();

        partial void OnPeriodToChanged(DateTime? value) => WritePeriodValue();

        partial void OnValueChanged(string value)
        {
            if (Kind == ManualTagKind.Period) SyncPeriodPickersFromValue();
        }

        private void WritePeriodValue()
        {
            if (Kind != ManualTagKind.Period || _syncingPeriod) return;

            var from = PeriodFrom ?? PeriodTo;
            var to = PeriodTo ?? PeriodFrom;
            if (from is null || to is null) return;

            var start = DateOnly.FromDateTime(from.Value);
            var end = DateOnly.FromDateTime(to.Value);
            if (end < start) (start, end) = (end, start);

            _syncingPeriod = true;
            try
            {
                Value = Services.Generation.XlsxGenerationService.FormatPeriod(start, end);
            }
            finally
            {
                _syncingPeriod = false;
            }
        }

        private void SyncPeriodPickersFromValue()
        {
            if (_syncingPeriod) return;
            if (!Services.Generation.XlsxGenerationService.TryParsePeriodBounds(Value, out var from, out var to)) return;

            _syncingPeriod = true;
            try
            {
                PeriodFrom = from.ToDateTime(TimeOnly.MinValue);
                PeriodTo = to.ToDateTime(TimeOnly.MinValue);
            }
            finally
            {
                _syncingPeriod = false;
            }
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
