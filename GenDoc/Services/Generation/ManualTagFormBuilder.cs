using System.Collections.ObjectModel;
using System.Text.Json;
using GenDoc.Data;
using GenDoc.ViewModels.Generation;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services.Generation
{
    public class ManualTagFormBuilder : IManualTagFormBuilder
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly Staff.IStaffService _staffService;
        private readonly ICurrentUserContext _currentUserContext;
        private readonly Completeness.IIntakeServiceAccessor _intakeAccessor;

        public ManualTagFormBuilder(
            IDbContextFactory<AppDbContext> dbFactory, Staff.IStaffService staffService,
            ICurrentUserContext currentUserContext, Completeness.IIntakeServiceAccessor intakeAccessor)
        {
            _dbFactory = dbFactory;
            _staffService = staffService;
            _currentUserContext = currentUserContext;
            _intakeAccessor = intakeAccessor;
        }

        public async Task<ManualTagFormViewModel> BuildAsync(
            IReadOnlyList<string> tags, string contextKey, bool needsCourseOfficer = false)
        {
            var hasSigner = ManualTagClassifier.HasSignerPair(tags);
            var arrival = _intakeAccessor.ActiveIntake?.DateStart ?? DateOnly.FromDateTime(DateTime.Today);

            var needsLastValues = hasSigner || tags.Any(t => ManualTagClassifier.Classify(t) == ManualTagKind.Text);
            var lastValues = needsLastValues ? await GetLastValuesAsync() : new Dictionary<string, string>();

            var rows = new ObservableCollection<ManualTagRowViewModel>();
            foreach (var tag in tags)
            {
                if (hasSigner && (ManualTagClassifier.IsSignerRank(tag) || ManualTagClassifier.IsSignerName(tag)))
                    continue;

                rows.Add(ManualTagClassifier.Classify(tag) == ManualTagKind.Date
                    ? new ManualTagRowViewModel(tag, ManualTagClassifier.PrefillDate(tag, arrival), d => ManualTagClassifier.FormatDate(tag, d))
                    : new ManualTagRowViewModel(tag, lastValues.GetValueOrDefault(tag)));
            }

            var signer = hasSigner ? await BuildSignerAsync(contextKey) : null;
            var courseOfficer = needsCourseOfficer ? await BuildCourseOfficerAsync(contextKey) : null;

            // Ключі підписанта — рівно ті рядки, що прийшли в переліку тегів
            // (з дужками, як у PlaceholderTag), а не голі константи: інакше
            // обране значення не знайшлося б під час підстановки.
            return new ManualTagFormViewModel(
                rows, signer, courseOfficer,
                tags.FirstOrDefault(ManualTagClassifier.IsSignerRank),
                tags.FirstOrDefault(ManualTagClassifier.IsSignerName));
        }

        // Хтось має бути обраний завжди. Порожній дропліст поруч із запасним
        // авто-вибором у генерації означав би, що підписанта знову визначає
        // порядок рядків у базі; краще показати перше прізвище й дати змінити.
        private async Task<SignerPickerViewModel> BuildCourseOfficerAsync(string contextKey)
        {
            var officers = await _staffService.GetCourseOfficersForPickerAsync();
            var options = officers
                .Select(s => new StaffPickerOption(
                    s.Id, s.Rank, Services.NameFormatter.SignatureName(s.LastName, s.FirstName),
                    $"{s.Rank} {Services.NameFormatter.SignatureName(s.LastName, s.FirstName)}"))
                .ToList();

            var lastId = await GetLastSignerIdAsync(CourseOfficerContextKey(contextKey));
            var initial = lastId is int id ? options.FirstOrDefault(o => o.RecipientId == id) : null;

            return new SignerPickerViewModel(options, initial ?? options.FirstOrDefault());
        }

        // Окремий ключ пам'яті: курсовий офіцер і підписант документа — різні
        // ролі, і запам'ятовувати їх під одним ключем означало б, що вибір однієї
        // ролі мовчки перебиває іншу.
        private static string CourseOfficerContextKey(string contextKey) => $"{contextKey}#курсовий";

        private async Task<SignerPickerViewModel> BuildSignerAsync(string contextKey)
        {
            var staff = await _staffService.GetPermanentStaffForPickerAsync();
            var options = staff
                .Select(s => new StaffPickerOption(
                    s.Id, s.Rank, Services.NameFormatter.SignatureName(s.LastName, s.FirstName),
                    $"{s.Rank} {Services.NameFormatter.SignatureName(s.LastName, s.FirstName)}"))
                .ToList();

            StaffPickerOption? initial = null;

            var currentName = _currentUserContext.CurrentUserFullName;
            if (!string.IsNullOrWhiteSpace(currentName))
            {
                var match = staff.FirstOrDefault(s =>
                    currentName.Contains(s.LastName, StringComparison.OrdinalIgnoreCase)
                    && currentName.Contains(s.FirstName, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                    initial = options.FirstOrDefault(o => o.RecipientId == match.Id);
            }

            if (initial is null)
            {
                var lastId = await GetLastSignerIdAsync(contextKey);
                if (lastId is int id) initial = options.FirstOrDefault(o => o.RecipientId == id);
            }

            // Порожній вибір не давав тегам жодного значення, і документ виходив
            // із порожнім місцем підпису — та сама тиха вада, проти якої заводився
            // дропліст курсового офіцера. Перше прізвище видно й можна змінити.
            return new SignerPickerViewModel(options, initial ?? options.FirstOrDefault());
        }

        public async Task SaveAsync(string contextKey, ManualTagFormViewModel form)
        {
            using var db = _dbFactory.CreateDbContext();
            var settings = await db.AppSettings.FirstOrDefaultAsync();
            if (settings is null) return;

            var textValues = form.Rows
                .Where(r => r.Kind == ManualTagKind.Text && !string.IsNullOrWhiteSpace(r.Value))
                .ToDictionary(r => r.Tag, r => r.Value);
            if (textValues.Count > 0)
            {
                var mergedValues = string.IsNullOrWhiteSpace(settings.LastManualValuesJson)
                    ? new Dictionary<string, string>()
                    : JsonSerializer.Deserialize<Dictionary<string, string>>(settings.LastManualValuesJson) ?? new Dictionary<string, string>();
                foreach (var (tag, value) in textValues) mergedValues[tag] = value;
                settings.LastManualValuesJson = JsonSerializer.Serialize(mergedValues);
            }

            if (form.Signer?.Selected is not null || form.CourseOfficer?.Selected is not null)
            {
                var mergedSigners = string.IsNullOrWhiteSpace(settings.LastSignerByTemplateJson)
                    ? new Dictionary<string, int>()
                    : JsonSerializer.Deserialize<Dictionary<string, int>>(settings.LastSignerByTemplateJson) ?? new Dictionary<string, int>();

                if (form.Signer?.Selected is { } chosenSigner)
                    mergedSigners[contextKey] = chosenSigner.RecipientId;

                if (form.CourseOfficer?.Selected is { } chosenOfficer)
                    mergedSigners[CourseOfficerContextKey(contextKey)] = chosenOfficer.RecipientId;

                settings.LastSignerByTemplateJson = JsonSerializer.Serialize(mergedSigners);
            }

            await db.SaveChangesAsync();
        }

        private async Task<Dictionary<string, string>> GetLastValuesAsync()
        {
            using var db = _dbFactory.CreateDbContext();
            var json = await db.AppSettings.Select(s => s.LastManualValuesJson).FirstOrDefaultAsync();
            if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }

        private async Task<int?> GetLastSignerIdAsync(string contextKey)
        {
            using var db = _dbFactory.CreateDbContext();
            var json = await db.AppSettings.Select(s => s.LastSignerByTemplateJson).FirstOrDefaultAsync();
            if (string.IsNullOrWhiteSpace(json)) return null;
            var dict = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
            return dict is not null && dict.TryGetValue(contextKey, out var id) ? id : null;
        }
    }
}
