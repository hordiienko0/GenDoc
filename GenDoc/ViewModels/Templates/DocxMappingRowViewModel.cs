using CommunityToolkit.Mvvm.ComponentModel;
using GenDoc.Models.Enums;
using GenDoc.Services.Generation;
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

    private static readonly HashSet<string> DateFieldNames = new(StringComparer.Ordinal)
    {
        "DateOfBirth", "CourseArrivalDate", "TravelCertificateDate"
    };

    public DocxMappingRowViewModel(int id, string placeholderTag, MappingSourceType sourceType, string? fieldName, string? dateFormat)
    {
        Id = id;
        PlaceholderTag = placeholderTag;
        this.sourceType = sourceType;
        selectedFieldName = fieldName;
        selectedDateFormat = dateFormat;
    }

    public int Id { get; }
    public string PlaceholderTag { get; }
    public IReadOnlyList<MappingSourceOption> SourceOptions => SharedSourceOptions;
    public IReadOnlyList<DateFormatOption> DateFormatOptions => DateFormatCatalog.Options;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFieldEnabled))]
    [NotifyPropertyChangedFor(nameof(FieldOptions))]
    [NotifyPropertyChangedFor(nameof(IsDateField))]
    private MappingSourceType sourceType;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDateField))]
    private string? selectedFieldName;

    [ObservableProperty]
    private string? selectedDateFormat;

    public bool IsFieldEnabled => SourceType != MappingSourceType.Manual;

    public bool IsDateField => SourceType == MappingSourceType.Recipient
        && SelectedFieldName is not null && DateFieldNames.Contains(SelectedFieldName);

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

    partial void OnSelectedFieldNameChanged(string? value)
    {
        if (value is null || !DateFieldNames.Contains(value)) SelectedDateFormat = null;
    }
}
