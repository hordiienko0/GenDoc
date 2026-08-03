using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Services;
using GenDoc.Services.Documents;
using GenDoc.Services.OrgTree;
using GenDoc.ViewModels.Personnel;

namespace GenDoc.ViewModels.Trash
{
    public partial class DeletedFolderRowViewModel : ObservableObject
    {
        public DeletedFolderRowViewModel(DeletedFolderInfo info)
        {
            Info = info;
        }

        public DeletedFolderInfo Info { get; }
        public string Name => Info.Name;
        public string DeletedAtDisplay => Info.DeletedAt.ToString("dd.MM.yyyy HH:mm");
        public string DeletedByDisplay => Info.DeletedBy ?? "—";
        public bool ParentDead => !Info.ParentAlive;
    }

    public partial class DeletedDocumentRowViewModel : ObservableObject
    {
        public DeletedDocumentRowViewModel(DeletedDocumentInfo info)
        {
            Info = info;
        }

        public DeletedDocumentInfo Info { get; }
        public string Title => $"{Info.Person} — {Info.TemplateName} (в.{Info.Version})";
        public string DeletedAtDisplay => Info.DeletedAt.ToString("dd.MM.yyyy HH:mm");
        public string DeletedByDisplay => Info.DeletedBy ?? "—";
    }

    public partial class DeletedGroupDocumentRowViewModel : ObservableObject
    {
        public DeletedGroupDocumentRowViewModel(DeletedGroupDocumentInfo info)
        {
            Info = info;
        }

        public DeletedGroupDocumentInfo Info { get; }
        public string Title => $"{Info.TemplateName} — {Info.RecipientCount} осіб (в.{Info.Version})";
        public string DeletedAtDisplay => Info.DeletedAt.ToString("dd.MM.yyyy HH:mm");
        public string DeletedByDisplay => Info.DeletedBy ?? "—";
    }

    public partial class TrashViewModel : ObservableObject
    {
        private readonly IOrgTreeService _orgTreeService;
        private readonly IDocumentArchiveService _archiveService;
        private readonly IDialogService _dialogService;
        private readonly OrgTreeViewModel _tree;

        public TrashViewModel(
            IOrgTreeService orgTreeService,
            IDocumentArchiveService archiveService,
            IDialogService dialogService,
            OrgTreeViewModel tree)
        {
            _orgTreeService = orgTreeService;
            _archiveService = archiveService;
            _dialogService = dialogService;
            _tree = tree;
            _ = LoadAsync();
        }

        public ObservableCollection<DeletedFolderRowViewModel> Folders { get; } = new();
        public ObservableCollection<DeletedDocumentRowViewModel> Documents { get; } = new();
        public ObservableCollection<DeletedGroupDocumentRowViewModel> GroupDocuments { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasFolders))]
        private int folderCount;

        public bool HasFolders => FolderCount > 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasDocuments))]
        private int documentCount;

        public bool HasDocuments => DocumentCount > 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasGroupDocuments))]
        private int groupDocumentCount;

        public bool HasGroupDocuments => GroupDocumentCount > 0;

        public async Task LoadAsync()
        {
            var folders = await _orgTreeService.GetDeletedFoldersAsync();
            Folders.Clear();
            foreach (var folder in folders)
                Folders.Add(new DeletedFolderRowViewModel(folder));
            FolderCount = Folders.Count;

            var documents = await _archiveService.GetDeletedDocumentsAsync();
            Documents.Clear();
            foreach (var document in documents)
                Documents.Add(new DeletedDocumentRowViewModel(document));
            DocumentCount = Documents.Count;

            var groupDocuments = await _archiveService.GetDeletedGroupDocumentsAsync();
            GroupDocuments.Clear();
            foreach (var document in groupDocuments)
                GroupDocuments.Add(new DeletedGroupDocumentRowViewModel(document));
            GroupDocumentCount = GroupDocuments.Count;
        }

        [RelayCommand]
        private async Task RestoreDocumentAsync(DeletedDocumentRowViewModel? row)
        {
            if (row is null) return;
            await _archiveService.RestoreAsync(row.Info.Id);
            await LoadAsync();
        }

        [RelayCommand]
        private async Task RestoreGroupDocumentAsync(DeletedGroupDocumentRowViewModel? row)
        {
            if (row is null) return;
            await _archiveService.RestoreGroupAsync(row.Info.Id);
            await LoadAsync();
        }

        [RelayCommand]
        private async Task RestoreAsync(DeletedFolderRowViewModel? row)
        {
            if (row is null) return;

            int? newParentId = null;
            if (row.ParentDead)
            {
                await _tree.EnsureLoadedAsync();
                var picker = NodePickerDialogViewModel.ForRestoreParent(_tree);
                if (_dialogService.ShowDialog(picker, Application.Current.MainWindow) != true
                    || picker.SelectedTargetId is not int targetId) return;
                newParentId = targetId;
            }

            await _orgTreeService.RestoreAsync(row.Info.Id, newParentId);
            await _tree.ReloadAsync();
            WeakReferenceMessenger.Default.Send(new CountsChangedMessage());
            await LoadAsync();
        }
    }
}
