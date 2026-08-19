using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Services.Documents;
using Microsoft.Win32;

namespace GenDoc.ViewModels.Staff
{
    // Один рядок кроку «Документи» у StaffDocDialog - і щойно згенерований, і
    // будь-який раніше створений документ для обраних людей (перечитується через
    // IDocumentArchiveService.GetCurrentRowAsync після генерації).
    public partial class StaffDocResultRowViewModel : ObservableObject
    {
        private readonly IDocumentArchiveService _archiveService;
        private readonly int? _documentId;
        private readonly bool _hasContent;

        public StaffDocResultRowViewModel(
            IDocumentArchiveService archiveService, int? documentId, bool hasContent, string fileName,
            string recipientName, string templateName, bool success, string? errorMessage)
        {
            _archiveService = archiveService;
            _documentId = documentId;
            _hasContent = hasContent;
            FileName = fileName;
            RecipientName = recipientName;
            TemplateName = templateName;
            Success = success;
            ErrorMessage = errorMessage;
        }

        public string FileName { get; }
        public string RecipientName { get; }
        public string TemplateName { get; }
        public bool Success { get; }
        public string? ErrorMessage { get; }

        public bool CanOpen => Success && _documentId is not null && _hasContent;

        [RelayCommand(CanExecute = nameof(CanOpen))]
        private async Task OpenAsync()
        {
            var result = await _archiveService.OpenAsync(_documentId!.Value);
            if (!result.Success)
                MessageBox.Show(result.ErrorMessage, "Відкриття документа",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        [RelayCommand(CanExecute = nameof(CanOpen))]
        private async Task SaveAsAsync()
        {
            var dialog = new SaveFileDialog { FileName = FileName };
            if (dialog.ShowDialog() != true) return;

            var result = await _archiveService.SaveAsAsync(_documentId!.Value, dialog.FileName);
            if (!result.Success)
                MessageBox.Show(result.ErrorMessage, "Зберегти як",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
