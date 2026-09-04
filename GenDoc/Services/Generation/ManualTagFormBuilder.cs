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
        private readonly IUserSettingsService _userSettings;

        public ManualTagFormBuilder(
            IDbContextFactory<AppDbContext> dbFactory, Staff.IStaffService staffService,
            ICurrentUserContext currentUserContext, Completeness.IIntakeServiceAccessor intakeAccessor,
            IUserSettingsService userSettings)
        {
            _dbFactory = dbFactory;
            _staffService = staffService;
            _currentUserContext = currentUserContext;
            _intakeAccessor = intakeAccessor;
            _userSettings = userSettings;
        }

        public async Task<ManualTagFormViewModel> BuildAsync(
            IReadOnlyList<string> tags, string contextKey, bool needsCourseOfficer = false)
        {
            var hasSigner = ManualTagClassifier.HasSignerPair(tags);
            var arrival = _intakeAccessor.ActiveIntake?.DateStart ?? DateOnly.FromDateTime(DateTime.Today);

            var needsLastValues = hasSigner || tags.Any(t => ManualTagClassifier.Classify(t) != ManualTagKind.Date);
            var lastValues = needsLastValues ? await GetLastValuesAsync() : new Dictionary<string, string>();

            var rows = new ObservableCollection<ManualTagRowViewModel>();
            foreach (var tag in tags)
            {
                if (hasSigner && (ManualTagClassifier.IsSignerRank(tag) || ManualTagClassifier.IsSignerName(tag)))
                    continue;

                rows.Add(ManualTagClassifier.Classify(tag) switch
                {
                    ManualTagKind.Date => new ManualTagRowViewModel(
                        tag, ManualTagClassifier.PrefillDate(tag, arrival), d => ManualTagClassifier.FormatDate(tag, d)),
                    ManualTagKind.Period => ManualTagRowViewModel.Period(tag, lastValues.GetValueOrDefault(tag)),
                    _ => new ManualTagRowViewModel(tag, lastValues.GetValueOrDefault(tag))
                });
            }

            var signer = hasSigner ? await BuildSignerAsync(contextKey) : null;
            var courseOfficer = needsCourseOfficer ? await BuildCourseOfficerAsync(contextKey) : null;

            return new ManualTagFormViewModel(
                rows, signer, courseOfficer,
                tags.FirstOrDefault(ManualTagClassifier.IsSignerRank),
                tags.FirstOrDefault(ManualTagClassifier.IsSignerName));
        }

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

            return new SignerPickerViewModel(options, initial ?? options.FirstOrDefault());
        }

        public async Task SaveAsync(string contextKey, ManualTagFormViewModel form)
        {
            var textRows = form.Rows.Where(r => r.Kind != ManualTagKind.Date).ToList();
            var hasSignerChoice = form.Signer?.Selected is not null || form.CourseOfficer?.Selected is not null;
            if (textRows.Count == 0 && !hasSignerChoice) return;

            await _userSettings.UpdateAsync(settings =>
            {
                if (textRows.Count > 0)
                {
                    var mergedValues = Parse<string>(settings.LastManualValuesJson);
                    foreach (var row in textRows)
                    {
                        if (string.IsNullOrWhiteSpace(row.Value)) mergedValues.Remove(row.Tag);
                        else mergedValues[row.Tag] = row.Value;
                    }
                    settings.LastManualValuesJson = JsonSerializer.Serialize(mergedValues);
                }

                if (hasSignerChoice)
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
            });
        }

        private static Dictionary<string, T> Merge<T>(string? globalJson, string? userJson)
        {
            var merged = Parse<T>(globalJson);
            foreach (var (key, value) in Parse<T>(userJson)) merged[key] = value;
            return merged;
        }

        private static Dictionary<string, T> Parse<T>(string? json) =>
            string.IsNullOrWhiteSpace(json)
                ? new Dictionary<string, T>()
                : JsonSerializer.Deserialize<Dictionary<string, T>>(json) ?? new Dictionary<string, T>();

        private async Task<Dictionary<string, string>> GetLastValuesAsync()
        {
            var user = (await _userSettings.GetForCurrentUserAsync()).LastManualValuesJson;
            using var db = _dbFactory.CreateDbContext();
            var global = await db.AppSettings.Select(s => s.LastManualValuesJson).FirstOrDefaultAsync();
            return Merge<string>(global, user);
        }

        private async Task<int?> GetLastSignerIdAsync(string contextKey)
        {
            var user = (await _userSettings.GetForCurrentUserAsync()).LastSignerByTemplateJson;
            using var db = _dbFactory.CreateDbContext();
            var global = await db.AppSettings.Select(s => s.LastSignerByTemplateJson).FirstOrDefaultAsync();
            return Merge<int>(global, user).TryGetValue(contextKey, out var id) ? id : null;
        }
    }
}
