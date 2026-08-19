using CommunityToolkit.Mvvm.ComponentModel;
using GenDoc.Services.Import;

namespace GenDoc.ViewModels.Import;

/// <summary>
/// Рядок попереднього перегляду на кроці 4. Обгортка потрібна лише через
/// колонку «ДІЯ»: прапорець мусить оновлюватись і тоді, коли його ставить
/// не користувач, а перемикач «перенести всі» - тобто потрібне сповіщення,
/// якого простий ImportRowPreview не дає.
///
/// Решта полів - наскрізні, щоб розмітка лишилась тією самою.
/// </summary>
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

    /// <summary>Переносити можна лише те, що вже є в базі. Дубль усередині
    /// файлу картки не має, тож для нього колонка «ДІЯ» лишається порожньою.</summary>
    public bool CanMove => _model.ExistingRecipientId is not null;

    [ObservableProperty] private bool move;

    partial void OnMoveChanged(bool value) => _model.Move = value && CanMove;

    /// <summary>Оновити прапорець із моделі - після того, як його змінили
    /// гуртом (перемикач «перенести всі»), а не в цьому рядку.</summary>
    public void SyncFromModel() => Move = _model.Move;
}
