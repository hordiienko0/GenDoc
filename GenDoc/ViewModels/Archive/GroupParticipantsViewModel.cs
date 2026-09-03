using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Services.Documents;
using GenDoc.ViewModels.Personnel;

namespace GenDoc.ViewModels.Archive
{
    public partial class GroupParticipantsViewModel : DialogViewModelBase
    {
        public GroupParticipantsViewModel(string title, IReadOnlyList<GroupParticipantDto> participants)
        {
            Title = title;
            foreach (var p in participants) Participants.Add(p);
            EmptyNote = participants.Count == 0
                ? "Склад не записано: документ згенеровано до оновлення. Після перегенерації склад з'явиться."
                : null;
        }

        public string Title { get; }
        public ObservableCollection<GroupParticipantDto> Participants { get; } = new();
        public string? EmptyNote { get; }
        public bool HasParticipants => Participants.Count > 0;
        public string CountText => HasParticipants ? $"Осіб у складі: {Participants.Count}" : string.Empty;

        [RelayCommand]
        private void Close() => CloseDialog(true);
    }
}
