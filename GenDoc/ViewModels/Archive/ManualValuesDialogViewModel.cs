using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.ViewModels.Personnel;

namespace GenDoc.ViewModels.Archive
{
    public partial class ManualTagRowViewModel : ObservableObject
    {
        public ManualTagRowViewModel(string tag)
        {
            Tag = tag;
        }

        public string Tag { get; }

        [ObservableProperty]
        private string value = string.Empty;
    }

    // Одна форма ручних міток на весь батч перегенерації.
    public partial class ManualValuesDialogViewModel : DialogViewModelBase
    {
        public ManualValuesDialogViewModel(IEnumerable<string> tags)
        {
            foreach (var tag in tags)
                Tags.Add(new ManualTagRowViewModel(tag));
        }

        public ObservableCollection<ManualTagRowViewModel> Tags { get; } = new();

        public Dictionary<string, string> GetValues()
            => Tags.ToDictionary(t => t.Tag, t => t.Value);

        [RelayCommand]
        private void Ok() => CloseDialog(true);

        [RelayCommand]
        private void Cancel() => CloseDialog(false);
    }
}
