using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Models;
using GenDoc.Services.Intakes;

namespace GenDoc.ViewModels.Personnel
{
    public record BaseNodeOption(int Id, string DisplayName);

    public partial class IntakeWizardViewModel : DialogViewModelBase
    {
        private readonly IIntakeService _intakeService;
        private readonly OrgTreeViewModel _tree;

        public IntakeWizardViewModel(IIntakeService intakeService, OrgTreeViewModel tree)
        {
            _intakeService = intakeService;
            _tree = tree;
        }

        public ObservableCollection<BaseNodeOption> BaseNodes { get; } = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
        private string displayNumber = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
        private BaseNodeOption? selectedBaseNode;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
        private DateTime? dateStart = DateTime.Today;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
        private DateTime? dateEnd = DateTime.Today.AddMonths(1);

        // Структура фіксована - «Всі» → «Придатні», «Обмежено придатні» - тому
        // текст статичний, а не рахується з чекбоксів.
        public string SummaryText => "Буде створено 3 папки: Всі → Придатні, Обмежено придатні";

        [ObservableProperty]
        private string? errorText;

        public Intake? CreatedIntake { get; private set; }

        public async Task InitializeAsync()
        {
            var number = await _intakeService.GetNextNumberAsync();
            var template = await _intakeService.GetNumberTemplateAsync();
            DisplayNumber = template.Replace("{n}", number.ToString());

            // Основою набору може бути лише папка поза іншими наборами.
            foreach (var node in Flatten(_tree.RootNodes).Where(n => n.IntakeId is null))
                BaseNodes.Add(new BaseNodeOption(node.Id, $"{new string(' ', node.Depth * 3)}{node.Name}"));
            SelectedBaseNode = BaseNodes.FirstOrDefault();
        }

        private static IEnumerable<OrgNodeViewModel> Flatten(IEnumerable<OrgNodeViewModel> nodes)
        {
            foreach (var node in nodes)
            {
                yield return node;
                foreach (var child in Flatten(node.Children))
                    yield return child;
            }
        }

        private bool CanCreate =>
            !string.IsNullOrWhiteSpace(DisplayNumber)
            && SelectedBaseNode is not null
            && DateStart is not null && DateEnd is not null
            && DateEnd >= DateStart;

        [RelayCommand(CanExecute = nameof(CanCreate))]
        private async Task CreateAsync()
        {
            ErrorText = null;
            try
            {
                CreatedIntake = await _intakeService.CreateAsync(new IntakeCreateRequest(
                    DisplayNumber,
                    SelectedBaseNode!.Id,
                    DateOnly.FromDateTime(DateStart!.Value),
                    DateOnly.FromDateTime(DateEnd!.Value)));
                CloseDialog(true);
            }
            catch (Exception ex)
            {
                ErrorText = $"Не вдалося створити набір: {ex.Message}";
            }
        }

        [RelayCommand]
        private void Cancel() => CloseDialog(false);
    }
}
