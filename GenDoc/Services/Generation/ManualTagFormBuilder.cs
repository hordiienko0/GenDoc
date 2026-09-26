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
            IReadOnlyList<string> tags, string contextKey, bool needsCourseOfficer = false, int? intakeId = null)
        {
            var hasSigner = ManualTagClassifier.HasSignerPair(tags);
            var arrival = await GetArrivalDateAsync(intakeId);

            var needsLastValues = hasSigner || tags.Any(t => ManualTagClassifier.Classify(t) != ManualTagKind.Date);
            var lastValues = needsLastValues ? await GetLastValuesAsync() : new Dictionary<string, string>();

            var rows = new ObservableCollection<ManualTagRowViewModel>();

            var documentDateTags = tags.Where(ManualTagClassifier.IsDocumentDate).ToList();
            if (documentDateTags.Count > 0)
                rows.Add(ManualTagRowViewModel.DocumentDate(documentDateTags, DateOnly.FromDateTime(DateTime.Today)));

            // {{дати_стрільб}} і {{період}} - ті самі дні: один рядок «Дні стрільб» замість двох полів.
            var periodTags = tags.Where(t => ManualTagClassifier.Classify(t) == ManualTagKind.Period).ToList();
            var shootingDays = periodTags.Any(ManualTagClassifier.IsShootingDates);
            if (shootingDays)
            {
                var stored = periodTags
                    .OrderBy(t => ManualTagClassifier.IsShootingDates(t) ? 1 : 0)
                    .Select(t => lastValues.GetValueOrDefault(t))
                    .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                rows.Add(ManualTagRowViewModel.ShootingDays(periodTags, stored));
            }

            foreach (var tag in tags)
            {
                if (hasSigner && (ManualTagClassifier.IsSignerRank(tag) || ManualTagClassifier.IsSignerName(tag)
                                  || ManualTagClassifier.IsSignerPosition(tag)))
                    continue;
                if (ManualTagClassifier.IsDocumentDate(tag)) continue;
                if (shootingDays && periodTags.Contains(tag)) continue;

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
                tags.FirstOrDefault(ManualTagClassifier.IsSignerName),
                tags.FirstOrDefault(ManualTagClassifier.IsSignerPosition));
        }

        private async Task<DateOnly> GetArrivalDateAsync(int? intakeId)
        {
            if (intakeId is int id)
            {
                using var db = _dbFactory.CreateDbContext();
                var start = await db.Intakes.IgnoreQueryFilters()
                    .Where(i => i.Id == id)
                    .Select(i => (DateOnly?)i.DateStart)
                    .FirstOrDefaultAsync();
                if (start is DateOnly documentIntakeStart) return documentIntakeStart;
            }

            return _intakeAccessor.ActiveIntake?.DateStart ?? DateOnly.FromDateTime(DateTime.Today);
        }

        private async Task<SignerPickerViewModel> BuildCourseOfficerAsync(string contextKey)
        {
            var officers = (await _staffService.GetCourseOfficersForPickerAsync())
                .Select(s => new StaffPickerOption(
                    s.Id, s.Rank, Services.NameFormatter.SignatureName(s.LastName, s.FirstName),
                    $"{s.Rank} {Services.NameFormatter.SignatureName(s.LastName, s.FirstName)}",
                    CourseOfficerSignature.Compose(s.UnitName, s.Rank, s.LastName, s.FirstName, s.MiddleName)))
                .ToList();

            var head = await GetHeadOfCoursesOptionAsync();
            var options = head is null ? officers : officers.Prepend(head).ToList();

            var lastId = await GetLastSignerIdAsync(CourseOfficerContextKey(contextKey));
            var initial = lastId is int id ? options.FirstOrDefault(o => o.RecipientId == id) : null;

            return new SignerPickerViewModel(options, initial ?? officers.FirstOrDefault() ?? head);
        }

        private static string CourseOfficerContextKey(string contextKey) => $"{contextKey}#курсовий";

        /// <summary>
        /// Підписати документ може начальник курсів (з налаштувань) або курсовий офіцер; начальник -
        /// за замовчуванням. Лише коли немає ні начальника, ні курсових, список - увесь постійний склад,
        /// щоб пікер не лишився порожнім.
        /// </summary>
        private async Task<SignerPickerViewModel> BuildSignerAsync(string contextKey)
        {
            var head = await GetHeadOfCoursesOptionAsync();

            // Посада над підписом іде за підписантом: курсовим офіцерам - «Курсовий офіцер курсів …».
            // Запасний список (увесь постійний склад) посади не має - тег лишиться незаповненим і це буде видно.
            var officerPosition = HeadOfCourses.CourseOfficerPosition(await GetOrganizationAsync());
            var staff = await _staffService.GetCourseOfficersForPickerAsync();
            var onlyOfficers = true;
            if (head is null && staff.Count == 0)
            {
                staff = await _staffService.GetPermanentStaffForPickerAsync();
                onlyOfficers = false;
            }

            var people = staff
                .Select(s => new StaffPickerOption(
                    s.Id, s.Rank, Services.NameFormatter.SignatureName(s.LastName, s.FirstName),
                    $"{s.Rank} {Services.NameFormatter.SignatureName(s.LastName, s.FirstName)}",
                    Position: onlyOfficers ? officerPosition : null))
                .ToList();

            if (head is not null)
                return new SignerPickerViewModel(people.Prepend(head), head);

            var options = people;
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

        private async Task<StaffPickerOption?> GetHeadOfCoursesOptionAsync()
        {
            using var db = _dbFactory.CreateDbContext();
            var org = await db.OrganizationSettings.AsNoTracking().FirstOrDefaultAsync();
            if (!HeadOfCourses.IsConfigured(org)) return null;

            return new StaffPickerOption(
                HeadOfCourses.PickerId, org!.CommanderRank.Trim(), org.CommanderFullName.Trim(),
                HeadOfCourses.DisplayLabel(org), HeadOfCourses.Signature(org), HeadOfCourses.FormalPosition(org));
        }

        private async Task<Models.OrganizationSettings?> GetOrganizationAsync()
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.OrganizationSettings.AsNoTracking().FirstOrDefaultAsync();
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
                        // Рядок «Дні стрільб» пам'ятає введений перелік під кожним своїм тегом - тоді
                        // наступного разу його підхопить і пакет, де є лише {{дати_стрільб}} або лише {{період}}.
                        foreach (var tag in row.PeriodTags.Count > 0 ? row.PeriodTags : new[] { row.Tag })
                        {
                            if (string.IsNullOrWhiteSpace(row.Value)) mergedValues.Remove(tag);
                            else mergedValues[tag] = row.Value;
                        }
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
