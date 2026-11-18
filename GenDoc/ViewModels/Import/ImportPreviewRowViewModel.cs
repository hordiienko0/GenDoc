using CommunityToolkit.Mvvm.ComponentModel;
using GenDoc.Services.Import;

namespace GenDoc.ViewModels.Import;

public record ImportActionOption(ImportExistingAction Action, string Label);

public partial class ImportPreviewRowViewModel : ObservableObject
{
    internal const int DifferenceLinesShown = 5;

    private readonly ImportRowPreview _model;

    public ImportPreviewRowViewModel(ImportRowPreview model)
    {
        _model = model;
        ActionOptions = BuildActionOptions(model);
        action = model.Action;
    }

    public int RowNumber => _model.RowNumber;
    public string FullNameDisplay => _model.FullNameDisplay;
    public string RankDisplay => _model.RankDisplay;
    public string UnitDisplay => _model.UnitDisplay;
    public ImportRowStatus Status => _model.Status;
    public string Note => _model.Note;

    /// <summary>Людина з рядка вже є в особовому складі (не дубль усередині файлу).</summary>
    public bool IsExisting => _model.ExistingRecipientId is not null;

    public bool HasDifferences => _model.Differences.Count > 0;

    /// <summary>Перші кілька відмінностей під приміткою; решта - у підказці.</summary>
    public IReadOnlyList<string> DifferenceLines
    {
        get
        {
            var lines = _model.Differences.Take(DifferenceLinesShown).Select(d => d.Display).ToList();
            var rest = _model.Differences.Count - DifferenceLinesShown;
            if (rest > 0) lines.Add($"…та ще {rest} (наведіть, щоб побачити всі)");
            return lines;
        }
    }

    public string DifferencesTooltip => string.Join(Environment.NewLine, _model.Differences.Select(d => d.Display));

    public IReadOnlyList<ImportActionOption> ActionOptions { get; }

    /// <summary>Є з чого обирати - інакше в колонці «Дія» просто «-».</summary>
    public bool HasActions => ActionOptions.Count > 1;

    public bool CanMove => _model.CanMove;

    public bool CanUpdate => _model.CanUpdate;

    [ObservableProperty] private ImportExistingAction action;

    partial void OnActionChanged(ImportExistingAction value)
        => _model.Action = ActionOptions.Any(o => o.Action == value) ? value : ImportExistingAction.Skip;

    public void SyncFromModel() => Action = _model.Action;

    private static IReadOnlyList<ImportActionOption> BuildActionOptions(ImportRowPreview model)
    {
        if (model.ExistingRecipientId is null) return Array.Empty<ImportActionOption>();

        var options = new List<ImportActionOption> { new(ImportExistingAction.Skip, "пропустити") };
        if (model.CanUpdate) options.Add(new(ImportExistingAction.Update, "актуалізувати"));
        if (model.CanMove) options.Add(new(ImportExistingAction.Move, "перенести сюди"));
        if (model.CanAddAsNew) options.Add(new(ImportExistingAction.AddAsNew, "додати як нового"));
        return options;
    }
}
