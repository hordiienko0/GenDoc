using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GenDoc.ViewModels.Generation
{
    public enum ManualTagKind { Text, Date, Period }

    public partial class ManualTagRowViewModel : ObservableObject
    {
        private const string PeriodLabel = "Період (з – по)";
        private const string DocumentDateLabel = "Дата документа";
        public const string DocumentDateTag = "{{дата}}";

        private Func<DateOnly, string>? _dateFormatter;
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

        /// <summary>
        /// Один рядок «Дні стрільб» на {{період}} і {{дати_стрільб}}: офіцер вибирає дні один раз, відомості
        /// беруть з них аркуші, а накази й заявки - той самий перелік словами.
        /// </summary>
        public static ManualTagRowViewModel ShootingDays(IReadOnlyList<string> periodTags, string? storedValue)
        {
            var row = new ManualTagRowViewModel(periodTags[0], ManualTagKind.Period, storedValue, ShootingDaysLabel)
            {
                PeriodTags = periodTags
            };
            row.SyncPeriodPickersFromValue();
            return row;
        }

        private const string ShootingDaysLabel = "Дні стрільб (з – по або перелік через кому)";

        /// <summary>Теги, які заповнює цей рядок періоду; порожньо - лише власний <see cref="Tag"/>.</summary>
        public IReadOnlyList<string> PeriodTags { get; private init; } = Array.Empty<string>();

        public static ManualTagRowViewModel DocumentDate(IReadOnlyList<string> drivenTags, DateOnly initialDate)
        {
            var row = new ManualTagRowViewModel(DocumentDateTag, ManualTagKind.Date, null, DocumentDateLabel)
            {
                DrivenTags = drivenTags,
                _dateFormatter = d => Services.Generation.ManualTagClassifier.FormatDate(DocumentDateTag, d)
            };
            row.DateValue = initialDate.ToDateTime(TimeOnly.MinValue);
            return row;
        }

        public string Tag { get; }

        public string Label => _labelOverride ?? Services.Generation.ManualTagLabel.Human(Tag);

        public ManualTagKind Kind { get; }

        public IReadOnlyList<string> DrivenTags { get; private init; } = Array.Empty<string>();

        public bool IsDocumentDate => DrivenTags.Count > 0;

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

            // У діалогах (перегенерація, постійний склад) «Дата документа» - звичайне текстове поле, а
            // GetValues бере дату з DateValue: без цього введена вручну дата мовчки ігнорувалась.
            if (IsDocumentDate
                && DateTime.TryParseExact(value?.Trim(), "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var typed)
                && DateValue != typed)
            {
                DateValue = typed;
            }
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

    public record StaffPickerOption(
        int RecipientId, string Rank, string SignatureName, string DisplayLabel, string? SignaturePreview = null,
        string? Position = null);

    public partial class SignerPickerViewModel : ObservableObject
    {
        public SignerPickerViewModel(IEnumerable<StaffPickerOption> options, StaffPickerOption? initial)
        {
            Options = new ObservableCollection<StaffPickerOption>(options);
            selected = initial;
        }

        public ObservableCollection<StaffPickerOption> Options { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedSignaturePreview))]
        [NotifyPropertyChangedFor(nameof(HasSignaturePreview))]
        private StaffPickerOption? selected;

        public string? SelectedSignaturePreview => Selected?.SignaturePreview;

        public bool HasSignaturePreview => !string.IsNullOrWhiteSpace(SelectedSignaturePreview);
    }

    public class ManualTagFormViewModel
    {
        public const string SignerRankTag = "звання_підписанта";
        public const string SignerNameTag = "піб_підписанта";
        public const string SignerPositionTag = "посада_підписанта";

        public ManualTagFormViewModel(
            ObservableCollection<ManualTagRowViewModel> rows,
            SignerPickerViewModel? signer,
            SignerPickerViewModel? courseOfficer = null,
            string? signerRankTag = null,
            string? signerNameTag = null,
            string? signerPositionTag = null)
        {
            Rows = rows;
            Signer = signer;
            CourseOfficer = courseOfficer;

            _signerRankTag = signerRankTag ?? SignerRankTag;
            _signerNameTag = signerNameTag ?? SignerNameTag;
            _signerPositionTag = signerPositionTag;
        }

        private readonly string _signerRankTag;
        private readonly string _signerNameTag;
        private readonly string? _signerPositionTag;

        public ObservableCollection<ManualTagRowViewModel> Rows { get; }
        public SignerPickerViewModel? Signer { get; }

        public SignerPickerViewModel? CourseOfficer { get; }

        public bool HasContent => Rows.Count > 0 || Signer is not null || CourseOfficer is not null;

        public ManualTagRowViewModel? DocumentDateRow => Rows.FirstOrDefault(r => r.IsDocumentDate);

        public void SetDocumentDate(DateTime? date)
        {
            if (DocumentDateRow is { } row) row.DateValue = date ?? DateTime.Today;
        }

        public Dictionary<string, string> GetValues()
        {
            var values = Rows.ToDictionary(r => r.Tag, r => r.Value);

            foreach (var row in Rows.Where(r => r.PeriodTags.Count > 0))
            {
                foreach (var tag in row.PeriodTags)
                    values[tag] = Services.Generation.ManualTagClassifier.FormatPeriodValue(tag, row.Value);
            }

            if (DocumentDateRow is { DateValue: DateTime documentDate } dateRow)
            {
                var date = DateOnly.FromDateTime(documentDate);
                foreach (var tag in dateRow.DrivenTags)
                    values[tag] = Services.Generation.ManualTagClassifier.FormatDate(tag, date);
            }

            if (Signer?.Selected is { } signer)
            {
                values[_signerRankTag] = signer.Rank;
                values[_signerNameTag] = signer.SignatureName;
                if (_signerPositionTag is not null) values[_signerPositionTag] = signer.Position ?? string.Empty;
            }

            return values;
        }
    }
}
