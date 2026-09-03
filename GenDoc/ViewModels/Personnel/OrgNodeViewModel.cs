using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GenDoc.Models;
using GenDoc.Models.Enums;

namespace GenDoc.ViewModels.Personnel
{
    public partial class OrgNodeViewModel : ObservableObject
    {
        private readonly OrgTreeViewModel _owner;

        public OrgNodeViewModel(OrgNode node, OrgTreeViewModel owner)
        {
            _owner = owner;
            Id = node.Id;
            name = node.Name;
            DocumentName = node.DocumentName;
            Path = node.Path;
            Depth = node.Depth;
            ParentId = node.ParentId;
            IntakeId = node.IntakeId;
        }

        public int Id { get; }
        public string Path { get; set; }
        public int Depth { get; set; }
        public int? ParentId { get; set; }
        public int? IntakeId { get; set; }

        public ObservableCollection<OrgNodeViewModel> Children { get; } = new();

        [ObservableProperty]
        private string name;

        public string? DocumentName { get; set; }

        [ObservableProperty]
        private bool isExpanded;

        [ObservableProperty]
        private bool isVisibleInFilter = true;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CountLabel))]
        [NotifyPropertyChangedFor(nameof(CountTooltip))]
        private int ownCount;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CountLabel))]
        [NotifyPropertyChangedFor(nameof(CountTooltip))]
        private int totalCount;

        public string CountLabel => $"{OwnCount} / {TotalCount}";

        public string CountTooltip =>
            $"У самому підрозділі: {OwnCount}\nРазом із вкладеними: {TotalCount}";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusGlyph))]
        private bool isIntakeRoot;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusGlyph))]
        private IntakeStatus? intakeStatus;

        [ObservableProperty]
        private bool isCompletedBranch;

        [ObservableProperty]
        private bool isActiveIntakeBranch;

        public string StatusGlyph => !IsIntakeRoot ? "" : IntakeStatus switch
        {
            Models.Enums.IntakeStatus.Active => "●",
            Models.Enums.IntakeStatus.Completed => "○",
            _ => "◌"
        };

        public bool IsRoot => ParentId is null;
        public bool CanAddChild => true;
        public bool CanRename => !IsIntakeRoot;
        public bool CanMove => !IsRoot && !IsIntakeRoot;
        public bool CanDelete => !IsRoot;

        [ObservableProperty]
        private bool isSelected;

        partial void OnIsSelectedChanged(bool value)
        {
            if (value) _owner.OnNodeSelected(this);
        }
    }
}
