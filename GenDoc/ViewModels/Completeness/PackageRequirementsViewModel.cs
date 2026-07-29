using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Data;
using GenDoc.Models.Enums;
using GenDoc.Services.Completeness;
using GenDoc.ViewModels.Personnel;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.ViewModels.Completeness
{
    public partial class RequirementTemplateRowViewModel : ObservableObject
    {
        public RequirementTemplateRowViewModel(MatrixTemplateInfo info, bool hasDocuments)
        {
            LinkId = info.LinkId;
            TemplateId = info.TemplateId;
            Name = info.Name;
            SortOrder = info.SortOrder;
            HasDocuments = hasDocuments;
            regular = info.RequirementRegular;
            limited = info.RequirementLimited;
        }

        public int? LinkId { get; }
        public int TemplateId { get; }
        public string Name { get; }
        public int SortOrder { get; set; }
        public bool HasDocuments { get; }

        [ObservableProperty]
        private TemplateRequirement regular;

        [ObservableProperty]
        private TemplateRequirement limited;
    }

    public partial class PackageRequirementsViewModel : DialogViewModelBase
    {
        private readonly ICompletenessService _completenessService;
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        private int _packageId;
        private List<(int TemplateId, TemplateRequirement Regular, TemplateRequirement Limited)> _snapshot = new();

        public PackageRequirementsViewModel(ICompletenessService completenessService, IDbContextFactory<AppDbContext> dbFactory)
        {
            _completenessService = completenessService;
            _dbFactory = dbFactory;
        }

        public string PackageName { get; private set; } = string.Empty;
        public ObservableCollection<RequirementTemplateRowViewModel> Rows { get; } = new();
        public ObservableCollection<(int Id, string Name)> AvailableTemplates { get; } = new();

        [ObservableProperty]
        private (int Id, string Name)? selectedTemplateToAdd;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasPreview))]
        private string previewText = string.Empty;

        public bool HasPreview => !string.IsNullOrEmpty(PreviewText);

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanSave))]
        [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
        private string? validationError;

        public async Task InitializeAsync(int packageId, int? previewIntakeId)
        {
            _packageId = packageId;
            _previewIntakeId = previewIntakeId;

            using var db = _dbFactory.CreateDbContext();
            PackageName = await db.GenerationPackages.Where(p => p.Id == packageId)
                .Select(p => p.Name).FirstOrDefaultAsync() ?? "—";

            var links = await _completenessService.GetPackageLinksAsync(packageId);
            var withDocs = await _completenessService.GetTemplateIdsWithDocumentsAsync(
                links.Select(l => l.TemplateId).ToList());

            Rows.Clear();
            foreach (var link in links)
                AddRow(new RequirementTemplateRowViewModel(link, withDocs.Contains(link.TemplateId)));

            _snapshot = links.Select(l => (l.TemplateId, l.RequirementRegular, l.RequirementLimited)).ToList();

            await ReloadAvailableTemplatesAsync();
            RefreshPreview();
        }

        private int? _previewIntakeId;

        private async Task ReloadAvailableTemplatesAsync()
        {
            var available = await _completenessService.GetTemplatesNotInPackageAsync(_packageId);
            AvailableTemplates.Clear();
            foreach (var t in available) AvailableTemplates.Add(t);
            SelectedTemplateToAdd = AvailableTemplates.FirstOrDefault();
        }

        private void AddRow(RequirementTemplateRowViewModel row)
        {
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(RequirementTemplateRowViewModel.Regular)
                    or nameof(RequirementTemplateRowViewModel.Limited))
                {
                    RefreshPreview();
                    Validate();
                }
            };
            Rows.Add(row);
        }

        [RelayCommand]
        private async Task AddTemplateAsync()
        {
            if (SelectedTemplateToAdd is not (int id, string name)) return;

            AddRow(new RequirementTemplateRowViewModel(
                new MatrixTemplateInfo(0, id, name, null, Rows.Count, TemplateRequirement.Required, TemplateRequirement.Required),
                false));

            await ReloadAvailableTemplatesAsync();
            RefreshPreview();
            Validate();
            SaveCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private void RemoveTemplate(RequirementTemplateRowViewModel? row)
        {
            if (row is null) return;

            if (row.HasDocuments)
            {
                var confirm = System.Windows.MessageBox.Show(
                    "Документи залишаться в архіві, але зникнуть з матриці. Прибрати шаблон з пакета?",
                    "Прибрати шаблон", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
                if (confirm != System.Windows.MessageBoxResult.Yes) return;
            }

            Rows.Remove(row);
            _ = ReloadAvailableTemplatesAsync();
            RefreshPreview();
            Validate();
            SaveCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private void SetRegular(RequirementTemplateRowViewModel? row)
        {
            if (row is not null) row.Regular = TemplateRequirement.Required;
        }

        [RelayCommand]
        private void SetRegularOptional(RequirementTemplateRowViewModel? row)
        {
            if (row is not null) row.Regular = TemplateRequirement.Optional;
        }

        [RelayCommand]
        private void SetRegularNa(RequirementTemplateRowViewModel? row)
        {
            if (row is not null) row.Regular = TemplateRequirement.NotApplicable;
        }

        [RelayCommand]
        private void SetLimitedRequired(RequirementTemplateRowViewModel? row)
        {
            if (row is not null) row.Limited = TemplateRequirement.Required;
        }

        [RelayCommand]
        private void SetLimitedOptional(RequirementTemplateRowViewModel? row)
        {
            if (row is not null) row.Limited = TemplateRequirement.Optional;
        }

        [RelayCommand]
        private void SetLimitedNa(RequirementTemplateRowViewModel? row)
        {
            if (row is not null) row.Limited = TemplateRequirement.NotApplicable;
        }

        [RelayCommand]
        private void MoveUp(RequirementTemplateRowViewModel? row)
        {
            if (row is null) return;
            var index = Rows.IndexOf(row);
            if (index <= 0) return;
            Rows.Move(index, index - 1);
            RenumberSortOrder();
        }

        [RelayCommand]
        private void MoveDown(RequirementTemplateRowViewModel? row)
        {
            if (row is null) return;
            var index = Rows.IndexOf(row);
            if (index < 0 || index >= Rows.Count - 1) return;
            Rows.Move(index, index + 1);
            RenumberSortOrder();
        }

        private void RenumberSortOrder()
        {
            for (var i = 0; i < Rows.Count; i++) Rows[i].SortOrder = i;
        }

        private void RefreshPreview()
        {
            if (_previewIntakeId is null)
            {
                PreviewText = string.Empty;
                return;
            }

            var requiredRegular = Rows.Count(r => r.Regular == TemplateRequirement.Required);
            var requiredLimited = Rows.Count(r => r.Limited == TemplateRequirement.Required);
            var optionalRegular = Rows.Count(r => r.Regular == TemplateRequirement.Optional);
            var optionalLimited = Rows.Count(r => r.Limited == TemplateRequirement.Optional);

            PreviewText = $"Для набору №{_previewIntakeId}: обов'язкових — {requiredRegular} (звич.) / {requiredLimited} (обмеж.), " +
                          $"опційних — {optionalRegular} / {optionalLimited}";
        }

        private void Validate()
        {
            var hasAnyRegular = Rows.Any(r => r.Regular != TemplateRequirement.NotApplicable);
            var hasAnyLimited = Rows.Any(r => r.Limited != TemplateRequirement.NotApplicable);

            ValidationError = !hasAnyRegular || !hasAnyLimited
                ? "Пакет повинен мати хоча б один шаблон, застосовний до кожної категорії"
                : null;
        }

        public bool CanSave => ValidationError is null && Rows.Count > 0;

        [RelayCommand(CanExecute = nameof(CanSave))]
        private async Task SaveAsync()
        {
            Validate();
            if (ValidationError is not null) return;

            var rows = Rows.Select(r => new RequirementRow(
                r.LinkId, r.TemplateId, r.Regular, r.Limited, r.SortOrder)).ToList();
            await _completenessService.SaveRequirementsAsync(_packageId, rows);
            CloseDialog(true);
        }

        [RelayCommand]
        private void Cancel() => CloseDialog(false);
    }
}
