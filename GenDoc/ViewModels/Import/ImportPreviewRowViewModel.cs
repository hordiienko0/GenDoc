using CommunityToolkit.Mvvm.ComponentModel;
using GenDoc.Services.Import;

namespace GenDoc.ViewModels.Import;

public partial class ImportPreviewRowViewModel : ObservableObject
{
    private readonly ImportRowPreview _model;

    public ImportPreviewRowViewModel(ImportRowPreview model)
    {
        _model = model;
        move = model.Move;
    }

    public int RowNumber => _model.RowNumber;
    public string FullNameDisplay => _model.FullNameDisplay;
    public string RankDisplay => _model.RankDisplay;
    public string UnitDisplay => _model.UnitDisplay;
    public ImportRowStatus Status => _model.Status;
    public string Note => _model.Note;

    public bool CanMove => _model.ExistingRecipientId is not null;

    [ObservableProperty] private bool move;

    partial void OnMoveChanged(bool value) => _model.Move = value && CanMove;

    public void SyncFromModel() => Move = _model.Move;
}
