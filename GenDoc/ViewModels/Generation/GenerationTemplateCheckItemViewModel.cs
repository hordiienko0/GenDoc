using CommunityToolkit.Mvvm.ComponentModel;

namespace GenDoc.ViewModels.Generation;

public partial class GenerationTemplateCheckItemViewModel : ObservableObject
{
    public GenerationTemplateCheckItemViewModel(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public int Id { get; }
    public string Name { get; }

    [ObservableProperty]
    private bool isChecked;
}
