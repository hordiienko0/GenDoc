using CommunityToolkit.Mvvm.ComponentModel;

namespace GenDoc.ViewModels.Generation;

public partial class ManualTagInputViewModel : ObservableObject
{
    public ManualTagInputViewModel(string tag)
    {
        Tag = tag;
    }

    public string Tag { get; }

    [ObservableProperty]
    private string? value;
}
