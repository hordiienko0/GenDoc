using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GenDoc.Services;
using GenDoc.Services.Duty;

namespace GenDoc.ViewModels.Duty;

/// <summary>
/// Папка в дереві «Хто в графіку»: відмічена папка бере в новий графік своїх людей разом із вкладеними папками.
/// Вкладені у відмічену показуються відміченими й неактивними - вони вже входять.
/// </summary>
public partial class DutyFolderViewModel : ObservableObject
{
    private readonly Action _changed;

    public DutyFolderViewModel(DutyFolderOption option, DutyFolderViewModel? parent, Action changed)
    {
        Option = option;
        Parent = parent;
        _changed = changed;
    }

    public DutyFolderOption Option { get; }
    public DutyFolderViewModel? Parent { get; }
    public int Id => Option.Id;
    public string Name => Option.Name;
    public ObservableCollection<DutyFolderViewModel> Children { get; } = new();

    public string CountText => Option.PeopleIds.Count == 0
        ? "порожня"
        : $"{Option.PeopleIds.Count} {PluralHelper.Pluralize(Option.PeopleIds.Count, "особа", "особи", "осіб")}";

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsTicked))] private bool _isChecked;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsTicked), nameof(CanTick))] private bool _isCovered;

    /// <summary>Галочка на екрані: відмічена сама або входить у відмічену вищу папку.</summary>
    public bool IsTicked
    {
        get => IsChecked || IsCovered;
        set { if (!IsCovered) IsChecked = value; }
    }

    public bool CanTick => !IsCovered;

    /// <summary>Відмічена сама, а не через вищу папку - саме такі йдуть у запит на графік.</summary>
    public bool IsPicked => IsChecked && !IsCovered;

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

    public IEnumerable<DutyFolderViewModel> SelfAndDescendants()
    {
        yield return this;
        foreach (var descendant in Children.SelectMany(c => c.SelfAndDescendants()))
            yield return descendant;
    }
}
