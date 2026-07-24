using CommunityToolkit.Mvvm.ComponentModel;
using GenDoc.Models.Enums;

namespace GenDoc.ViewModels.Templates;

public record ExportFieldOption(ExportFieldKey Field, string DisplayName);

public partial class TemplateMappingRowViewModel : ObservableObject
{
    private static readonly IReadOnlyList<ExportFieldOption> SharedFieldOptions = Enum.GetValues<ExportFieldKey>()
        .Select(f => new ExportFieldOption(f, ExportFieldKeyNames.DisplayNames[f]))
        .ToList();

    public TemplateMappingRowViewModel(int columnIndex, string headerText, ExportFieldKey fieldKey)
    {
        ColumnIndex = columnIndex;
        HeaderText = headerText;
        selectedField = fieldKey;
    }

    public int ColumnIndex { get; }
    public string HeaderText { get; }
    public IReadOnlyList<ExportFieldOption> FieldOptions => SharedFieldOptions;

    [ObservableProperty]
    private ExportFieldKey selectedField;
}
