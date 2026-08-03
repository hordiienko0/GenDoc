using CommunityToolkit.Mvvm.ComponentModel;

namespace GenDoc.ViewModels.Generation;

public enum TemplateKind { Docx, Xlsx }

public partial class GenerationTemplateCheckItemViewModel : ObservableObject
{
    public GenerationTemplateCheckItemViewModel(int id, string name, TemplateKind kind = TemplateKind.Docx)
    {
        Id = id;
        Name = name;
        Kind = kind;
    }

    public int Id { get; }
    public string Name { get; }
    public TemplateKind Kind { get; }
    public string KindBadge => Kind == TemplateKind.Docx ? "docx" : "xlsx";

    [ObservableProperty]
    private bool isChecked;
}
