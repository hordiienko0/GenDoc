using CommunityToolkit.Mvvm.ComponentModel;
using GenDoc.Models.Enums;
using GenDoc.Services.Templates;

namespace GenDoc.ViewModels.Templates;

public record MappingSourceOption(MappingSourceType Value, string Display);

public partial class DocxMappingRowViewModel : ObservableObject
{
    private static readonly IReadOnlyList<MappingSourceOption> SharedSourceOptions = new List<MappingSourceOption>
    {
        new(MappingSourceType.Recipient, "Про людину"),
        new(MappingSourceType.Organization, "Про частину"),
        new(MappingSourceType.Manual, "Вручну при генерації")
    };

    public DocxMappingRowViewModel(int id, string placeholderTag, MappingSourceType sourceType, string? fieldName)
    {
        Id = id;
        PlaceholderTag = placeholderTag;
        this.sourceType = sourceType;
        selectedFieldName = fieldName;
    }

    public int Id { get; }
    public string PlaceholderTag { get; }
    public IReadOnlyList<MappingSourceOption> SourceOptions => SharedSourceOptions;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFieldEnabled))]
    [NotifyPropertyChangedFor(nameof(FieldOptions))]
    private MappingSourceType sourceType;

    [ObservableProperty]
    private string? selectedFieldName;

    public bool IsFieldEnabled => SourceType != MappingSourceType.Manual;

    public IReadOnlyList<TemplateFieldOption> FieldOptions => SourceType switch
    {
        MappingSourceType.Recipient => TemplateFieldCatalog.RecipientFields,
        MappingSourceType.Organization => TemplateFieldCatalog.OrganizationFields,
        _ => Array.Empty<TemplateFieldOption>()
    };

    partial void OnSourceTypeChanged(MappingSourceType value)
    {
        if (value == MappingSourceType.Manual) SelectedFieldName = null;
    }
}
