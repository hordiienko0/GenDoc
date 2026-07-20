using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Services;
using GenDoc.Services.Recipients;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.ViewModels.Recipients
{
    public partial class RecipientsViewModel : ObservableObject
    {
        private readonly IRecipientService _recipientService;
        private readonly IDialogService _dialogService;
        private readonly IServiceProvider _serviceProvider;

        public RecipientsViewModel(IRecipientService recipientService, IDialogService dialogService, IServiceProvider serviceProvider)
        {
            _recipientService = recipientService;
            _dialogService = dialogService;
            _serviceProvider = serviceProvider;
            Refresh();
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasResults))]
        [NotifyPropertyChangedFor(nameof(IsEmpty))]
        [NotifyPropertyChangedFor(nameof(CountLabel))]
        private ObservableCollection<RecipientListItem> recipients = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EmptyMessage))]
        private string? searchText;

        [ObservableProperty]
        private RecipientListItem? selectedRecipient;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CountLabel))]
        private int totalCount;

        public bool HasResults => Recipients.Count > 0;
        public bool IsEmpty => !HasResults;
        public string CountLabel => $"{Recipients.Count} з {TotalCount} записів";
        public string EmptyMessage => string.IsNullOrWhiteSpace(SearchText)
            ? "Особовий склад порожній"
            : $"Нічого не знайдено за запитом «{SearchText}»";

        partial void OnSearchTextChanged(string? value) => Refresh();

        [RelayCommand]
        private void Add()
        {
            var vm = _serviceProvider.GetRequiredService<RecipientEditViewModel>();
            vm.Initialize(null);

            var result = _dialogService.ShowDialog(vm, Application.Current.MainWindow);
            if (result == true) Refresh();
        }

        [RelayCommand]
        private void Edit(RecipientListItem? item)
        {
            var target = item ?? SelectedRecipient;
            if (target is null) return;

            var vm = _serviceProvider.GetRequiredService<RecipientEditViewModel>();
            vm.Initialize(target.Id);

            var result = _dialogService.ShowDialog(vm, Application.Current.MainWindow);
            if (result == true) Refresh();
        }

        private void Refresh()
        {
            TotalCount = _recipientService.Search(null).Count;
            Recipients = new ObservableCollection<RecipientListItem>(_recipientService.Search(SearchText));
        }
    }
}
