using CommunityToolkit.Mvvm.ComponentModel;
using GenDoc.Services.Import;

namespace GenDoc.ViewModels.Import;

public record ImportFieldOption(ImportTargetField Field, string DisplayName);

public partial class ImportColumnViewModel : ObservableObject
{
    private static readonly IReadOnlyList<ImportFieldOption> SharedFieldOptions = Enum.GetValues<ImportTargetField>()
        .Select(f => new ImportFieldOption(f, ImportTargetFieldNames.DisplayNames[f]))
        .ToList();

    private readonly ImportColumn _column;

    public ImportColumnViewModel(ImportColumn column)
    {
        _column = column;
        selectedField = column.MappedField;
    }

    public string Header => _column.Header;
    public string ExampleValue => _column.ExampleValue;
    public IReadOnlyList<ImportFieldOption> FieldOptions => SharedFieldOptions;

    [ObservableProperty]
    private ImportTargetField selectedField;

    public event EventHandler? MappingChanged;

    partial void OnSelectedFieldChanged(ImportTargetField value)
    {
        _column.MappedField = value;
        MappingChanged?.Invoke(this, EventArgs.Empty);
    }
}
