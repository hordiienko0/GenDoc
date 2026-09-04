using CommunityToolkit.Mvvm.ComponentModel;
using FitnessFilter = GenDoc.Models.Enums.FitnessFilter;

namespace GenDoc.ViewModels.Generation;

public sealed partial class PackageTemplateSummaryItemViewModel : ObservableObject
{
    public PackageTemplateSummaryItemViewModel(string name)
    {
        Name = name;
        Kind = TemplateKind.Docx;
        FitnessFilter = FitnessFilter.All;
    }

    public PackageTemplateSummaryItemViewModel(string name, FitnessFilter filter, int personCount)
    {
        Name = name;
        Kind = TemplateKind.Xlsx;
        FitnessFilter = filter;
        FitnessLabel = filter switch
        {
            FitnessFilter.RegularOnly => "придатні",
            FitnessFilter.LimitedOnly => "обмежено придатні",
            _ => "усі"
        };
        this.personCount = personCount;
    }

    public string Name { get; }
    public TemplateKind Kind { get; }
    public string KindBadge => Kind == TemplateKind.Docx ? "docx" : "xlsx";
    public string? FitnessLabel { get; }
    public FitnessFilter FitnessFilter { get; }
    public bool IsXlsx => Kind == TemplateKind.Xlsx;
    public string XlsxDetail => $"{FitnessLabel} · {PersonCount} осіб";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(XlsxDetail))]
    private int personCount;
}
