using CommunityToolkit.Mvvm.ComponentModel;
using GenDoc.Models.Enums;
using GenDoc.Services.Staff;

namespace GenDoc.ViewModels.Staff
{
    public partial class StaffRowViewModel : ObservableObject
    {
        public StaffRowViewModel(StaffRowOverview overview)
        {
            Id = overview.Id;
            FullName = overview.FullName;
            Rank = overview.Rank;
            Position = overview.Position;
            UnitName = overview.UnitName;
            CurrentState = overview.CurrentState;
            DocsCount = overview.DocsCount;
        }

        public int Id { get; }
        public string FullName { get; }
        public string Rank { get; }
        public string Position { get; }
        public string? UnitName { get; }
        public StaffEventKind? CurrentState { get; }
        public int DocsCount { get; }

        public string StateText => CurrentState switch
        {
            StaffEventKind.BusinessTrip => "У відрядженні",
            StaffEventKind.Leave => "У відпустці",
            _ => "На місці"
        };

        [ObservableProperty]
        private bool isChecked;
    }
}
