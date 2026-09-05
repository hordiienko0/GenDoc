using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Services.Audit;

namespace GenDoc.ViewModels.Audit;

public partial class AuditLogViewModel : ObservableObject
{
    private const int PageSize = 200;

    private readonly IAuditLogQueryService _queryService;
    private readonly DispatcherTimer _searchDebounceTimer;
    private bool _suppressAutoQuery;
    private int _skip;

    public AuditLogViewModel(IAuditLogQueryService queryService)
    {
        _queryService = queryService;

        ProfileOptions = BuildOptions("Усі профілі", queryService.GetProfiles());
        ActionOptions = BuildOptions("Усі дії", queryService.GetActions());

        _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchDebounceTimer.Tick += (_, _) =>
        {
            _searchDebounceTimer.Stop();
            RunQuery(reset: true);
        };

        RunQuery(reset: true);
    }

    [ObservableProperty]
    private ObservableCollection<AuditLogListItem> entries = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool hasEntries;

    public bool IsEmpty => !HasEntries;

    [ObservableProperty]
    private bool canLoadMore;

    [ObservableProperty]
    private DateTime? selectedFrom;

    [ObservableProperty]
    private DateTime? selectedTo;

    [ObservableProperty]
    private string? selectedProfile;

    [ObservableProperty]
    private string? selectedAction;

    [ObservableProperty]
    private string? searchText;

    public ObservableCollection<AuditFilterOption> ProfileOptions { get; }
    public ObservableCollection<AuditFilterOption> ActionOptions { get; }

    partial void OnSelectedFromChanged(DateTime? value) => TriggerQuery();
    partial void OnSelectedToChanged(DateTime? value) => TriggerQuery();
    partial void OnSelectedProfileChanged(string? value) => TriggerQuery();
    partial void OnSelectedActionChanged(string? value) => TriggerQuery();

    partial void OnSearchTextChanged(string? value)
    {
        if (_suppressAutoQuery) return;
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    internal void ApplySearchNow()
    {
        _searchDebounceTimer.Stop();
        RunQuery(reset: true);
    }

    private void TriggerQuery()
    {
        if (_suppressAutoQuery) return;
        RunQuery(reset: true);
    }

    [RelayCommand]
    private void Reset()
    {
        _suppressAutoQuery = true;
        SelectedFrom = null;
        SelectedTo = null;
        SelectedProfile = null;
        SelectedAction = null;
        SearchText = null;
        _searchDebounceTimer.Stop();
        _suppressAutoQuery = false;

        RunQuery(reset: true);
    }

    [RelayCommand]
    private void LoadMore() => RunQuery(reset: false);

    private void RunQuery(bool reset)
    {
        if (reset)
        {
            _skip = 0;
            Entries = new ObservableCollection<AuditLogListItem>();
        }

        var from = SelectedFrom.HasValue ? DateOnly.FromDateTime(SelectedFrom.Value) : (DateOnly?)null;
        var to = SelectedTo.HasValue ? DateOnly.FromDateTime(SelectedTo.Value) : (DateOnly?)null;

        var filter = new AuditLogFilter(from, to, SelectedProfile, SelectedAction, _skip, PageSize, SearchText);
        var results = _queryService.Query(filter);

        foreach (var item in results)
            Entries.Add(item);

        _skip += results.Count;
        CanLoadMore = results.Count == PageSize;
        HasEntries = Entries.Count > 0;
    }

    private static ObservableCollection<AuditFilterOption> BuildOptions(string allLabel, IEnumerable<string> values)
    {
        var options = new ObservableCollection<AuditFilterOption> { new(null, allLabel) };
        foreach (var value in values)
            options.Add(new AuditFilterOption(value, value));

        return options;
    }
}
