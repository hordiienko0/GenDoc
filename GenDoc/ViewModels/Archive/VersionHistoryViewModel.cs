using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Models.Enums;
using GenDoc.Services.Documents;
using GenDoc.ViewModels.Personnel;
using Microsoft.Win32;

namespace GenDoc.ViewModels.Archive
{
    public partial class VersionRowViewModel : ObservableObject
    {
        private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("uk-UA");

        public VersionRowViewModel(DocumentVersionDto dto, VersionHistoryViewModel owner)
        {
            Dto = dto;
            Owner = owner;
        }

        public DocumentVersionDto Dto { get; }
        public VersionHistoryViewModel Owner { get; }

        public string VersionText => $"в.{Dto.Version}";
        public string DateText => Dto.CreatedAt.ToString("dd.MM.yyyy HH:mm", Uk);
        public string Author => Dto.Author;
        public string SizeText => Dto.SizeBytes > 0 ? $"{Dto.SizeBytes / 1024.0:0.#} КБ" : "—";
        public string SourceText => Dto.SourceType switch
        {
            DocumentSourceType.ManualUpload => "завантажено вручну",
            DocumentSourceType.ScannedCopy => "скан",
            _ => "згенеровано"
        };
        public bool IsCurrent => Dto.IsCurrent;
        public bool CanOpen => Dto.HasContent;
        public bool CanMakeCurrent => !Dto.IsCurrent;
    }

    public partial class AttachmentRowViewModel : ObservableObject
    {
        private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("uk-UA");

        public AttachmentRowViewModel(AttachmentDto dto)
        {
            Dto = dto;
        }

        public AttachmentDto Dto { get; }
        public string FileName => Dto.FileName;
        public string DateText => Dto.UploadedAt.ToString("dd.MM.yyyy HH:mm", Uk);
        public string NoteText => string.IsNullOrWhiteSpace(Dto.Note) ? "—" : Dto.Note!;
    }

    public partial class VersionHistoryViewModel : DialogViewModelBase
    {
        private readonly IDocumentArchiveService _archiveService;
        private readonly int _recipientId;
        private readonly int _templateId;
        private readonly int _documentId;

        public VersionHistoryViewModel(
            IDocumentArchiveService archiveService,
            int recipientId, int templateId, int documentId,
            string personName, string templateName)
        {
            _archiveService = archiveService;
            _recipientId = recipientId;
            _templateId = templateId;
            _documentId = documentId;
            HeaderText = $"{personName} — {templateName}";
        }

        public string HeaderText { get; }

        // Список оновлює рядок точково після закриття, якщо були зміни.
        public bool HasChanges { get; private set; }

        public ObservableCollection<VersionRowViewModel> Versions { get; } = new();
        public ObservableCollection<AttachmentRowViewModel> Attachments { get; } = new();

        [ObservableProperty]
        private bool hasAttachments;

        public async Task InitializeAsync()
        {
            Versions.Clear();
            foreach (var version in await _archiveService.GetVersionsAsync(_recipientId, _templateId))
                Versions.Add(new VersionRowViewModel(version, this));

            Attachments.Clear();
            foreach (var attachment in await _archiveService.GetAttachmentsAsync(_documentId))
                Attachments.Add(new AttachmentRowViewModel(attachment));
            HasAttachments = Attachments.Count > 0;
        }

        [RelayCommand]
        private async Task OpenVersionAsync(VersionRowViewModel? row)
        {
            if (row is null || !row.CanOpen) return;
            try
            {
                await _archiveService.OpenAsync(row.Dto.Id);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                MessageBox.Show(
                    "Не вдалося відкрити: немає програми для цього типу файлу. Скористайтесь «Зберегти як…».",
                    "Відкриття документа", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        private async Task SaveVersionAsAsync(VersionRowViewModel? row)
        {
            if (row is null || !row.CanOpen) return;

            var dialog = new SaveFileDialog { FileName = row.Dto.FileName };
            if (dialog.ShowDialog() != true) return;

            await _archiveService.SaveAsAsync(row.Dto.Id, dialog.FileName);
        }

        [RelayCommand]
        private async Task MakeCurrentAsync(VersionRowViewModel? row)
        {
            if (row is null || !row.CanMakeCurrent) return;

            var confirm = MessageBox.Show(
                $"Зробити версію {row.VersionText} актуальною?",
                "Зміна актуальної версії", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            await _archiveService.MakeCurrentAsync(row.Dto.Id);
            HasChanges = true;
            await InitializeAsync();
        }

        [RelayCommand]
        private async Task OpenAttachmentAsync(AttachmentRowViewModel? row)
        {
            if (row is null) return;
            try
            {
                await _archiveService.OpenAttachmentAsync(row.Dto.Id);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                MessageBox.Show(
                    "Не вдалося відкрити: немає програми для цього типу файлу. Скористайтесь «Зберегти як…».",
                    "Відкриття вкладення", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        private async Task SaveAttachmentAsAsync(AttachmentRowViewModel? row)
        {
            if (row is null) return;

            var dialog = new SaveFileDialog { FileName = row.Dto.FileName };
            if (dialog.ShowDialog() != true) return;

            await _archiveService.SaveAttachmentAsAsync(row.Dto.Id, dialog.FileName);
        }

        [RelayCommand]
        private async Task DeleteAttachmentAsync(AttachmentRowViewModel? row)
        {
            if (row is null) return;

            var confirm = MessageBox.Show(
                $"Видалити вкладення «{row.FileName}»?",
                "Видалення вкладення", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            await _archiveService.DeleteAttachmentAsync(row.Dto.Id);
            HasChanges = true;
            await InitializeAsync();
        }

        [RelayCommand]
        private void Close() => CloseDialog(HasChanges);
    }
}
