using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Services;
using GenDoc.Services.Duty;

namespace GenDoc.ViewModels.Duty;

/// <summary>
/// Блок на листі вечірньої перевірки: галочка «включити», заголовок блоку (як у паперовому списку),
/// папки дерева, чиї люди йдуть у блок (як у графіку чергувань), і самі люди з галочками -
/// знятий у список не йде, решта нумерується за порядком. Прикомандированих можна ділити на дві таблиці
/// за придатністю: обмежено придатні вгорі, придатні внизу, кожна з нумерацією з 1.
/// </summary>
public partial class EveningCheckGroupViewModel : ObservableObject
{
    private readonly Action _peopleChanged;
    private readonly Action _compositionChanged;
    private readonly HashSet<int> _unchecked = new();
    private IReadOnlyList<EveningCheckPersonOption> _options = Array.Empty<EveningCheckPersonOption>();
    private bool _buildingFolders;

    public EveningCheckGroupViewModel(string title, string kindLabel, Action peopleChanged, Action compositionChanged, bool canSplitByFitness = false)
    {
        _title = title;
        KindLabel = kindLabel;
        _peopleChanged = peopleChanged;
        _compositionChanged = compositionChanged;
        CanSplitByFitness = canSplitByFitness;
        _splitByFitness = canSplitByFitness;
    }

    /// <summary>Підпис картки в програмі: «1. Набір», «2. Прикомандировані».</summary>
    public string KindLabel { get; }

    /// <summary>Чи показувати галочку поділу за придатністю (лише у прикомандированих).</summary>
    public bool CanSplitByFitness { get; }

    /// <summary>Дерево папок: відмічена папка бере своїх людей разом із вкладеними.</summary>
    public ObservableCollection<DutyFolderViewModel> Folders { get; } = new();

    public ObservableCollection<EveningCheckPersonViewModel> People { get; } = new();

    [ObservableProperty] private bool _isIncluded = true;
    [ObservableProperty] private string _title;
    [ObservableProperty] private string _folderSummary = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private bool _hasPeople;

    /// <summary>Дві окремі таблиці: спершу обмежено придатні, далі придатні, кожна зі своїм заголовком і нумерацією з 1.</summary>
    [ObservableProperty] private bool _splitByFitness;

    /// <summary>Скільки людей з папок не взято, бо вони в іншому блоці (прикомандировані) - для підказки.</summary>
    [ObservableProperty] private int _takenByOtherBlock;

    partial void OnIsIncludedChanged(bool value) => _compositionChanged();

    partial void OnTitleChanged(string value) => _peopleChanged();

    /// <summary>Поділ міняє порядок у списку (обмежено придатні першими) і нумерацію - список перебудовується.</summary>
    partial void OnSplitByFitnessChanged(bool value) => SetPeople(_options);

    public int CheckedCount => People.Count(p => p.IsChecked);

    /// <summary>Дерево з відміченими папками; гілки до відмічених розгорнуті, щоб їх було видно одразу.</summary>
    public void BuildFolders(IReadOnlyList<DutyFolderOption> options, IReadOnlyCollection<int> checkedIds)
    {
        _buildingFolders = true;
        try
        {
            Folders.Clear();
            var byId = new Dictionary<int, DutyFolderViewModel>();
            foreach (var option in options)
            {
                var parent = option.ParentId is int parentId ? byId.GetValueOrDefault(parentId) : null;
                var folder = new DutyFolderViewModel(option, parent, OnFolderChanged);
                byId[option.Id] = folder;
                (parent?.Children ?? Folders).Add(folder);
            }

            foreach (var folder in Folders) folder.IsExpanded = true;
            foreach (var id in checkedIds)
            {
                if (!byId.TryGetValue(id, out var folder)) continue;
                folder.IsChecked = true;
                for (var up = folder.Parent; up is not null; up = up.Parent) up.IsExpanded = true;
            }
        }
        finally
        {
            _buildingFolders = false;
        }
        UpdateFolderSummary();
    }

    private void OnFolderChanged()
    {
        if (_buildingFolders) return;
        UpdateFolderSummary();
        _compositionChanged();
    }

    private IEnumerable<DutyFolderViewModel> AllFolders() => Folders.SelectMany(f => f.SelfAndDescendants());

    /// <summary>Відмічені папки без вкладених у вже відмічені.</summary>
    public IReadOnlyList<int> PickedFolderIds => AllFolders().Where(f => f.IsPicked).Select(f => f.Id).ToList();

    /// <summary>Усі люди відмічених папок (разом із вкладеними), без повторів.</summary>
    public IReadOnlySet<int> FolderPeopleIds
        => AllFolders().Where(f => f.IsPicked).SelectMany(f => f.Option.PeopleIds).ToHashSet();

    private void UpdateFolderSummary()
    {
        var picked = AllFolders().Where(f => f.IsPicked).ToList();
        if (picked.Count == 0)
        {
            FolderSummary = "Папок не відмічено - блок буде з самих порожніх пронумерованих рядків.";
            return;
        }

        var people = FolderPeopleIds.Count;
        FolderSummary = $"{people} {PluralHelper.Pluralize(people, "особа", "особи", "осіб")} "
                        + $"з {picked.Count} {PluralHelper.Pluralize(picked.Count, "папки", "папок", "папок")}, вкладені папки входять.";
    }

    /// <summary>Нові люди блоку (з поділом - обмежено придатні першими); зняті раніше галочки (за Id) лишаються знятими.</summary>
    public void SetPeople(IEnumerable<EveningCheckPersonOption> options)
    {
        _options = options.ToList();
        var ordered = SplitByFitness ? _options.OrderBy(o => o.IsLimited ? 0 : 1).ToList() : _options;

        People.Clear();
        foreach (var option in ordered)
        {
            var person = new EveningCheckPersonViewModel(option, OnPersonChanged);
            if (_unchecked.Contains(option.RecipientId)) person.SetCheckedQuietly(false);
            People.Add(person);
        }
        HasPeople = People.Count > 0;
        Renumber();
        _peopleChanged();
    }

    [RelayCommand]
    private void CheckAll() => SetAll(true);

    [RelayCommand]
    private void UncheckAll() => SetAll(false);

    private void SetAll(bool checkedState)
    {
        foreach (var person in People) person.SetCheckedQuietly(checkedState);
        OnPersonChanged();
    }

    private void OnPersonChanged()
    {
        foreach (var person in People)
        {
            if (person.IsChecked) _unchecked.Remove(person.RecipientId);
            else _unchecked.Add(person.RecipientId);
        }
        Renumber();
        _peopleChanged();
    }

    /// <summary>Номери «№ п/п» лише у відмічених - як вони стануть на листі; з поділом кожна таблиця нумерується з 1.</summary>
    private void Renumber()
    {
        var number = 0;
        bool? lastLimited = null;
        foreach (var person in People)
        {
            if (SplitByFitness && lastLimited is bool previous && previous != person.IsLimited) number = 0;
            lastLimited = person.IsLimited;
            person.NumberText = person.IsChecked ? (++number).ToString() : string.Empty;
        }
    }

    /// <summary>«17 з 17 осіб · 1 блок по 25 рядків · 2 аркуші A4» (аркушів - блоки × сторінки днів).</summary>
    public void UpdateSummary(int datePages)
    {
        var count = CheckedCount;
        var group = ToGroup();
        var blocks = EveningCheckXlsxExporter.BlocksOf(group);
        var parts = new List<string>
        {
            People.Count == 0
                ? "людей немає"
                : $"{count} з {People.Count} {PluralHelper.Pluralize(People.Count, "особи", "осіб", "осіб")}"
        };
        var tables = EveningCheckXlsxExporter.Tables(group);
        if (tables.Count == 2)
        {
            var limited = tables[0].People.Count;
            var fit = tables[1].People.Count;
            parts.Add($"дві таблиці: {limited} обмежено {PluralHelper.Pluralize(limited, "придатний", "придатних", "придатних")} "
                      + $"і {fit} {PluralHelper.Pluralize(fit, "придатний", "придатних", "придатних")}");
        }
        parts.Add($"{blocks} {PluralHelper.Pluralize(blocks, "блок", "блоки", "блоків")} по {EveningCheckXlsxExporter.RowsPerBlock} рядків");
        if (datePages > 0)
        {
            var sheets = blocks * datePages;
            parts.Add($"{sheets} {PluralHelper.Pluralize(sheets, "аркуш", "аркуші", "аркушів")} A4");
        }
        if (TakenByOtherBlock > 0)
            parts.Add($"без {TakenByOtherBlock} {PluralHelper.Pluralize(TakenByOtherBlock, "особи", "осіб", "осіб")} з блоку «Прикомандировані»");
        Summary = string.Join(" · ", parts);
    }

    public EveningCheckGroup ToGroup()
    {
        var group = new EveningCheckGroup { Title = Title.Trim(), SplitByFitness = SplitByFitness };
        group.People.AddRange(People.Where(p => p.IsChecked).Select(p => new EveningCheckPerson(p.Rank, p.FullName, p.IsLimited)));
        return group;
    }
}

public partial class EveningCheckPersonViewModel : ObservableObject
{
    private readonly Action _changed;
    private bool _quiet;

    public EveningCheckPersonViewModel(EveningCheckPersonOption option, Action changed)
    {
        Option = option;
        _changed = changed;
    }

    public EveningCheckPersonOption Option { get; }
    public int RecipientId => Option.RecipientId;
    public string Rank => Option.Rank;
    public string FullName => Option.FullName;
    public bool IsLimited => Option.IsLimited;

    /// <summary>Позначка в списку: «обмежено придатний» або порожньо.</summary>
    public string FitnessMark => IsLimited ? FitnessCategoryHelper.Limited : string.Empty;

    [ObservableProperty] private bool _isChecked = true;
    [ObservableProperty] private string _numberText = string.Empty;

    partial void OnIsCheckedChanged(bool value)
    {
        if (!_quiet) _changed();
    }

    /// <summary>Галочка без перерахунку - для «Усіх» / «Нікого» і відновлення знятих, де перерахунок один на всіх.</summary>
    internal void SetCheckedQuietly(bool value)
    {
        _quiet = true;
        try
        {
            IsChecked = value;
        }
        finally
        {
            _quiet = false;
        }
    }
}
