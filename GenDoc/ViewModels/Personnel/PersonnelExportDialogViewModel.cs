using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Duty;
using GenDoc.Services.Personnel;
using GenDoc.ViewModels.Common;
using GenDoc.ViewModels.Duty;
using Microsoft.Win32;

namespace GenDoc.ViewModels.Personnel
{
    /// <summary>
    /// Кого експортувати: папку дерева (з вкладеними чи без) або лише позначених галочкою в списку.
    /// </summary>
    public sealed record PersonExportRequest(
        int NodeId,
        string NodeName,
        string NodePath,
        bool IncludeDescendants,
        IReadOnlyList<int>? OnlyIds = null,
        string? SearchText = null);

    public sealed record ExportChoice<T>(T Value, string Label);

    public partial class ExportFieldOptionViewModel : ObservableObject
    {
        public ExportFieldOptionViewModel(PersonExportField field) => Field = field;

        public PersonExportField Field { get; }
        public string Label => Field.Label;

        [ObservableProperty]
        private bool isChecked;
    }

    public sealed class ExportFieldGroupViewModel
    {
        public ExportFieldGroupViewModel(string title, IReadOnlyList<ExportFieldOptionViewModel> fields)
        {
            Title = title;
            Fields = fields;
        }

        public string Title { get; }
        public IReadOnlyList<ExportFieldOptionViewModel> Fields { get; }
    }

    public partial class ExportRankOptionViewModel : ObservableObject
    {
        public ExportRankOptionViewModel(RankCategory category) => Category = category;

        public RankCategory Category { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Label))]
        [NotifyPropertyChangedFor(nameof(HasPeople))]
        private int count;

        [ObservableProperty]
        private bool isChecked = true;

        public bool HasPeople => Count > 0;
        public string Label => $"{RankOrder.CategoryDisplayName(Category)} ({Count})";
    }

    public partial class ExportConditionViewModel : ObservableObject
    {
        public ExportConditionViewModel(
            IReadOnlyList<PersonExportField> fields,
            IReadOnlyList<ExportChoice<PersonExportOperator>> operators)
        {
            Fields = fields;
            Operators = operators;
            field = fields[0];
            comparison = operators[0];
        }

        public IReadOnlyList<PersonExportField> Fields { get; }
        public IReadOnlyList<ExportChoice<PersonExportOperator>> Operators { get; }

        [ObservableProperty]
        private PersonExportField field;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(NeedsValue))]
        private ExportChoice<PersonExportOperator> comparison;

        [ObservableProperty]
        private string? value;

        public bool NeedsValue => ToCondition().NeedsValue;

        public PersonExportCondition ToCondition() => new(Field, Comparison.Value, Value);
    }

    /// <summary>Галочка аркуша книги й те, скільки людей на нього зараз потрапляє.</summary>
    public partial class ExportSheetOptionViewModel : ObservableObject
    {
        public ExportSheetOptionViewModel(PersonExportSheetKind kind) => Kind = kind;

        public PersonExportSheetKind Kind { get; }
        public string Label => PersonExportSheets.Title(Kind);
        public string Hint => PersonExportSheets.Hint(Kind);

        [ObservableProperty]
        private bool isChecked = true;

        [ObservableProperty]
        private int count;

        internal IReadOnlyList<PersonExportRow> Rows { get; set; } = Array.Empty<PersonExportRow>();
    }

    /// <summary>
    /// Папка в дереві вибору - як у графіку чергувань: відмічена бере своїх людей разом із вкладеними,
    /// вкладені у відмічену показуються відміченими й неактивними. У дереві набору папка прикомандированих
    /// ще й виключена: вона не відмічається, бо її люди йдуть на свої аркуші.
    /// </summary>
    public partial class ExportFolderViewModel : ObservableObject
    {
        private readonly Action _changed;

        public ExportFolderViewModel(DutyFolderOption option, ExportFolderViewModel? parent, Action changed)
        {
            Option = option;
            Parent = parent;
            _changed = changed;
        }

        public DutyFolderOption Option { get; }
        public ExportFolderViewModel? Parent { get; }
        public int Id => Option.Id;
        public string Name => Option.Name;
        public ObservableCollection<ExportFolderViewModel> Children { get; } = new();

        public string CountText => Option.PeopleIds.Count == 0
            ? "порожня"
            : $"{Option.PeopleIds.Count} {PluralHelper.Pluralize(Option.PeopleIds.Count, "особа", "особи", "осіб")}";

        [ObservableProperty]
        private bool isExpanded;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsTicked))]
        private bool isChecked;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsTicked))]
        [NotifyPropertyChangedFor(nameof(CanTick))]
        private bool isCovered;

        /// <summary>Папка прикомандированих (або вкладена в неї) у дереві набору.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsTicked))]
        [NotifyPropertyChangedFor(nameof(CanTick))]
        private bool isExcluded;

        /// <summary>Галочка на екрані: відмічена сама або входить у відмічену вищу папку - і не виключена.</summary>
        public bool IsTicked
        {
            get => (IsChecked || IsCovered) && !IsExcluded;
            set { if (CanTick) IsChecked = value; }
        }

        public bool CanTick => !IsCovered && !IsExcluded;

        /// <summary>Відмічена сама, а не через вищу папку.</summary>
        public bool IsPicked => IsChecked && !IsCovered && !IsExcluded;

        partial void OnIsCheckedChanged(bool value)
        {
            CoverChildren();
            _changed();
        }

        private void CoverChildren()
        {
            foreach (var child in Children)
            {
                child.IsCovered = IsChecked || IsCovered;
                child.CoverChildren();
            }
        }

        public IEnumerable<ExportFolderViewModel> SelfAndDescendants()
        {
            yield return this;
            foreach (var descendant in Children.SelectMany(c => c.SelfAndDescendants()))
                yield return descendant;
        }
    }

    /// <summary>
    /// Дерево папок із галочками - те саме, що в графіку чергувань і вечірній перевірці:
    /// відмічена папка бере своїх людей разом із вкладеними.
    /// </summary>
    public partial class ExportFolderPickerViewModel : ObservableObject
    {
        private readonly Action _changed;
        private bool _building;
        private HashSet<int> _excludedPeople = new();

        public ExportFolderPickerViewModel(string title, Action changed)
        {
            Title = title;
            _changed = changed;
        }

        public string Title { get; }
        public ObservableCollection<ExportFolderViewModel> Folders { get; } = new();

        [ObservableProperty]
        private string summary = string.Empty;

        /// <summary>Дерево з відміченими папками; гілки до відмічених розгорнуті, щоб їх було видно одразу.</summary>
        public void Build(IReadOnlyList<DutyFolderOption> options, IReadOnlyCollection<int> checkedIds)
        {
            _building = true;
            try
            {
                Folders.Clear();
                var byId = new Dictionary<int, ExportFolderViewModel>();
                foreach (var option in options)
                {
                    var parent = option.ParentId is int parentId ? byId.GetValueOrDefault(parentId) : null;
                    var folder = new ExportFolderViewModel(option, parent, OnFolderChanged);
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
                _building = false;
            }
            UpdateSummary();
        }

        private void OnFolderChanged()
        {
            if (_building) return;
            UpdateSummary();
            _changed();
        }

        private IEnumerable<ExportFolderViewModel> AllFolders() => Folders.SelectMany(f => f.SelfAndDescendants());

        /// <summary>Відмічені папки без вкладених у вже відмічені.</summary>
        private IEnumerable<ExportFolderViewModel> Picked => AllFolders().Where(f => f.IsPicked);

        public IReadOnlyList<int> PickedIds => Picked.Select(f => f.Id).ToList();
        public IReadOnlyList<string> PickedNames => Picked.Select(f => f.Name).ToList();

        /// <summary>Усі люди відмічених папок (разом із вкладеними), без повторів і без виключених.</summary>
        public HashSet<int> PeopleIds
        {
            get
            {
                var people = Picked.SelectMany(f => f.Option.PeopleIds).ToHashSet();
                people.ExceptWith(_excludedPeople);
                return people;
            }
        }

        /// <summary>
        /// Папки, які в цьому дереві не відмічаються (прикомандировані в дереві набору), разом із вкладеними:
        /// їхні люди з дерева не беруться, навіть коли відмічена вища папка.
        /// </summary>
        public void Exclude(IReadOnlyCollection<int> folderIds)
        {
            var roots = folderIds as ISet<int> ?? new HashSet<int>(folderIds);
            _excludedPeople = new HashSet<int>();

            void Mark(ExportFolderViewModel folder, bool underExcluded)
            {
                var excluded = underExcluded || roots.Contains(folder.Id);
                if (excluded && !underExcluded) _excludedPeople.UnionWith(folder.Option.PeopleIds);
                folder.IsExcluded = excluded;
                foreach (var child in folder.Children) Mark(child, excluded);
            }

            foreach (var folder in Folders) Mark(folder, false);
            UpdateSummary();
        }

        private void UpdateSummary()
        {
            var picked = Picked.ToList();
            if (picked.Count == 0)
            {
                Summary = "Папок не відмічено.";
                return;
            }

            var people = PeopleIds.Count;
            var left = picked.SelectMany(f => f.Option.PeopleIds).Distinct().Count() - people;
            var without = left > 0
                ? $", без {left} {PluralHelper.Pluralize(left, "прикомандированого", "прикомандированих", "прикомандированих")}"
                : string.Empty;
            Summary = $"{people} {PluralHelper.Pluralize(people, "особа", "особи", "осіб")} "
                      + $"з {picked.Count} {PluralHelper.Pluralize(picked.Count, "папки", "папок", "папок")}, вкладені папки входять{without}.";
        }
    }

    public partial class PersonnelExportDialogViewModel : DialogViewModelBase
    {
        private const string Caption = "Експорт особового складу";

        private static readonly RankCategory[] RankCategories =
        {
            RankCategory.Officers, RankCategory.Sergeants, RankCategory.Soldiers, RankCategory.Unknown
        };

        private readonly IPersonExportService _service;
        private readonly PersonExportRequest _request;
        private readonly List<ExportFieldOptionViewModel> _allFields;

        private IReadOnlyList<PersonExportRow> _rows = Array.Empty<PersonExportRow>();
        private List<PersonExportRow> _matched = new();
        private bool _suppressFieldSync;
        private bool _suppressRecount;

        // Вивантаження аркушами: люди всього дерева й ті, кого дають відмічені папки.
        private IReadOnlyList<PersonExportRow> _treeRows = Array.Empty<PersonExportRow>();
        private HashSet<int> _sheetScope = new();
        private HashSet<int> _secondedIds = new();
        private PersonExportPrefs _savedPrefs = new();

        public PersonnelExportDialogViewModel(IPersonExportService service, PersonExportRequest request)
        {
            _service = service;
            _request = request;

            includeDescendants = request.IncludeDescendants;
            searchText = request.SearchText;

            IntakeFolders = new ExportFolderPickerViewModel("Набір - відмітьте папки", OnSheetFoldersChanged);
            SecondedFolders = new ExportFolderPickerViewModel("Прикомандировані - відмітьте папки", OnSheetFoldersChanged);
            foreach (var kind in PersonExportSheets.Kinds)
            {
                var sheet = new ExportSheetOptionViewModel(kind);
                sheet.PropertyChanged += OnSheetOptionChanged;
                Sheets.Add(sheet);
            }

            _allFields = PersonExportFields.All.Select(f => new ExportFieldOptionViewModel(f)).ToList();
            foreach (var option in _allFields) option.PropertyChanged += OnFieldOptionChanged;

            FieldGroups = _allFields
                .GroupBy(o => o.Field.Group)
                .Select(g => new ExportFieldGroupViewModel(PersonExportFields.GroupTitle(g.Key), g.ToList()))
                .ToList();

            ConditionFields = PersonExportFields.All.Where(f => !f.IsRowNumber).ToList();
            OperatorOptions = new ExportChoice<PersonExportOperator>[]
            {
                new(PersonExportOperator.Contains, "містить"),
                new(PersonExportOperator.NotContains, "не містить"),
                new(PersonExportOperator.EqualTo, "дорівнює"),
                new(PersonExportOperator.IsEmpty, "не заповнено"),
                new(PersonExportOperator.IsNotEmpty, "заповнено")
            };

            FitnessOptions = new ExportChoice<FitnessFilter>[]
            {
                new(FitnessFilter.All, "Усі"),
                new(FitnessFilter.RegularOnly, "Лише придатні"),
                new(FitnessFilter.LimitedOnly, "Лише обмежено придатні")
            };
            selectedFitness = FitnessOptions[0];

            SortOptions = new ExportChoice<PersonExportSort>[]
            {
                new(PersonExportSort.Alphabet, "За абеткою"),
                new(PersonExportSort.Rank, "За званням (старші вище), далі за абеткою"),
                new(PersonExportSort.Folder, "За папками, далі за абеткою")
            };
            selectedSort = SortOptions[0];

            foreach (var category in RankCategories)
            {
                var option = new ExportRankOptionViewModel(category);
                option.PropertyChanged += OnRankOptionChanged;
                RankOptions.Add(option);
            }

            ApplyFieldKeys(PersonExportFields.DefaultKeys);
        }

        public string Title => Caption;

        public bool IsFolderScope => _request.OnlyIds is null;

        /// <summary>Назва вибірки для журналу.</summary>
        public string ScopeTitle
        {
            get
            {
                if (BySheets) return SheetFolderNames();
                return IsFolderScope ? _request.NodePath : $"{_request.NodePath} (позначені)";
            }
        }

        public string ScopeText
        {
            get
            {
                if (BySheets) return $"Аркуші з папок: {SheetFolderNames()}";
                return IsFolderScope
                    ? $"Папка: {_request.NodePath}"
                    : $"Позначені в списку: {_request.OnlyIds!.Count} · папка «{_request.NodeName}»";
            }
        }

        // ---------- аркуші ----------

        /// <summary>Аркуші книги з галочками - у тому порядку, в якому вони стануть у файлі.</summary>
        public ObservableCollection<ExportSheetOptionViewModel> Sheets { get; } = new();

        /// <summary>Папки набору - типово корінь набору, з якого відкрили експорт, інакше останнього активного.</summary>
        public ExportFolderPickerViewModel IntakeFolders { get; }

        /// <summary>Папки прикомандированих - типово «Прикомандировані» того ж набору; у набір вони не входять.</summary>
        public ExportFolderPickerViewModel SecondedFolders { get; }

        /// <summary>Одна книга з кількома аркушами замість одного списку; лише для папки, не для позначених.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSingleList))]
        [NotifyPropertyChangedFor(nameof(ShowIncludeDescendants))]
        [NotifyPropertyChangedFor(nameof(ScopeText))]
        [NotifyCanExecuteChangedFor(nameof(CopyCommand))]
        private bool bySheets;

        public bool IsSingleList => !BySheets;

        public bool ShowIncludeDescendants => IsFolderScope && !BySheets;

        public IReadOnlyList<ExportFieldGroupViewModel> FieldGroups { get; }
        public ObservableCollection<ExportFieldOptionViewModel> SelectedFields { get; } = new();

        public ObservableCollection<ExportRankOptionViewModel> RankOptions { get; } = new();
        public IReadOnlyList<ExportChoice<FitnessFilter>> FitnessOptions { get; }
        public IReadOnlyList<ExportChoice<PersonExportSort>> SortOptions { get; }

        public IReadOnlyList<PersonExportField> ConditionFields { get; }
        public IReadOnlyList<ExportChoice<PersonExportOperator>> OperatorOptions { get; }
        public ObservableCollection<ExportConditionViewModel> Conditions { get; } = new();

        [ObservableProperty]
        private bool includeDescendants;

        [ObservableProperty]
        private string? searchText;

        [ObservableProperty]
        private ExportChoice<FitnessFilter> selectedFitness;

        [ObservableProperty]
        private ExportChoice<PersonExportSort> selectedSort;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SummaryText))]
        [NotifyPropertyChangedFor(nameof(HasNoMatches))]
        [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
        [NotifyCanExecuteChangedFor(nameof(CopyCommand))]
        private int matchCount;

        [ObservableProperty]
        private string? statusNote;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
        [NotifyCanExecuteChangedFor(nameof(CopyCommand))]
        private bool isBusy;

        public bool HasNoMatches => MatchCount == 0;

        public string ColumnsHeader => $"ПОРЯДОК КОЛОНОК ({SelectedFields.Count})";

        public bool HasNoColumns => SelectedFields.Count == 0;

        public bool HasActiveFilters =>
            !string.IsNullOrWhiteSpace(SearchText)
            || SelectedFitness.Value != FitnessFilter.All
            || Conditions.Count > 0
            || RankOptions.Any(o => o.HasPeople && !o.IsChecked);

        public string SummaryText
        {
            get
            {
                var sheets = Sheets.Count(s => s.IsChecked);
                if (BySheets && sheets == 0) return "Відмітьте хоча б один аркуш";
                if (BySheets && _sheetScope.Count == 0) return "Відмітьте папки набору чи прикомандированих";
                if (MatchCount == 0) return "Під ці умови ніхто не підпадає";
                if (SelectedFields.Count == 0) return "Оберіть хоча б одне поле";

                var bySheets = BySheets ? $"{SheetsText(sheets)} · " : string.Empty;
                return $"До експорту: {bySheets}{PeopleText(MatchCount)} · {ColumnsText(SelectedFields.Count)}";
            }
        }

        private bool CanExport => MatchCount > 0 && SelectedFields.Count > 0 && !IsBusy;

        // У буфер обміну йде одна таблиця - книгу з аркушами туди не покласти.
        private bool CanCopy => CanExport && !BySheets;

        /// <summary>Усі, хто пройшов фільтри; у вивантаженні аркушами їх ще ділять по аркушах.</summary>
        internal IReadOnlyList<PersonExportRow> MatchedRows => _matched;

        internal static string PeopleText(int count)
            => $"{count} {PluralHelper.Pluralize(count, "особа", "особи", "осіб")}";

        internal static string ColumnsText(int count)
            => $"{count} {PluralHelper.Pluralize(count, "колонка", "колонки", "колонок")}";

        internal static string SheetsText(int count)
            => $"{count} {PluralHelper.Pluralize(count, "аркуш", "аркуші", "аркушів")}";

        public async Task InitializeAsync()
        {
            _rows = await _service.LoadAsync(_request.NodeId);

            _savedPrefs = await _service.LoadPrefsAsync();
            if (_savedPrefs.Fields.Any(k => PersonExportFields.Find(k) is not null))
                ApplyFieldKeys(_savedPrefs.Fields);
            SelectedSort = SortOptions.FirstOrDefault(o => o.Value == _savedPrefs.Sort) ?? SortOptions[0];

            if (IsFolderScope)
            {
                var tree = await _service.LoadTreeAsync();
                _treeRows = tree.Rows;

                // Як у вечірній перевірці: набір - одна папка, прикомандировані - інша, і в набір вони не входять.
                var intakeFolder = DefaultIntakeFolder(tree, _request.NodeId);
                SecondedFolders.Build(tree.Folders, EveningCheckViewModel.DefaultSecondedFolders(tree.Folders, intakeFolder));
                IntakeFolders.Build(tree.Folders, new[] { intakeFolder });
                IntakeFolders.Exclude(SecondedFolders.PickedIds);
                ReadSheetFolders();

                _suppressRecount = true;
                // Жодного запам'ятованого аркуша - це не «нічого не вивантажувати», а «ще не вибирали».
                if (_savedPrefs.Sheets.Count > 0)
                    foreach (var sheet in Sheets) sheet.IsChecked = _savedPrefs.Sheets.Contains(sheet.Kind);
                BySheets = _savedPrefs.BySheets;
                _suppressRecount = false;
            }

            RefreshRankCounts();
            Recount();
        }

        /// <summary>
        /// Папка набору для аркушів: набір, з якого відкрили експорт (його корінь чи будь-яка вкладена папка),
        /// а коли відкрили не з набору - останній активний набір. Немає жодного набору - сама відкрита папка.
        /// </summary>
        internal static int DefaultIntakeFolder(PersonExportTree tree, int openedId)
        {
            var byId = tree.Folders.ToDictionary(f => f.Id);

            var seen = new HashSet<int>();
            for (int? id = openedId; id is int current && seen.Add(current) && byId.TryGetValue(current, out var folder); id = folder.ParentId)
                if (folder.IsIntakeRoot) return folder.Id;

            return tree.DefaultIntakeRootId is int rootId && byId.ContainsKey(rootId) ? rootId : openedId;
        }

        private string SheetFolderNames()
        {
            var names = SecondedFolders.PickedNames.Concat(IntakeFolders.PickedNames).Distinct().ToList();
            return names.Count == 0 ? "папок не відмічено" : string.Join(", ", names);
        }

        private void ReadSheetFolders()
        {
            _secondedIds = SecondedFolders.PeopleIds;
            _sheetScope = IntakeFolders.PeopleIds;
            _sheetScope.UnionWith(_secondedIds);
        }

        private void OnSheetFoldersChanged()
        {
            // Що відмічено в прикомандированих, те в дереві набору показується невідміченим: це різні списки.
            IntakeFolders.Exclude(SecondedFolders.PickedIds);
            ReadSheetFolders();
            OnPropertyChanged(nameof(ScopeText));
            RefreshRankCounts();
            Recount();
        }

        private void OnSheetOptionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ExportSheetOptionViewModel.IsChecked)) Recount();
        }

        partial void OnBySheetsChanged(bool value)
        {
            RefreshRankCounts();
            Recount();
        }

        // ---------- кого експортувати ----------

        /// <summary>З кого вибираємо: гілка папки, а у вивантаженні аркушами - усе дерево.</summary>
        private IReadOnlyList<PersonExportRow> SourceRows => BySheets ? _treeRows : _rows;

        /// <summary>Перелік людей, що замінює папку: позначені в списку або люди відмічених папок аркушів.</summary>
        private IReadOnlyCollection<int>? ScopeIds => BySheets ? _sheetScope : _request.OnlyIds;

        partial void OnIncludeDescendantsChanged(bool value)
        {
            RefreshRankCounts();
            Recount();
        }

        partial void OnSearchTextChanged(string? value) => Recount();

        partial void OnSelectedFitnessChanged(ExportChoice<FitnessFilter> value) => Recount();

        partial void OnSelectedSortChanged(ExportChoice<PersonExportSort> value) => Recount();

        private void OnRankOptionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ExportRankOptionViewModel.IsChecked)) Recount();
        }

        private void OnConditionChanged(object? sender, PropertyChangedEventArgs e) => Recount();

        [RelayCommand]
        private void AddCondition()
        {
            var condition = new ExportConditionViewModel(ConditionFields, OperatorOptions);
            condition.PropertyChanged += OnConditionChanged;
            Conditions.Add(condition);
            Recount();
        }

        [RelayCommand]
        private void RemoveCondition(ExportConditionViewModel? condition)
        {
            if (condition is null || !Conditions.Remove(condition)) return;
            condition.PropertyChanged -= OnConditionChanged;
            Recount();
        }

        [RelayCommand]
        private void ResetFilters()
        {
            _suppressRecount = true;
            SearchText = null;
            SelectedFitness = FitnessOptions[0];
            foreach (var option in RankOptions) option.IsChecked = true;
            foreach (var condition in Conditions) condition.PropertyChanged -= OnConditionChanged;
            Conditions.Clear();
            _suppressRecount = false;
            Recount();
        }

        private PersonExportFilter BuildFilter()
        {
            // Усі видимі категорії позначені - звання не обмежуємо; так люди з нерозпізнаним званням не губляться.
            var limitedByRank = RankOptions.Any(o => o.HasPeople && !o.IsChecked);

            return new PersonExportFilter
            {
                NodeId = _request.NodeId,
                IncludeDescendants = IncludeDescendants,
                OnlyIds = ScopeIds,
                SearchText = SearchText,
                RankCategories = limitedByRank
                    ? RankOptions.Where(o => o.IsChecked).Select(o => o.Category).ToHashSet()
                    : null,
                Fitness = SelectedFitness.Value,
                Conditions = Conditions.Select(c => c.ToCondition()).ToList()
            };
        }

        private void RefreshRankCounts()
        {
            var scope = PersonExportQuery
                .InScope(SourceRows, new PersonExportFilter
                {
                    NodeId = _request.NodeId,
                    IncludeDescendants = IncludeDescendants,
                    OnlyIds = ScopeIds
                })
                .GroupBy(r => RankOrder.Category(r.Person.Rank))
                .ToDictionary(g => g.Key, g => g.Count());

            foreach (var option in RankOptions)
                option.Count = scope.GetValueOrDefault(option.Category);
        }

        private void Recount()
        {
            if (_suppressRecount) return;

            _matched = PersonExportQuery.Apply(SourceRows, BuildFilter(), SelectedSort.Value);

            foreach (var sheet in Sheets)
            {
                sheet.Rows = BySheets
                    ? PersonExportSheets.Rows(sheet.Kind, _matched, _secondedIds)
                    : Array.Empty<PersonExportRow>();
                sheet.Count = sheet.Rows.Count;
            }

            // Одна людина буває на кількох аркушах («Усі слухачі» і «Набір») - рахуємо її раз.
            MatchCount = BySheets
                ? Sheets.Where(s => s.IsChecked).SelectMany(s => s.Rows).Select(r => r.Person.Id).Distinct().Count()
                : _matched.Count;
            StatusNote = null;
            OnPropertyChanged(nameof(HasActiveFilters));
            // Кількість людей могла лишитися тією самою, а аркушів чи папок стало інакше.
            OnPropertyChanged(nameof(SummaryText));
        }

        // ---------- які поля ----------

        private void OnFieldOptionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_suppressFieldSync || e.PropertyName != nameof(ExportFieldOptionViewModel.IsChecked)) return;

            var option = (ExportFieldOptionViewModel)sender!;
            if (option.IsChecked)
            {
                if (!SelectedFields.Contains(option)) SelectedFields.Add(option);
            }
            else
            {
                SelectedFields.Remove(option);
            }

            OnColumnsChanged();
        }

        private void ApplyFieldKeys(IEnumerable<string> keys)
        {
            _suppressFieldSync = true;
            foreach (var option in _allFields) option.IsChecked = false;
            SelectedFields.Clear();

            foreach (var key in keys)
            {
                var option = _allFields.FirstOrDefault(o => o.Field.Key == key);
                if (option is null || option.IsChecked) continue;
                option.IsChecked = true;
                SelectedFields.Add(option);
            }

            _suppressFieldSync = false;
            OnColumnsChanged();
        }

        private void OnColumnsChanged()
        {
            StatusNote = null;
            OnPropertyChanged(nameof(ColumnsHeader));
            OnPropertyChanged(nameof(HasNoColumns));
            OnPropertyChanged(nameof(SummaryText));
            SaveCommand.NotifyCanExecuteChanged();
            CopyCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private void SelectAllFields()
        {
            // Уже обрані лишаються на своїх місцях, решта дописується в кінець у порядку переліку.
            ApplyFieldKeys(SelectedFields.Select(o => o.Field.Key)
                .Concat(_allFields.Select(o => o.Field.Key))
                .ToList());
        }

        [RelayCommand]
        private void ClearFields() => ApplyFieldKeys(Array.Empty<string>());

        [RelayCommand]
        private void DefaultFields() => ApplyFieldKeys(PersonExportFields.DefaultKeys);

        [RelayCommand]
        private void MoveFieldUp(ExportFieldOptionViewModel? option) => MoveField(option, -1);

        [RelayCommand]
        private void MoveFieldDown(ExportFieldOptionViewModel? option) => MoveField(option, 1);

        private void MoveField(ExportFieldOptionViewModel? option, int shift)
        {
            if (option is null) return;
            var index = SelectedFields.IndexOf(option);
            var target = index + shift;
            if (index < 0 || target < 0 || target >= SelectedFields.Count) return;

            SelectedFields.Move(index, target);
            StatusNote = null;
        }

        [RelayCommand]
        private void RemoveField(ExportFieldOptionViewModel? option)
        {
            if (option is not null) option.IsChecked = false;
        }

        // ---------- експорт ----------

        private IReadOnlyList<PersonExportField> ChosenFields() => SelectedFields.Select(o => o.Field).ToList();

        // Для позначених у списку аркушів немає - те, що запам'ятали про аркуші раніше, лишається як було.
        private PersonExportPrefs CurrentPrefs() => new()
        {
            Fields = SelectedFields.Select(o => o.Field.Key).ToList(),
            Sort = SelectedSort.Value,
            BySheets = IsFolderScope ? BySheets : _savedPrefs.BySheets,
            Sheets = IsFolderScope
                ? Sheets.Where(s => s.IsChecked).Select(s => s.Kind).ToList()
                : _savedPrefs.Sheets
        };

        /// <summary>Відмічені аркуші з їхніми людьми; порожній аркуш теж іде у файл - із самою шапкою.</summary>
        internal IReadOnlyList<PersonExportSheet> BuildSheets() => Sheets
            .Where(s => s.IsChecked)
            .Select(s => new PersonExportSheet(PersonExportSheets.SheetName(s.Kind), s.Rows))
            .ToList();

        internal string SuggestedFileName()
        {
            var name = string.Concat(_request.NodeName.Split(System.IO.Path.GetInvalidFileNameChars())).Trim();
            var middle = name.Length == 0 ? string.Empty : $"_{name}";
            return $"особовий_склад{middle}_{DateTime.Now:ddMMyyyy}.xlsx";
        }

        internal async Task<ExportResult> ExportToFileAsync(string filePath)
        {
            var result = BySheets
                ? await _service.SaveXlsxAsync(BuildSheets(), ChosenFields(), filePath, ScopeTitle)
                : await _service.SaveXlsxAsync(_matched, ChosenFields(), filePath, ScopeTitle);
            if (result.Success) await _service.SavePrefsAsync(CurrentPrefs());
            return result;
        }

        internal string BuildClipboardText()
            => PersonExportTable.Build(_matched, ChosenFields()).ToClipboardText();

        [RelayCommand(CanExecute = nameof(CanExport))]
        private async Task SaveAsync()
        {
            var dialog = new SaveFileDialog
            {
                FileName = SuggestedFileName(),
                Filter = "Excel файли (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx"
            };
            if (dialog.ShowDialog() != true || CloudFolderGuard.Blocks(dialog.FileName)) return;

            IsBusy = true;
            ExportResult result;
            try
            {
                result = await ExportToFileAsync(dialog.FileName);
            }
            finally
            {
                IsBusy = false;
            }

            if (!result.Success)
            {
                MessageBox.Show(
                    $"Не вдалося зберегти файл:\n{result.ErrorMessage}\n\nМожливо, він зараз відкритий в Excel.",
                    Caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var onSheets = BySheets ? $" · {SheetsText(Sheets.Count(s => s.IsChecked))}" : string.Empty;
            var open = MessageBox.Show(
                $"Експортовано {PeopleText(result.RowCount)}{onSheets}:\n{result.FilePath}\n\nВідкрити файл?",
                Caption, MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (open == MessageBoxResult.Yes)
            {
                try { Process.Start(new ProcessStartInfo(result.FilePath!) { UseShellExecute = true }); }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
            }

            CloseDialog(true);
        }

        [RelayCommand(CanExecute = nameof(CanCopy))]
        private async Task CopyAsync()
        {
            var count = _matched.Count;
            var fields = ChosenFields();

            try
            {
                Clipboard.SetText(BuildClipboardText());
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                MessageBox.Show(
                    "Буфер обміну зайнятий іншою програмою. Спробуйте ще раз.",
                    "Не вдалося скопіювати", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _service.LogClipboardExport(count, fields, ScopeTitle);
            await _service.SavePrefsAsync(CurrentPrefs());
            StatusNote = $"Скопійовано {PeopleText(count)} - вставте в Excel чи Word (Ctrl+V)";
        }

        [RelayCommand]
        private void Cancel() => CloseDialog(false);
    }
}
