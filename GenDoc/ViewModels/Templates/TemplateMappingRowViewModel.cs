using CommunityToolkit.Mvvm.ComponentModel;
using GenDoc.Models.Enums;
using GenDoc.Services.Templates;

namespace GenDoc.ViewModels.Templates;

public record ExportFieldOption(ExportFieldKey Field, string DisplayName);

public partial class TemplateMappingRowViewModel : ObservableObject
{
    private static readonly IReadOnlyList<ExportFieldOption> SharedFieldOptions = Enum.GetValues<ExportFieldKey>()
        .Select(f => new ExportFieldOption(f, ExportFieldKeyNames.DisplayNames[f]))
        .ToList();

    private static readonly IReadOnlyList<MappingSourceOption> SharedSourceOptions = new List<MappingSourceOption>
    {
        new(MappingSourceType.Recipient, "Про людину"),
        new(MappingSourceType.Organization, "Про частину"),
        new(MappingSourceType.Manual, "Вручну при експорті")
    };

    public TemplateMappingRowViewModel(int columnIndex, string headerText, ExportFieldKey fieldKey)
    {
        Id = 0;
        UsesPlaceholders = false;
        ColumnIndex = columnIndex;
        HeaderText = headerText;
        PlaceholderTag = string.Empty;
        selectedField = fieldKey;
    }

    public TemplateMappingRowViewModel(int id, string placeholderTag, MappingSourceType sourceType, string? fieldKey)
    {
        Id = id;
        UsesPlaceholders = true;
        ColumnIndex = 0;
        HeaderText = string.Empty;
        PlaceholderTag = placeholderTag;
        this.sourceType = sourceType;
        selectedFieldName = fieldKey;
        selectedField = Enum.TryParse<ExportFieldKey>(fieldKey, out var parsed) ? parsed : ExportFieldKey.Empty;
    }

    public int Id { get; }
    public bool UsesPlaceholders { get; }
    public bool IsHeaderMode => !UsesPlaceholders;
    public int ColumnIndex { get; }
    public string HeaderText { get; }
    public string PlaceholderTag { get; }
    public string DisplayLabel => UsesPlaceholders ? PlaceholderTag : HeaderText;

    public IReadOnlyList<ExportFieldOption> FieldOptions => SharedFieldOptions;
    public IReadOnlyList<MappingSourceOption> SourceOptions => SharedSourceOptions;

    [ObservableProperty]
    private ExportFieldKey selectedField;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFieldEnabled))]
    [NotifyPropertyChangedFor(nameof(RecipientOrOrgFieldOptions))]
    private MappingSourceType sourceType;

    [ObservableProperty]
    private string? selectedFieldName;

    public bool IsFieldEnabled => SourceType != MappingSourceType.Manual;

    public IReadOnlyList<TemplateFieldOption> RecipientOrOrgFieldOptions => SourceType switch
    {
        MappingSourceType.Recipient => RecipientPlaceholderFields,
        MappingSourceType.Organization => TemplateFieldCatalog.OrganizationFields,
        _ => Array.Empty<TemplateFieldOption>()
    };

    private static readonly IReadOnlyList<TemplateFieldOption> RecipientPlaceholderFields = Enum.GetValues<ExportFieldKey>()
        .Where(f => f != ExportFieldKey.Empty)
        .Select(f => new TemplateFieldOption(f.ToString(), ExportFieldKeyNames.DisplayNames[f]))
        .ToList();

    partial void OnSourceTypeChanged(MappingSourceType value) => SelectedFieldName = null;
}
