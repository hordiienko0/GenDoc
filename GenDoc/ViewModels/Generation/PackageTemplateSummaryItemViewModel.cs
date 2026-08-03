namespace GenDoc.ViewModels.Generation;

public sealed class PackageTemplateSummaryItemViewModel
{
    public PackageTemplateSummaryItemViewModel(string name)
    {
        Name = name;
        Kind = TemplateKind.Docx;
    }

    public PackageTemplateSummaryItemViewModel(string name, GenDoc.Models.Enums.FitnessFilter filter, int personCount)
    {
        Name = name;
        Kind = TemplateKind.Xlsx;
        FitnessLabel = filter switch
        {
            GenDoc.Models.Enums.FitnessFilter.RegularOnly => "придатні",
            GenDoc.Models.Enums.FitnessFilter.LimitedOnly => "обмежено придатні",
            _ => "усі"
        };
        PersonCount = personCount;
    }

    public string Name { get; }
    public TemplateKind Kind { get; }
    public string KindBadge => Kind == TemplateKind.Docx ? "docx" : "xlsx";
    public string? FitnessLabel { get; }
    public int PersonCount { get; }
    public bool IsXlsx => Kind == TemplateKind.Xlsx;
    public string XlsxDetail => $"{FitnessLabel} · {PersonCount} осіб";
}
